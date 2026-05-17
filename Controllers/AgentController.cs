using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.DTOs;
using MovieApi.Models;
using Pgvector.EntityFrameworkCore;
using MovieApi.Services;

namespace MovieApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AgentController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IVertexAIService _vertexAI;

        public AgentController(ApplicationDbContext context, IVertexAIService vertexAI)
        {
            _context = context;
            _vertexAI = vertexAI;
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] AgentQueryRequest request)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("admin");
            var stopwatch = Stopwatch.StartNew();

            string question = request.Question.Trim();
            string questionLower = question.ToLower();

            // === DETECTAR SI ES PETICIÓN DE CREACIÓN ===
            string[] creationKeywords = { "registra", "añade", "agrega", "crea", "inserta" };
            bool isCreationRequest = creationKeywords.Any(k => questionLower.Contains(k));

            if (isCreationRequest)
            {
                var createResult = await TryCreateMovie(question, userId, stopwatch);
                if (createResult != null)
                {
                    stopwatch.Stop();
                    var agentQuery = new AgentQuery
                    {
                        UserId = userId,
                        Question = request.Question,
                        Answer = createResult,
                        ResponseLatencyMs = (int)stopwatch.ElapsedMilliseconds,
                        ResultsCount = 1,
                        HasResults = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.AgentQueries.Add(agentQuery);
                    await _context.SaveChangesAsync();
                    return Ok(new AgentQueryResponse { Answer = createResult, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }
            }

            // === RECOPILAR CONTEXTO ===
            var metrics = await _context.VDashboardSummaries.AsNoTracking().FirstOrDefaultAsync();
            var userProfile = await _context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId);

            var userMovies = await _context.Movies
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(20)
                .Select(m => new { m.Title, m.Genre, m.Director, m.Year, m.InsertionStatus, m.InsertionLatencyMs })
                .ToListAsync();

            var userLists = await _context.MovieLists
                .Where(l => l.UserId == userId)
                .Select(l => new { l.Name, l.Description })
                .ToListAsync();

            var userFavorites = await _context.FavoriteMovies
                .Where(f => f.UserId == userId)
                .Include(f => f.Movie)
                .Select(f => new { Title = f.Movie!.Title, f.Movie.Genre })
                .ToListAsync();

            var recentErrors = await _context.OperationLogs
                .Where(l => l.Status == "error" && (isAdmin || l.UserId == userId))
                .OrderByDescending(l => l.CreatedAt)
                .Take(10)
                .Select(l => new { l.Action, l.ErrorMessage, l.CreatedAt })
                .ToListAsync();

            var agentHistory = await _context.AgentQueries
                .Where(q => q.UserId == userId)
                .OrderByDescending(q => q.CreatedAt)
                .Take(5)
                .OrderBy(q => q.CreatedAt)
                .Select(q => new { q.Question, q.Answer })
                .ToListAsync();

            var vectorCount = await _context.Movies.CountAsync(m => m.Embedding != null);
            var moviesWithoutVector = await _context.Movies
                .Where(m => m.Embedding == null && (m.UserId == userId || isAdmin))
                .Select(m => m.Title)
                .ToListAsync();

            var allMoviesAdmin = isAdmin
                ? await _context.Movies.OrderByDescending(m => m.CreatedAt).Take(50)
                    .Select(m => new { m.Title, m.Genre, m.Director, m.Year, m.InsertionStatus }).ToListAsync()
                : null;

            // === CONSTRUIR SYSTEM PROMPT ===
            var moviesText = userMovies.Any()
                ? string.Join(", ", userMovies.Select(m => $"'{m.Title}' ({m.Genre}, {m.Year}, {m.InsertionStatus})"))
                : "ninguna película registrada aún";

            var listsText = userLists.Any() ? string.Join(", ", userLists.Select(l => $"'{l.Name}'")) : "sin listas";
            var favoritesText = userFavorites.Any() ? string.Join(", ", userFavorites.Select(f => f.Title)) : "sin favoritos";
            var errorsText = recentErrors.Any()
                ? string.Join(" | ", recentErrors.Select(e => $"{e.Action}: {e.ErrorMessage} ({e.CreatedAt:dd/MM HH:mm})"))
                : "sin errores recientes";
            var historyText = agentHistory.Any()
                ? string.Join("\n", agentHistory.Select(h => $"Usuario: {h.Question}\nAgente: {h.Answer}"))
                : "";
            var adminSection = isAdmin && allMoviesAdmin != null
                ? $"\nCOMO ADMIN VES TODAS ({allMoviesAdmin.Count}): {string.Join(", ", allMoviesAdmin.Select(m => m.Title))}"
                : "";

            var systemPrompt = $@"Eres CineNova AI, asistente inteligente del catálogo de películas CineNova.
Respondes en español, de forma natural y conversacional. Usas exactamente los datos que se te dan, nunca inventas.

USUARIO: {userProfile?.FullName ?? "Usuario"} | Rol: {(isAdmin ? "Administrador" : "Cliente")}

SUS PELÍCULAS ({userMovies.Count}): {moviesText}
SUS LISTAS: {listsText}
SUS FAVORITOS: {favoritesText}

MÉTRICAS DEL SISTEMA:
- Total películas: {metrics?.TotalMovies} | Usuarios: {metrics?.TotalUsers}
- Latencia inserción: {metrics?.AvgInsertionLatencyMs}ms | Latencia semántica: {metrics?.AvgQueryLatencyMs}ms
- Tasa éxito: {metrics?.SuccessRatePct}% | Errores: {metrics?.TotalErrors} | Duplicados: {metrics?.TotalDuplicates}
- Vectores almacenados: {vectorCount} | Sin vector: {moviesWithoutVector.Count}
- Consultas al agente: {metrics?.TotalAgentQueries}
{adminSection}

ERRORES RECIENTES: {errorsText}

HISTORIAL:
{historyText}

INSTRUCCIONES:
- Responde directo, sin saludos repetidos si hay historial
- Usa datos reales, nunca estimes ni inventes
- Si preguntan géneros, analiza la lista de películas del usuario
- Si no tienes el dato, dilo honestamente
- Para registrar películas: el sistema ya lo maneja automáticamente, no necesitas mencionarlo";

            var answer = await _vertexAI.GetChatResponseAsync(
                request.Question,
                systemPrompt,
                agentHistory.Select(h => new ConversationTurn { Question = h.Question, Answer = h.Answer }).ToList()
            );

            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;

            _context.AgentQueries.Add(new AgentQuery
            {
                UserId = userId,
                Question = request.Question,
                Answer = answer,
                ResponseLatencyMs = latency,
                ResultsCount = 1,
                HasResults = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return Ok(new AgentQueryResponse { Answer = answer, Data = null, LatencyMs = latency });
        }

        private async Task<string?> TryCreateMovie(string message, Guid userId, Stopwatch stopwatch)
        {
            try
            {
                // Extraer título
                string title = ExtractField(message, new[] { "llamada", "titulada", "título", "titulo", "película" }, new[] { "género", "genero", "director", "año", "sinopsis", "," });
                string genre = ExtractField(message, new[] { "género", "genero" }, new[] { "director", "año", "sinopsis", "," });
                string director = ExtractField(message, new[] { "director" }, new[] { "año", "sinopsis", "género", "genero", "," });
                string synopsis = ExtractSynopsis(message);
                int year = ExtractYear(message);

                // Validar campos obligatorios
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(title)) missing.Add("título");
                if (string.IsNullOrWhiteSpace(genre)) missing.Add("género");
                if (string.IsNullOrWhiteSpace(director)) missing.Add("director");
                if (string.IsNullOrWhiteSpace(synopsis)) missing.Add("sinopsis");
                if (year == 0) missing.Add("año");

                if (missing.Any())
                    return $"Para registrar la película necesito que me indiques: {string.Join(", ", missing)}. Por favor completa la información.";

                // Verificar duplicado
                var exists = await _context.Movies
                    .AnyAsync(m => m.Title.ToLower() == title.ToLower() && m.UserId == userId);
                if (exists)
                    return $"Ya tienes una película registrada con el título '{title}'. ¿Quieres registrar otra con un título diferente?";

                // Generar embedding
                var embeddingInput = $"{title} {genre} {director} {synopsis}";
                var embeddingStart = stopwatch.ElapsedMilliseconds;
                var embeddingValues = await _vertexAI.GetEmbeddingAsync(embeddingInput);
                var embeddingLatency = (int)(stopwatch.ElapsedMilliseconds - embeddingStart);

                var movie = new Movie
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = title.Trim(),
                    Genre = genre.Trim(),
                    Director = director.Trim(),
                    Year = year,
                    Synopsis = synopsis.Trim(),
                    Embedding = new Pgvector.Vector(embeddingValues),
                    EmbeddingLatencyMs = embeddingLatency,
                    InsertionLatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    InsertionStatus = "success",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Movies.Add(movie);
                await _context.SaveChangesAsync();

                return $"✅ Película registrada exitosamente:\n- **Título:** {movie.Title}\n- **Género:** {movie.Genre}\n- **Director:** {movie.Director}\n- **Año:** {movie.Year}\n- **Sinopsis:** {movie.Synopsis}\n- **Tiempo de inserción:** {movie.InsertionLatencyMs}ms\n- **Embedding generado:** {embeddingLatency}ms";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AgentCreate Error] {ex.Message}");
                return null;
            }
        }

        private string ExtractField(string text, string[] startMarkers, string[] endMarkers)
        {
            string lower = text.ToLower();
            foreach (var marker in startMarkers)
            {
                int idx = lower.IndexOf(marker);
                if (idx < 0) continue;
                int start = idx + marker.Length;
                // saltar separadores ": " o " "
                while (start < text.Length && (text[start] == ':' || text[start] == ' ')) start++;
                int end = text.Length;
                foreach (var endMarker in endMarkers)
                {
                    int endIdx = lower.IndexOf(endMarker, start);
                    if (endIdx > start && endIdx < end) end = endIdx;
                }
                var value = text.Substring(start, end - start).Trim().TrimEnd(',').Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "";
        }

        private string ExtractSynopsis(string text)
        {
            string lower = text.ToLower();
            string[] markers = { "sinopsis:", "sinopsis " };
            foreach (var marker in markers)
            {
                int idx = lower.IndexOf(marker);
                if (idx >= 0)
                {
                    int start = idx + marker.Length;
                    return text.Substring(start).Trim().TrimEnd('.').Trim();
                }
            }
            return "";
        }

        private int ExtractYear(string text)
        {
            var match = Regex.Match(text, @"\b(19[0-9]{2}|20[0-9]{2})\b");
            if (match.Success && int.TryParse(match.Value, out int year)) return year;
            return 0;
        }
    }
}