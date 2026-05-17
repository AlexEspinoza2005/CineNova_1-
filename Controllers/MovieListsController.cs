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
    public class MovieListsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public MovieListsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetLists()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var lists = await _context.MovieLists
                .Where(l => l.IsPublic || l.UserId == userId)
                .ToListAsync();
            return Ok(lists);
        }

        [HttpPost]
        public async Task<IActionResult> CreateList([FromBody] CreateMovieListDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var list = new MovieList
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = dto.Name,
                Description = dto.Description,
                IsPublic = dto.IsPublic,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.MovieLists.Add(list);
            await _context.SaveChangesAsync();

            return Ok(list);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateList(Guid id, [FromBody] CreateMovieListDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var list = await _context.MovieLists.FindAsync(id);

            if (list == null) return NotFound();
            if (list.UserId != userId) return Forbid();

            list.Name = dto.Name;
            list.Description = dto.Description;
            list.IsPublic = dto.IsPublic;
            list.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(list);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteList(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var list = await _context.MovieLists.FindAsync(id);

            if (list == null) return NotFound();
            if (list.UserId != userId) return Forbid();

            _context.MovieLists.Remove(list);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{id}/movies/{movieId}")]
        public async Task<IActionResult> AddMovieToList(Guid id, Guid movieId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var list = await _context.MovieLists.FindAsync(id);

            if (list == null) return NotFound();
            if (list.UserId != userId) return Forbid();

            var existing = await _context.MovieListItems
                .FirstOrDefaultAsync(i => i.ListId == id && i.MovieId == movieId);

            if (existing != null) return Ok(existing);

            var item = new MovieListItem
            {
                ListId = id,
                MovieId = movieId,
                CreatedAt = DateTime.UtcNow
            };

            _context.MovieListItems.Add(item);
            await _context.SaveChangesAsync();

            return Ok(item);
        }

        [HttpDelete("{id}/movies/{movieId}")]
        public async Task<IActionResult> RemoveMovieFromList(Guid id, Guid movieId)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var list = await _context.MovieLists.FindAsync(id);

            if (list == null) return NotFound();
            if (list.UserId != userId) return Forbid();

            var item = await _context.MovieListItems
                .FirstOrDefaultAsync(i => i.ListId == id && i.MovieId == movieId);

            if (item == null) return NotFound();

            _context.MovieListItems.Remove(item);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
