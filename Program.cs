using Microsoft.OpenApi.Models;
using MovieApi.Data;
using Microsoft.EntityFrameworkCore;
using MovieApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using MovieApi.Models;
using Microsoft.AspNetCore.Authentication.Cookies;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Movie API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Database connection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, o => {
        o.UseVector();
        o.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
        o.CommandTimeout(10);
    }));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:3001")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});


// ... (previous code)

builder.Services.AddHttpClient();
builder.Services.AddScoped<IAuthService, AuthService>();

// Authentication Configuration
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.Cookie.Name = "CineNovaAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
    });

// (Keep JwtBearer if API still needs it, but for MVC, Cookies are better)
// ...

var app = builder.Build();

// Global Exception Middleware
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? userId = userIdStr != null ? Guid.Parse(userIdStr) : null;

            var log = new OperationLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Action = "system_error",
                Status = "error",
                ErrorMessage = ex.Message,
                Metadata = JsonSerializer.Serialize(new { 
                    path = context.Request.Path.Value, 
                    method = context.Request.Method,
                    stackTrace = ex.StackTrace 
                }),
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                dbContext.OperationLogs.Add(log);
                await dbContext.SaveChangesAsync();
            }
            catch { }
        }

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message, code = 500 }));
        }
        else
        {
            throw; // Let MVC handle non-API errors or redirect
        }
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();

// Force en-US culture for consistent decimal parsing
var supportedCultures = new[] { "en-US" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);
app.UseRequestLocalization(localizationOptions);

app.UseRouting();

app.UseCors("AllowLocalhost");

app.UseAuthentication();
app.UseAuthorization();

// --- AUTO-FIX: Asegurar que la columna password existe y rating soporta 10.0 ---
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try {
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.profiles ADD COLUMN IF NOT EXISTS password text NOT NULL DEFAULT 'temporal';");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.profiles ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.movie_reviews ALTER COLUMN rating TYPE numeric(3,1);");
        
        // Ensure movies table has latency and status columns
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.movies ADD COLUMN IF NOT EXISTS insertion_latency_ms integer;");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.movies ADD COLUMN IF NOT EXISTS embedding_latency_ms integer;");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.movies ADD COLUMN IF NOT EXISTS insertion_status text DEFAULT 'success';");

        // Fix rating constraint to allow 0-10
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.movie_reviews DROP CONSTRAINT IF EXISTS movie_reviews_rating_check;");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.movie_reviews ADD CONSTRAINT movie_reviews_rating_check CHECK (rating >= 0 AND rating <= 10);");
        
        // Ensure operation_logs has all required columns
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.operation_logs ADD COLUMN IF NOT EXISTS latency_ms integer;");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.operation_logs ADD COLUMN IF NOT EXISTS records_affected integer DEFAULT 0;");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.operation_logs ADD COLUMN IF NOT EXISTS error_code text;");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.operation_logs ADD COLUMN IF NOT EXISTS metadata jsonb;");

        // Fix operation_logs constraint to allow system_error and others
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.operation_logs DROP CONSTRAINT IF EXISTS operation_logs_action_check;");
        
        // --- CREATE DASHBOARD VIEWS ---
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE OR REPLACE VIEW public.v_dashboard_summary AS
            SELECT 
                (SELECT COUNT(*) FROM public.movies) as total_movies,
                (SELECT COUNT(*) FROM public.profiles) as total_users,
                (SELECT COUNT(DISTINCT ur.user_id) FROM public.user_roles ur JOIN public.roles r ON ur.role_id = r.id WHERE r.name = 'client') as total_clients,
                (SELECT COUNT(*) FROM public.operation_logs WHERE status = 'error') as total_errors,
                (SELECT COUNT(*) FROM public.agent_queries) as total_agent_queries,
                CASE WHEN (SELECT COUNT(*) FROM public.operation_logs) = 0 THEN 100 
                     ELSE ROUND((SELECT COUNT(*) FROM public.operation_logs WHERE status = 'success')::numeric / NULLIF((SELECT COUNT(*) FROM public.operation_logs), 0)::numeric * 100, 2) 
                END as success_rate_pct,
                COALESCE(ROUND((SELECT AVG(insertion_latency_ms) FROM public.movies)::numeric, 2), 0) as avg_insertion_latency_ms,
                COALESCE(ROUND((SELECT AVG(response_latency_ms) FROM public.agent_queries)::numeric, 2), 0) as avg_query_latency_ms,
                (SELECT COUNT(*) FROM public.operation_logs WHERE action = 'create_movie' AND status = 'error' AND metadata::text LIKE '%duplicate%') as total_duplicates,
                (SELECT COUNT(*) FROM public.movies WHERE vector_id IS NOT NULL) as total_vectors_stored
            FROM (SELECT 1) dummy;
        ");

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE OR REPLACE VIEW public.v_movies_per_user AS
            SELECT 
                p.id as user_id, 
                p.email, 
                p.username, 
                p.full_name,
                COUNT(m.id) as movie_count,
                (SELECT COUNT(*) FROM public.operation_logs ol WHERE ol.user_id = p.id AND ol.status = 'error' AND NOT (ol.action = 'create_movie' AND ol.metadata::text LIKE '%duplicate%')) as error_count,
                (SELECT COUNT(*) FROM public.operation_logs ol WHERE ol.user_id = p.id AND ol.action = 'create_movie' AND ol.status = 'error' AND ol.metadata::text LIKE '%duplicate%') as duplicate_count
            FROM public.profiles p
            LEFT JOIN public.movies m ON p.id = m.user_id
            GROUP BY p.id, p.email, p.username, p.full_name;
        ");

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE OR REPLACE VIEW public.v_recent_movies AS
            SELECT 
                m.id, m.title, m.genre, m.director, m.year, m.insertion_status, 
                m.insertion_latency_ms, m.embedding_latency_ms, m.ingestion_date, m.created_at,
                p.email as user_email, p.username
            FROM public.movies m
            JOIN public.profiles p ON m.user_id = p.id
            ORDER BY m.created_at DESC
            LIMIT 50;
        ");

        await context.Database.ExecuteSqlRawAsync(@"
            CREATE OR REPLACE VIEW public.v_errors_by_action AS
            SELECT 
                action,
                COUNT(*) as total,
                COUNT(*) FILTER (WHERE status = 'error') as errors,
                COUNT(*) FILTER (WHERE status = 'success') as successes,
                COALESCE(ROUND(AVG(latency_ms)::numeric, 2), 0) as avg_latency_ms
            FROM public.operation_logs
            GROUP BY action;
        ");
        
        Console.WriteLine("[System] DB Fix: Columna 'password', 'rating', 'latency_ms', constraints y vistas del dashboard ajustadas.");
    } catch (Exception ex) {
        Console.WriteLine($@"[System] DB Fix Error: {ex.Message}");
    }
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
