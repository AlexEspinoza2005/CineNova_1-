using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.Models;
using System.Diagnostics;
using System.Text.Json;

namespace MovieApi.Controllers
{
    [Authorize]
    public class FavoritesViewController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FavoritesViewController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Add(Guid movieId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var movie = await _context.Movies.FindAsync(movieId);
                var movieTitle = movie?.Title ?? movieId.ToString();

                var existing = await _context.FavoriteMovies
                    .FirstOrDefaultAsync(f => f.UserId == userId && f.MovieId == movieId);

                if (existing == null)
                {
                    var favorite = new FavoriteMovie
                    {
                        UserId = userId,
                        MovieId = movieId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.FavoriteMovies.Add(favorite);
                    await _context.SaveChangesAsync();
                }

                stopwatch.Stop();
                var logTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"));
                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "add_favorite",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha agregado la película {movieTitle} a favoritos a las {logTime:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { movieId = movieId, movieTitle = movieTitle }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "add_favorite",
                    Status = "error",
                    ErrorMessage = $"Error al agregar favorito: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Details", "MoviesView", new { id = movieId });
        }

        [HttpPost]
        public async Task<IActionResult> Remove(Guid movieId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var movie = await _context.Movies.FindAsync(movieId);
                var movieTitle = movie?.Title ?? movieId.ToString();

                var favorite = await _context.FavoriteMovies
                    .FirstOrDefaultAsync(f => f.UserId == userId && f.MovieId == movieId);

                if (favorite != null)
                {
                    _context.FavoriteMovies.Remove(favorite);
                    await _context.SaveChangesAsync();
                }

                stopwatch.Stop();
                var logTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"));
                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "remove_favorite",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha eliminado la película {movieTitle} de favoritos a las {logTime:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { movieId = movieId, movieTitle = movieTitle }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "remove_favorite",
                    Status = "error",
                    ErrorMessage = $"Error al eliminar de favoritos: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Details", "MoviesView", new { id = movieId });
        }
    }
}
