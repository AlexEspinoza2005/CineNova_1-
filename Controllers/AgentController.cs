using System.Diagnostics;
using System.Security.Claims;
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
            
            string question = request.Question.ToLower().Trim();
            string answer = "";
            object? data = null;
            int resultsCount = 0;
            bool isIntentDetected = false;

            // --- PASO 1: RECOGER CONTEXTO DINÁMICO ---
            var metrics = await _context.VDashboardSummaries.AsNoTracking().FirstOrDefaultAsync();
            var movieCount = await _context.Movies.CountAsync(m => m.UserId == userId);
            
            string contextData = $"Métricas actuales: Total películas global={metrics?.TotalMovies}, " +
                                $"Tus películas={movieCount}, Latencia media={metrics?.AvgInsertionLatencyMs}ms, " +
                                $"Vectores totales={metrics?.TotalVectorsStored}, Errores totales={metrics?.TotalErrors}.";

            // --- PASO 2: GESTIÓN DE INTENCIONES ESPECÍFICAS ---

            // 1. Errores reales
            if (question.Contains("error") || question.Contains("fallo"))
            {
                isIntentDetected = true;
                var logs = await _context.OperationLogs
                    .Where(l => l.Status == "error" && (isAdmin || l.UserId == userId))
                    .OrderByDescending(l => l.CreatedAt)
                    .Take(5)
                    .Select(l => new { l.Action, l.ErrorMessage, l.CreatedAt })
                    .ToListAsync();
                
                if (logs.Any()) {
                    contextData += $" He encontrado estos errores reales en los logs: {string.Join(" | ", logs.Select(l => $"{l.Action}: {l.ErrorMessage}"))}.";
                    data = logs;
                    resultsCount = logs.Count;
                } else {
                    contextData += " No hay errores registrados recientemente.";
                }
            }

            // 2. Películas similares (ILIKE + Vector si es posible)
            if (question.Contains("similares a"))
            {
                isIntentDetected = true;
                string searchTitle = question.Split("similares a").Last().Trim();
                
                // Primero por texto (ILIKE)
                var similarByText = await _context.Movies
                    .Where(m => (m.UserId == userId || isAdmin) && EF.Functions.ILike(m.Title, $"%{searchTitle}%"))
                    .Take(5)
                    .Select(m => new { m.Title, m.Genre, m.Year })
                    .ToListAsync();

                if (similarByText.Any()) {
                    contextData += $" Encontré estas películas similares por título: {string.Join(", ", similarByText.Select(s => s.Title))}.";
                    data = similarByText;
                    resultsCount = similarByText.Count;
                } else {
                    // Intento por vector si no hay match de texto
                    try {
                        var vectorValues = await _vertexAI.GetEmbeddingAsync(searchTitle);
                        var queryVector = new Pgvector.Vector(vectorValues);
                        var similarByVector = await _context.Movies
                            .Where(m => m.UserId == userId || isAdmin)
                            .OrderBy(m => m.Embedding!.L2Distance(queryVector))
                            .Take(3)
                            .Select(m => new { m.Title, m.Genre, m.Year })
                            .ToListAsync();
                        
                        if (similarByVector.Any()) {
                            contextData += $" No encontré coincidencias exactas, pero estas son semánticamente similares: {string.Join(", ", similarByVector.Select(s => s.Title))}.";
                            data = similarByVector;
                            resultsCount = similarByVector.Count;
                        }
                    } catch { }
                }
            }

            // 3. Mis favoritos
            if (question.Contains("mis favoritos") || question.Contains("mis películas favoritas"))
            {
                isIntentDetected = true;
                var favorites = await _context.FavoriteMovies
                    .Where(f => f.UserId == userId)
                    .Include(f => f.Movie)
                    .Select(f => new { Title = f.Movie!.Title, f.Movie.Genre, f.Movie.Year })
                    .ToListAsync();

                if (favorites.Any()) {
                    contextData += $" Tus películas favoritas son: {string.Join(", ", favorites.Select(f => f.Title))}.";
                    data = favorites;
                    resultsCount = favorites.Count;
                } else {
                    contextData += " Aún no tienes películas marcadas como favoritas.";
                }
            }

            // 4. Cuántos vectores
            if (question.Contains("cuántos vectores") || question.Contains("total de vectores"))
            {
                isIntentDetected = true;
                var vectorCount = await _context.Movies.CountAsync(m => m.Embedding != null);
                contextData += $" Actualmente hay un total de {vectorCount} películas con vectores generados en la base de datos.";
                resultsCount = 1;
            }

            // 5. Películas sin vector
            if (question.Contains("películas sin vector") || question.Contains("sin procesar"))
            {
                isIntentDetected = true;
                var noVectorMovies = await _context.Movies
                    .Where(m => m.Embedding == null && (m.UserId == userId || isAdmin))
                    .Select(m => m.Title)
                    .ToListAsync();

                if (noVectorMovies.Any()) {
                    contextData += $" Hay {noVectorMovies.Count} películas sin vector: {string.Join(", ", noVectorMovies.Take(10))}.";
                    resultsCount = noVectorMovies.Count;
                } else {
                    contextData += " Todas las películas tienen sus vectores generados correctamente.";
                }
            }

            // 6. Métricas y registros (General)
            if (question.Contains("métricas") || question.Contains("registros") || question.Contains("cuántos") || question.Contains("resumen"))
            {
                isIntentDetected = true;
                // El contextData ya tiene la info fresca del inicio del método
            }

            // --- PASO 3: RESPUESTA INTELIGENTE ---
            
            // Check for previous interaction to avoid repeated greeting
            var lastQuery = await _context.AgentQueries
                .Where(q => q.UserId == userId)
                .OrderByDescending(q => q.CreatedAt)
                .FirstOrDefaultAsync();
            
            bool isFollowUp = lastQuery != null && (DateTime.UtcNow - lastQuery.CreatedAt).TotalMinutes < 10;

            if (!isIntentDetected && !question.Contains("hola") && !question.Contains("ayuda") && !question.Contains("crear") && !question.Contains("añade"))
            {
                answer = "No entendí tu pregunta. Puedes preguntarme sobre tus registros, latencia, errores, favoritos o películas similares.";
            }
            else
            {
                string modifiedContext = contextData;
                if (isFollowUp) {
                    modifiedContext += " INSTRUCCIÓN: No saludes de nuevo, ve directo al grano ya que es una conversación en curso.";
                }

                answer = await _vertexAI.GetChatResponseAsync(request.Question, modifiedContext);
            }

            // --- PASO 4: ACCIONES AUTOMÁTICAS (Registrar película) ---
            string[] creationKeywords = { "crear", "registra", "añade", "agrega", "inserta" };
            bool isCreationRequest = creationKeywords.Any(k => question.Contains(k + " ") || question.EndsWith(k) || (k == "registra" && question.Contains("registra una")));

            if (isCreationRequest && !question.Contains("cuántos") && !question.Contains("ver") && !question.Contains("cuál"))
            {
                try {
                    string title = "";
                    if (question.Contains("llamada")) title = question.Split("llamada").Last().Trim();
                    else if (question.Contains("titulada")) title = question.Split("titulada").Last().Trim();
                    else if (question.Contains("película")) title = question.Split("película").Last().Trim();
                    else if (question.Contains("registra")) title = question.Split("registra").Last().Trim();
                    else if (question.Contains("crear")) title = question.Split("crear").Last().Trim();

                    string[] prefixes = { "un ", "una ", "de ", "sobre ", "titulada ", "llamada " };
                    foreach (var p in prefixes) if (title.StartsWith(p)) title = title.Substring(p.Length).Trim();

                    if (!string.IsNullOrEmpty(title) && title.Length >= 2)
                    {
                        if (title.Length > 50) title = title.Substring(0, 47) + "...";
                        var movieEmbeddingValues = await _vertexAI.GetEmbeddingAsync(title);
                        var newMovie = new Movie {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            Title = title.ToUpper(),
                            Genre = "IA Generated",
                            Director = "CineNova AI v2.5",
                            Year = DateTime.Now.Year,
                            Synopsis = $"Registro automático vía Chat. Consulta: '{request.Question}'",
                            Embedding = new Pgvector.Vector(movieEmbeddingValues),
                            EmbeddingLatencyMs = (int)(stopwatch.ElapsedMilliseconds / 2),
                            InsertionStatus = "success",
                            CreatedAt = DateTime.UtcNow
                        };

                        _context.Movies.Add(newMovie);
                        await _context.SaveChangesAsync();
                        answer += "\n\n✅ **[IA ACTION]** He registrado la película en la base de datos vectorial.";
                    }
                } catch {
                    answer += "\n\n⚠️ (No pude completar el registro automático, verifica los permisos de Vertex AI).";
                }
            }

            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;

            // Persist
            var agentQuery = new AgentQuery {
                UserId = userId,
                Question = request.Question,
                Answer = answer,
                ResponseLatencyMs = latency,
                ResultsCount = resultsCount,
                HasResults = resultsCount > 0,
                CreatedAt = DateTime.UtcNow
            };

            _context.AgentQueries.Add(agentQuery);
            await _context.SaveChangesAsync();

            return Ok(new AgentQueryResponse {
                Answer = answer,
                Data = data,
                LatencyMs = latency
            });
        }
    }
}
