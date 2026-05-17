using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.Models;
using MovieApi.Models.ViewModels;
using System.Text.Json;

namespace MovieApi.Controllers
{
    [Authorize]
    public class MoviesViewController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MoviesViewController(ApplicationDbContext context)
        {
            _context = context;
        }

        [Route("Movies")]
        public async Task<IActionResult> Index(int page = 1)
        {
            int pageSize = 12;
            var movies = new List<Movie>();
            int totalMovies = 0;
            try {
                IQueryable<Movie> query = _context.Movies.AsNoTracking();
                totalMovies = await query.CountAsync();

                movies = await query
                    .OrderByDescending(m => m.CreatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
            } catch { }

            var model = new MovieListViewModel
            {
                Movies = movies,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling((double)totalMovies / pageSize)
            };

            return View(model);
        }

        public async Task<IActionResult> Details(Guid id)
        {
            Movie? movie = null;
            try {
                movie = await _context.Movies
                    .AsNoTracking()
                    .Include(m => m.Profile)
                    .FirstOrDefaultAsync(m => m.Id == id);
            } catch { }

            if (movie == null) return NotFound();

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr != null)
            {
                var userId = Guid.Parse(userIdStr);
                ViewBag.UserLists = await _context.MovieLists
                    .AsNoTracking()
                    .Where(l => l.UserId == userId)
                    .ToListAsync();
                
                ViewBag.IsFavorite = await _context.FavoriteMovies
                    .AsNoTracking()
                    .AnyAsync(f => f.MovieId == id && f.UserId == userId);

                ViewBag.UserReview = await _context.MovieReviews
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.MovieId == id && r.UserId == userId);
            }

            var reviews = await _context.MovieReviews
                .AsNoTracking()
                .Include(r => r.Profile)
                .Where(r => r.MovieId == id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
            foreach (var review in reviews)
                review.CreatedAt = TimeZoneInfo.ConvertTimeFromUtc(review.CreatedAt, ecuadorZone);

            var avgRating = reviews.Any() ? (decimal)reviews.Average(r => r.Rating) : 0;

            var model = new MovieViewModel
            {
                Id = movie.Id,
                UserId = movie.UserId,
                Title = movie.Title,
                Genre = movie.Genre,
                Director = movie.Director,
                Year = movie.Year,
                Synopsis = movie.Synopsis,
                DurationMin = movie.DurationMin,
                CoverUrl = movie.CoverUrl,
                Reviews = reviews,
                AverageRating = Math.Round(avgRating, 1)
            };

            return View(model);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MovieViewModel model)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
            var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ecuadorZone);

            if (!ModelState.IsValid) 
            {
                // Log validation failure
                var errors = string.Join(" | ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

                var logError = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId == Guid.Empty ? null : userId,
                    Action = "create_movie",
                    Status = "error",
                    ErrorMessage = $"Fallo de validación al crear película: {errors} a las {localNow:HH:mm:ss}",
                    Metadata = JsonSerializer.Serialize(new { 
                        title = model.Title, 
                        errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList() 
                    }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(logError);
                await _context.SaveChangesAsync();

                return View(model);
            }

            // Check for duplicates by title
            var isDuplicate = await _context.Movies
                .AnyAsync(m => m.Title.ToLower() == model.Title.ToLower());

            if (isDuplicate)
            {
                var logDup = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = "create_movie",
                    Status = "error",
                    ErrorMessage = $"Intento de crear película duplicada: '{model.Title}' a las {localNow:HH:mm:ss}",
                    Metadata = JsonSerializer.Serialize(new { title = model.Title, reason = "duplicate_title" }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(logDup);
                await _context.SaveChangesAsync();

                ModelState.AddModelError("Title", "Ya existe una película con este título.");
                return View(model);
            }

            // Handle Image Upload -> Base64
            string? finalCoverUrl = model.CoverUrl;
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                using var ms = new MemoryStream();
                await model.ImageFile.CopyToAsync(ms);
                var fileBytes = ms.ToArray();
                string base64String = Convert.ToBase64String(fileBytes);
                finalCoverUrl = $"data:{model.ImageFile.ContentType};base64,{base64String}";
            }

            var stopwatch = Stopwatch.StartNew();

            var movie = new Movie
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = model.Title,
                Genre = model.Genre,
                Director = model.Director,
                Year = model.Year.Value,
                Synopsis = model.Synopsis,
                DurationMin = model.DurationMin,
                CoverUrl = finalCoverUrl,
                InsertionStatus = "success",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                _context.Movies.Add(movie);
                await _context.SaveChangesAsync();
                
                stopwatch.Stop();
                movie.InsertionLatencyMs = (int)stopwatch.ElapsedMilliseconds;
                await _context.SaveChangesAsync();

                // Log success
                var log = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = "create_movie",
                    Status = "success",
                    ErrorMessage = $"El usuario {User.Identity?.Name} ha creado la película {movie.Title} a las {localNow:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { movieId = movie.Id, title = movie.Title }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Log error
                var log = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = "create_movie",
                    Status = "error",
                    ErrorMessage = $"Fallo al crear película por el usuario {User.Identity?.Name}: {ex.Message} a las {localNow:HH:mm:ss}",
                    Metadata = JsonSerializer.Serialize(new { title = model.Title, exception = ex.Message }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();

                ModelState.AddModelError("", "Error al guardar la película: " + ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var movie = await _context.Movies.FindAsync(id);
            if (movie == null) return NotFound();

            var model = new MovieViewModel
            {
                Id = movie.Id,
                Title = movie.Title,
                Genre = movie.Genre,
                Director = movie.Director,
                Year = movie.Year,
                Synopsis = movie.Synopsis,
                DurationMin = movie.DurationMin,
                CoverUrl = movie.CoverUrl
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(MovieViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var movie = await _context.Movies.FindAsync(model.Id);
            if (movie == null) return NotFound();

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("admin");

            if (!isAdmin && movie.UserId != userId) return Forbid();

            // Handle Image Upload -> Base64
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                using var ms = new MemoryStream();
                await model.ImageFile.CopyToAsync(ms);
                var fileBytes = ms.ToArray();
                string base64String = Convert.ToBase64String(fileBytes);
                movie.CoverUrl = $"data:{model.ImageFile.ContentType};base64,{base64String}";
            }
            else if (!string.IsNullOrEmpty(model.CoverUrl))
            {
                movie.CoverUrl = model.CoverUrl;
            }

            var stopwatch = Stopwatch.StartNew();
            movie.Title = model.Title;
            movie.Genre = model.Genre;
            movie.Director = model.Director;
            movie.Year = model.Year.Value;
            movie.Synopsis = model.Synopsis;
            movie.DurationMin = model.DurationMin;
            movie.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
                stopwatch.Stop();

                var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ecuadorZone);

                var log = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = "edit_movie",
                    Status = "success",
                    ErrorMessage = $"El usuario {User.Identity?.Name} ha editado la película {movie.Title} a las {localNow:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { movieId = movie.Id }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ecuadorZone);

                var log = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = "edit_movie",
                    Status = "error",
                    ErrorMessage = $"Fallo al editar película por el usuario {User.Identity?.Name}: {ex.Message} a las {localNow:HH:mm:ss}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            var movie = await _context.Movies.FindAsync(id);
            if (movie == null) return NotFound();

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("admin");

            if (!isAdmin && movie.UserId != userId) return Forbid();

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var movieTitle = movie.Title;
                _context.Movies.Remove(movie);
                await _context.SaveChangesAsync();
                stopwatch.Stop();

                var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ecuadorZone);

                var log = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = "delete_movie",
                    Status = "success",
                    ErrorMessage = $"El usuario {User.Identity?.Name} ha eliminado la película {movieTitle} a las {localNow:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { movieId = id, title = movieTitle }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ecuadorZone);

                var log = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Action = "delete_movie",
                    Status = "error",
                    ErrorMessage = $"Fallo al eliminar película por el usuario {User.Identity?.Name}: {ex.Message} a las {localNow:HH:mm:ss}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
