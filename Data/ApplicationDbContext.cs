using Microsoft.EntityFrameworkCore;
using MovieApi.Models;
using MovieApi.Models.Views;

namespace MovieApi.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        // Tables
        public DbSet<Role> Roles { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<Movie> Movies { get; set; }
        public DbSet<MovieReview> MovieReviews { get; set; }
        public DbSet<FavoriteMovie> FavoriteMovies { get; set; }
        public DbSet<MovieList> MovieLists { get; set; }
        public DbSet<MovieListItem> MovieListItems { get; set; }
        public DbSet<OperationLog> OperationLogs { get; set; }
        public DbSet<AgentQuery> AgentQueries { get; set; }

        // Views
        public DbSet<VDashboardSummary> VDashboardSummaries { get; set; }
        public DbSet<VMoviesPerUser> VMoviesPerUsers { get; set; }
        public DbSet<VRecentMovie> VRecentMovies { get; set; }
        public DbSet<VErrorsByAction> VErrorsByActions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasPostgresExtension("vector");

            // --- Tables Configuration ---

            modelBuilder.Entity<Role>(entity => {
                entity.HasIndex(e => e.Name).IsUnique();
            });

            modelBuilder.Entity<Profile>(entity => {
                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasIndex(e => e.Username).IsUnique();
            });

            modelBuilder.Entity<UserRole>(entity => {
                entity.HasIndex(e => new { e.UserId, e.RoleId }).IsUnique();
                
                entity.HasOne(d => d.Profile)
                    .WithMany(p => p.UserRoles)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Role)
                    .WithMany()
                    .HasForeignKey(d => d.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Movie>(entity => {
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_movies_created_at");

                entity.Property(e => e.IngestionDate)
                    .HasColumnType("date")
                    .ValueGeneratedOnAddOrUpdate(); // Computed column handled by DB

                entity.Property(e => e.Embedding)
                    .HasColumnType("vector(768)");

                entity.HasOne(d => d.Profile)
                    .WithMany()
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MovieReview>(entity => {
                entity.HasIndex(e => new { e.MovieId, e.UserId }).IsUnique();

                entity.Property(e => e.Rating)
                    .HasPrecision(3, 1);

                entity.ToTable("movie_reviews", "public", t => t.HasCheckConstraint("movie_reviews_rating_check", "rating >= 0 AND rating <= 10"));

                entity.HasOne(d => d.Movie)
                    .WithMany()
                    .HasForeignKey(d => d.MovieId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Profile)
                    .WithMany()
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FavoriteMovie>(entity => {
                entity.HasIndex(e => new { e.UserId, e.MovieId }).IsUnique();

                entity.HasOne(d => d.Profile)
                    .WithMany()
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Movie)
                    .WithMany()
                    .HasForeignKey(d => d.MovieId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MovieList>(entity => {
                entity.HasOne(d => d.Profile)
                    .WithMany()
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MovieListItem>(entity => {
                entity.HasIndex(e => new { e.ListId, e.MovieId }).IsUnique();

                entity.HasOne(d => d.MovieList)
                    .WithMany()
                    .HasForeignKey(d => d.ListId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Movie)
                    .WithMany()
                    .HasForeignKey(d => d.MovieId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OperationLog>(entity => {
                entity.HasIndex(e => e.UserId).HasDatabaseName("idx_logs_user");
                entity.HasIndex(e => e.Status).HasDatabaseName("idx_logs_status");
                entity.HasIndex(e => e.Action).HasDatabaseName("idx_logs_action");
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_logs_date");

                entity.HasOne(d => d.Profile)
                    .WithMany()
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<AgentQuery>(entity => {
                entity.HasIndex(e => e.UserId).HasDatabaseName("idx_agent_user");
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_agent_date");

                entity.HasOne(d => d.Profile)
                    .WithMany()
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // --- Views Configuration (Keyless Entities) ---

            modelBuilder.Entity<VDashboardSummary>(entity => {
                entity.HasNoKey();
                entity.ToView("v_dashboard_summary", "public");
            });

            modelBuilder.Entity<VMoviesPerUser>(entity => {
                entity.HasNoKey();
                entity.ToView("v_movies_per_user", "public");
            });

            modelBuilder.Entity<VRecentMovie>(entity => {
                entity.HasNoKey();
                entity.ToView("v_recent_movies", "public");
            });

            modelBuilder.Entity<VErrorsByAction>(entity => {
                entity.HasNoKey();
                entity.ToView("v_errors_by_action", "public");
            });
        }
    }
}
