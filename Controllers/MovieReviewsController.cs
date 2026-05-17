using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.DTOs;
using MovieApi.Models;

namespace MovieApi.Controllers
{
    [ApiController]
    [Route("api")]
    public class MovieReviewsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public MovieReviewsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("movies/{movieId}/reviews")]
        public async Task<IActionResult> GetMovieReviews(Guid movieId)
        {
            var reviews = await _context.MovieReviews
                .Where(r => r.MovieId == movieId)
                .ToListAsync();
            return Ok(reviews);
        }

        [Authorize]
        [HttpPost("movies/{movieId}/reviews")]
        public async Task<IActionResult> CreateReview(Guid movieId, [FromBody] CreateReviewDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var existingReview = await _context.MovieReviews
                .FirstOrDefaultAsync(r => r.MovieId == movieId && r.UserId == userId);
            if (existingReview != null)
                return BadRequest(new { message = "Ya has reseñado esta película." });

            var now = DateTime.UtcNow;   // ✅ siempre UTC
            var review = new MovieReview
            {
                Id = Guid.NewGuid(),
                MovieId = movieId,
                UserId = userId,
                Rating = dto.Rating,
                Review = dto.Review,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.MovieReviews.Add(review);
            await _context.SaveChangesAsync();
            return Ok(review);
        }

        [Authorize]
        [HttpPut("reviews/{id}")]
        public async Task<IActionResult> UpdateReview(Guid id, [FromBody] CreateReviewDto dto)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var review = await _context.MovieReviews.FindAsync(id);
            if (review == null) return NotFound();
            if (review.UserId != userId) return Forbid();

            review.Rating = dto.Rating;
            review.Review = dto.Review;
            review.UpdatedAt = DateTime.UtcNow;   // ✅ siempre UTC

            await _context.SaveChangesAsync();
            return Ok(review);
        }

        [Authorize]
        [HttpDelete("reviews/{id}")]
        public async Task<IActionResult> DeleteReview(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var review = await _context.MovieReviews.FindAsync(id);
            if (review == null) return NotFound();
            if (review.UserId != userId) return Forbid();

            _context.MovieReviews.Remove(review);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}