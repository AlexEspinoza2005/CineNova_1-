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
    public class MoviesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public MoviesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("admin");

            IQueryable<Movie> query = _context.Movies;

            if (!isAdmin)
            {
                query = query.Where(m => m.UserId == userId);
            }

            var movies = await query.ToListAsync();
            return Ok(movies);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("admin");

            var movie = await _context.Movies.FindAsync(id);

            if (movie == null) return NotFound();
            if (!isAdmin && movie.UserId != userId) return Forbid();

            return Ok(movie);
        }

        [HttpPost]
        [Authorize(Roles = "client")]
        public async Task<IActionResult> Create([FromBody] CreateMovieDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var stopwatch = Stopwatch.StartNew();
            
            var movie = new Movie
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = dto.Title,
                Genre = dto.Genre,
                Director = dto.Director,
                Year = dto.Year,
                Synopsis = dto.Synopsis,
                DurationMin = dto.DurationMin,
                CoverUrl = dto.CoverUrl,
                InsertionStatus = "success",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var log = new OperationLog
            {
                UserId = userId,
                Action = "insert_movie",
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                _context.Movies.Add(movie);
                await _context.SaveChangesAsync();
                
                stopwatch.Stop();
                movie.InsertionLatencyMs = (int)stopwatch.ElapsedMilliseconds;
                
                // Update latency in DB
                await _context.SaveChangesAsync();

                log.Status = "success";
                log.LatencyMs = movie.InsertionLatencyMs;
                log.RecordsAffected = 1;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                log.Status = "error";
                log.ErrorMessage = ex.Message;
                log.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            }
            finally
            {
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            if (log.Status == "error") return BadRequest(new { message = log.ErrorMessage });

            return CreatedAtAction(nameof(GetById), new { id = movie.Id }, movie);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMovieDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var movie = await _context.Movies.FindAsync(id);

            if (movie == null) return NotFound();
            if (movie.UserId != userId) return Forbid();

            if (dto.Title != null) movie.Title = dto.Title;
            if (dto.Genre != null) movie.Genre = dto.Genre;
            if (dto.Director != null) movie.Director = dto.Director;
            if (dto.Year != null) movie.Year = dto.Year.Value;
            if (dto.Synopsis != null) movie.Synopsis = dto.Synopsis;
            if (dto.DurationMin != null) movie.DurationMin = dto.DurationMin;
            if (dto.CoverUrl != null) movie.CoverUrl = dto.CoverUrl;

            movie.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(movie);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("admin");

            var movie = await _context.Movies.FindAsync(id);

            if (movie == null) return NotFound();
            if (!isAdmin && movie.UserId != userId) return Forbid();

            _context.Movies.Remove(movie);
            
            var log = new OperationLog
            {
                UserId = userId,
                Action = "delete_movie",
                Status = "success",
                CreatedAt = DateTime.UtcNow,
                RecordsAffected = 1
            };

            _context.OperationLogs.Add(log);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }
}
