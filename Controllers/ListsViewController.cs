using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.Models;
using MovieApi.Models.ViewModels;
using MovieApi.DTOs;
using System.Diagnostics;
using System.Text.Json;

namespace MovieApi.Controllers
{
    [Authorize]
    public class ListsViewController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ListsViewController(ApplicationDbContext context)
        {
            _context = context;
        }

        [Route("Lists")]
        public async Task<IActionResult> Index()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);

            var lists = await _context.MovieLists
                .AsNoTracking()
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();
            return View(lists);
        }

        public async Task<IActionResult> Details(Guid id)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);

            var list = await _context.MovieLists
                .AsNoTracking()
                .Include(l => l.Profile)
                .FirstOrDefaultAsync(l => l.Id == id);

            if (list == null) return NotFound();
            if (!list.IsPublic && list.UserId != userId) return Forbid();

            var items = await _context.MovieListItems
                .AsNoTracking()
                .Include(i => i.Movie)
                .Where(i => i.ListId == id)
                .OrderBy(i => i.Position)
                .ToListAsync();

            ViewBag.IsOwner = list.UserId == userId;
            ViewBag.Items = items;

            return View(list);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateMovieListDto dto)
        {
            if (!ModelState.IsValid) return View(dto);

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            try
            {
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

                stopwatch.Stop();
                var logTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"));
                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "create_list",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha creado la lista '{dto.Name}' a las {logTime:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { listName = dto.Name, isPublic = dto.IsPublic }),
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
                    Action = "create_list",
                    Status = "error",
                    ErrorMessage = $"Error al crear lista: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);

            var list = await _context.MovieLists.FindAsync(id);
            if (list == null) return NotFound();
            if (list.UserId != userId) return Forbid();

            var dto = new CreateMovieListDto
            {
                Name = list.Name,
                Description = list.Description,
                IsPublic = list.IsPublic
            };

            ViewBag.ListId = id;
            return View(dto);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(Guid id, CreateMovieListDto dto)
        {
            if (!ModelState.IsValid) return View(dto);

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            var list = await _context.MovieLists.FindAsync(id);
            if (list == null) return NotFound();
            if (list.UserId != userId) return Forbid();

            try
            {
                list.Name = dto.Name;
                list.Description = dto.Description;
                list.IsPublic = dto.IsPublic;
                list.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                stopwatch.Stop();
                var logTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"));
                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "edit_list",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha editado la lista '{dto.Name}' a las {logTime:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { listId = id, listName = dto.Name, isPublic = dto.IsPublic }),
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
                    Action = "edit_list",
                    Status = "error",
                    ErrorMessage = $"Error al editar lista: {ex.Message}",
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
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            var list = await _context.MovieLists.FindAsync(id);
            if (list == null) return NotFound();
            if (list.UserId != userId) return Forbid();

            try
            {
                var listName = list.Name;
                _context.MovieLists.Remove(list);
                await _context.SaveChangesAsync();

                stopwatch.Stop();
                var logTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"));
                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "delete_list",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha eliminado la lista '{listName}' a las {logTime:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { listId = id, listName = listName }),
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
                    Action = "delete_list",
                    Status = "error",
                    ErrorMessage = $"Error al eliminar lista: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Lists/SearchMovies/{id}")]
        public async Task<IActionResult> SearchMovies(Guid id, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Json(new List<object>());

            var movies = await _context.Movies
                .Where(m => m.Title.ToLower().Contains(query.ToLower()))
                .Take(5)
                .Select(m => new { m.Id, m.Title, m.Year, m.CoverUrl })
                .ToListAsync();

            return Json(movies);
        }

        [HttpPost("Lists/AddMovie/{id}")]
        public async Task<IActionResult> AddMovie(Guid id, Guid movieId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            var list = await _context.MovieLists.FindAsync(id);
            if (list == null) return NotFound();
            if (list.UserId != userId) return Forbid();

            try
            {
                var movie = await _context.Movies.FindAsync(movieId);
                var movieTitle = movie?.Title ?? movieId.ToString();

                var existing = await _context.MovieListItems
                    .FirstOrDefaultAsync(i => i.ListId == id && i.MovieId == movieId);

                if (existing == null)
                {
                    var item = new MovieListItem
                    {
                        ListId = id,
                        MovieId = movieId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.MovieListItems.Add(item);
                    await _context.SaveChangesAsync();
                }

                stopwatch.Stop();
                var logTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"));
                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "add_to_list",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha agregado la película {movieTitle} a la lista '{list.Name}' a las {logTime:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { listId = id, movieId = movieId, movieTitle = movieTitle }),
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
                    Action = "add_to_list",
                    Status = "error",
                    ErrorMessage = $"Error al agregar pelicula a lista: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Details), new { id = id });
        }

        [HttpPost("Lists/RemoveMovie/{id}")]
        public async Task<IActionResult> RemoveMovie(Guid id, Guid movieId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdStr == null) return Unauthorized();
            var userId = Guid.Parse(userIdStr);
            var fullName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            var list = await _context.MovieLists.FindAsync(id);
            if (list == null) return NotFound();
            if (list.UserId != userId) return Forbid();

            try
            {
                var movie = await _context.Movies.FindAsync(movieId);
                var movieTitle = movie?.Title ?? movieId.ToString();

                var item = await _context.MovieListItems
                    .FirstOrDefaultAsync(i => i.ListId == id && i.MovieId == movieId);

                if (item != null)
                {
                    _context.MovieListItems.Remove(item);
                    await _context.SaveChangesAsync();
                }

                stopwatch.Stop();
                var logTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"));
                var log = new OperationLog
                {
                    UserId = userId,
                    Action = "remove_from_list",
                    Status = "success",
                    ErrorMessage = $"El usuario {fullName} ha eliminado la película {movieTitle} de la lista '{list.Name}' a las {logTime:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { listId = id, movieId = movieId, movieTitle = movieTitle }),
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
                    Action = "remove_from_list",
                    Status = "error",
                    ErrorMessage = $"Error al eliminar pelicula de lista: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Details), new { id = id });
        }
    }
}
