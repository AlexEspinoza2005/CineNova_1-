using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.Models;

namespace MovieApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class FavoriteMoviesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public FavoriteMoviesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyFavorites()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var favorites = await _context.FavoriteMovies
                .Include(f => f.Movie)
                .Where(f => f.UserId == userId)
                .ToListAsync();
            return Ok(favorites);
        }

        [HttpPost("{movieId}")]
        public async Task<IActionResult> AddFavorite(Guid movieId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var existing = await _context.FavoriteMovies
                .FirstOrDefaultAsync(f => f.UserId == userId && f.MovieId == movieId);

            if (existing != null) return Ok(existing);

            var favorite = new FavoriteMovie
            {
                UserId = userId,
                MovieId = movieId,
                CreatedAt = DateTime.UtcNow
            };

            _context.FavoriteMovies.Add(favorite);
            await _context.SaveChangesAsync();

            return Ok(favorite);
        }

        [HttpDelete("{movieId}")]
        public async Task<IActionResult> RemoveFavorite(Guid movieId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var favorite = await _context.FavoriteMovies
                .FirstOrDefaultAsync(f => f.UserId == userId && f.MovieId == movieId);

            if (favorite == null) return NotFound();

            _context.FavoriteMovies.Remove(favorite);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
