using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.DTOs;
using MovieApi.Models;

namespace MovieApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AgentController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AgentController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] AgentQueryRequest request)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("admin");
            var stopwatch = Stopwatch.StartNew();
            
            string question = request.Question.ToLower();
            string answer = "Lo siento, no entiendo esa pregunta. Intenta con 'cuántos registros', 'últimos', 'errores', 'latencia' o 'similares'.";
            object? data = null;
            int resultsCount = 0;

            if (question.Contains("cuántos registros") || question.Contains("mis registros"))
            {
                var count = await _context.Movies.CountAsync(m => m.UserId == userId);
                answer = $"Has ingresado un total de {count} películas.";
                resultsCount = 1;
                data = new { total = count };
            }
            else if (question.Contains("últimos") || question.Contains("recientes"))
            {
                var movies = await _context.Movies
                    .Where(m => m.UserId == userId)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(5)
                    .Select(m => new { m.Title, m.Genre, m.CreatedAt })
                    .ToListAsync();
                
                answer = movies.Any() ? "Aquí están tus últimos 5 registros." : "Aún no has ingresado ninguna película.";
                resultsCount = movies.Count;
                data = movies;
            }
            else if (question.Contains("errores"))
            {
                var errors = await _context.OperationLogs
                    .Where(l => l.UserId == userId && l.Status == "error")
                    .OrderByDescending(l => l.CreatedAt)
                    .Take(5)
                    .ToListAsync();
                
                answer = errors.Any() ? "He encontrado estos errores recientes en tus operaciones." : "No se han detectado errores en tus registros.";
                resultsCount = errors.Count;
                data = errors;
            }
            else if (question.Contains("promedio") || question.Contains("latencia"))
            {
                var avg = await _context.Movies
                    .Where(m => m.UserId == userId && m.InsertionLatencyMs.HasValue)
                    .AverageAsync(m => (double?)m.InsertionLatencyMs) ?? 0;
                
                answer = $"Tu tiempo promedio de inserción es de {Math.Round(avg, 2)} ms.";
                resultsCount = 1;
                data = new { average_latency_ms = avg };
            }
            else if (question.Contains("más registros") || question.Contains("quién más"))
            {
                if (isAdmin)
                {
                    var ranking = await _context.VMoviesPerUsers.Take(5).ToListAsync();
                    answer = "Aquí tienes el ranking de los 5 usuarios con más registros.";
                    resultsCount = ranking.Count;
                    data = ranking;
                }
                else
                {
                    answer = "Lo siento, solo los administradores pueden consultar métricas globales de otros usuarios.";
                }
            }
            else if (question.Contains("similares"))
            {
                // Simple ILIKE search for similarity (as a fallback for semantic search)
                var term = question.Replace("similares", "").Replace("a", "").Trim();
                var search = await _context.Movies
                    .Where(m => (m.UserId == userId || isAdmin) && EF.Functions.ILike(m.Title, $"%{term}%"))
                    .Take(5)
                    .ToListAsync();
                
                answer = search.Any() ? $"He encontrado estos títulos similares a '{term}'." : $"No encontré nada parecido a '{term}'.";
                resultsCount = search.Count;
                data = search;
            }

            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;

            // Persist the query
            var agentQuery = new AgentQuery
            {
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

            return Ok(new AgentQueryResponse
            {
                Answer = answer,
                Data = data,
                LatencyMs = latency
            });
        }
    }
}
