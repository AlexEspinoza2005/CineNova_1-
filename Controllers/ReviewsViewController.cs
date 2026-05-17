using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.Models;
using MovieApi.DTOs;
using System.Diagnostics;
using System.Text.Json;

namespace MovieApi.Controllers
{
    [Authorize]
    public class ReviewsViewController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReviewsViewController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Create(Guid movieId, CreateReviewDto dto)
        {
            if (!ModelState.IsValid)
            {
                return RedirectToAction("Details", "MoviesView", new { id = movieId });
            }

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            // Double check uniqueness
            var existing = await _context.MovieReviews
                .AnyAsync(r => r.MovieId == movieId && r.UserId == userId);

            if (existing) return BadRequest("Ya has reseñado esta película.");

            try
            {
                var newReview = new MovieReview
                {
                    Id = Guid.NewGuid(),
                    MovieId = movieId,
                    UserId = userId,
                    Rating = dto.Rating,
                    Review = dto.Review,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.MovieReviews.Add(newReview);
                await _context.SaveChangesAsync();
                stopwatch.Stop();

                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "create_review",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha dejado una reseña de {dto.Rating} estrellas",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { movieId = movieId, rating = dto.Rating }),
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
                    Action = "create_review",
                    Status = "error",
                    ErrorMessage = $"Error al crear reseña: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Details", "MoviesView", new { id = movieId });
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Guid id, CreateReviewDto dto)
        {
            if (!ModelState.IsValid)
            {
                var r = await _context.MovieReviews.FindAsync(id);
                return RedirectToAction("Details", "MoviesView", new { id = r?.MovieId });
            }

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();
            
            var existingReview = await _context.MovieReviews.FindAsync(id);

            if (existingReview == null) return NotFound();
            if (existingReview.UserId != userId) return Forbid();

            try
            {
                existingReview.Rating = dto.Rating;
                existingReview.Review = dto.Review;
                existingReview.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                stopwatch.Stop();

                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "edit_review",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha editado su reseña a {dto.Rating} estrellas",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { reviewId = id, movieId = existingReview.MovieId, rating = dto.Rating }),
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
                    Action = "edit_review",
                    Status = "error",
                    ErrorMessage = $"Error al editar reseña: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Details", "MoviesView", new { id = existingReview.MovieId });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            var review = await _context.MovieReviews.FindAsync(id);

            if (review == null) return NotFound();
            if (review.UserId != userId) return Forbid();

            var movieId = review.MovieId;

            try
            {
                _context.MovieReviews.Remove(review);
                await _context.SaveChangesAsync();
                stopwatch.Stop();

                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "delete_review",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha eliminado su reseña",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { reviewId = id, movieId = movieId }),
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
                    Action = "delete_review",
                    Status = "error",
                    ErrorMessage = $"Error al eliminar reseña: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Details", "MoviesView", new { id = movieId });
        }
    }
}
