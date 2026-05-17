using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.Models.ViewModels;
using MovieApi.Models;
using System.Diagnostics;
using System.Text.Json;

namespace MovieApi.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ProfileController(ApplicationDbContext context)
        {
            _context = context;
        }

        [Authorize(Roles = "admin")]
        [HttpGet]
        public async Task<IActionResult> AdminEdit(Guid id)
        {
            var profile = await _context.Profiles.FindAsync(id);
            if (profile == null) return NotFound();

            var model = new EditProfileViewModel
            {
                Username = profile.Username,
                FullName = profile.FullName,
                Bio = profile.Bio,
                AvatarUrl = profile.AvatarUrl,
                Country = profile.Country,
                City = profile.City,
                FavoriteGenresString = profile.FavoriteGenres != null ? string.Join(", ", profile.FavoriteGenres) : ""
            };

            ViewBag.ProfileId = id;
            return View(model);
        }

        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<IActionResult> AdminEdit(Guid id, EditProfileViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var profile = await _context.Profiles.FindAsync(id);
            if (profile == null) return NotFound();

            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var adminName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            var stopwatch = Stopwatch.StartNew();

            profile.Username = model.Username;
            profile.FullName = model.FullName;
            profile.Bio = model.Bio;
            profile.Country = model.Country;
            profile.City = model.City;

            // Handle Image Upload -> Base64
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                using var ms = new MemoryStream();
                await model.ImageFile.CopyToAsync(ms);
                var fileBytes = ms.ToArray();
                string base64String = Convert.ToBase64String(fileBytes);
                profile.AvatarUrl = $"data:{model.ImageFile.ContentType};base64,{base64String}";
            }
            else if (!string.IsNullOrEmpty(model.AvatarUrl))
            {
                profile.AvatarUrl = model.AvatarUrl;
            }
            
            if (!string.IsNullOrEmpty(model.FavoriteGenresString))
            {
                profile.FavoriteGenres = model.FavoriteGenresString.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }

            profile.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
                stopwatch.Stop();

                var logTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"));
                var log = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = adminId,
                    Action = "admin_edit_user",
                    Status = "success",
                    ErrorMessage = $"El administrador {adminName} ha editado el perfil de {profile.Email} a las {logTime:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { targetUserId = profile.Id, targetEmail = profile.Email }),
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                var log = new OperationLog
                {
                    UserId = adminId,
                    Action = "admin_edit_user",
                    Status = "error",
                    ErrorMessage = $"Error al editar perfil por admin: {ex.Message}",
                    CreatedAt = DateTime.UtcNow
                };
                _context.OperationLogs.Add(log);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index", "DashboardView");
        }

        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<IActionResult> ToggleStatus(Guid id)
        {
            var profile = await _context.Profiles.FindAsync(id);
            if (profile == null) return NotFound();

            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var adminName = User.FindFirst("FullName")?.Value ?? User.Identity?.Name;
            
            profile.IsActive = !profile.IsActive;
            profile.UpdatedAt = DateTime.UtcNow;

            var stopwatch = Stopwatch.StartNew();
            await _context.SaveChangesAsync();
            stopwatch.Stop();

            var logTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"));
            var actionName = profile.IsActive ? "unban_user" : "ban_user";
            var log = new OperationLog
            {
                Id = Guid.NewGuid(),
                UserId = adminId,
                Action = actionName,
                Status = "success",
                ErrorMessage = $"El administrador {adminName} ha {(profile.IsActive ? "activado" : "baneado")} al usuario {profile.Email} a las {logTime:HH:mm:ss}",
                LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                Metadata = JsonSerializer.Serialize(new { targetUserId = profile.Id, targetEmail = profile.Email, newStatus = profile.IsActive ? "active" : "banned" }),
                CreatedAt = DateTime.UtcNow
            };
            _context.OperationLogs.Add(log);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", "DashboardView");
        }

        public async Task<IActionResult> Index()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var profile = await _context.Profiles.FindAsync(userId);
            if (profile == null) return NotFound();

            var model = new ProfileViewModel
            {
                Id = profile.Id,
                Email = profile.Email,
                Username = profile.Username,
                FullName = profile.FullName,
                Bio = profile.Bio,
                AvatarUrl = profile.AvatarUrl,
                Country = profile.Country,
                City = profile.City,
                FavoriteGenres = profile.FavoriteGenres,
                CreatedAt = profile.CreatedAt,
                MyMovies = await _context.Movies.Where(m => m.UserId == userId).ToListAsync(),
                Favorites = await _context.FavoriteMovies.Include(f => f.Movie).Where(f => f.UserId == userId).ToListAsync(),
                MyLists = await _context.MovieLists.Where(l => l.UserId == userId).ToListAsync()
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit()
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var profile = await _context.Profiles.FindAsync(userId);
            if (profile == null) return NotFound();

            var model = new EditProfileViewModel
            {
                Username = profile.Username,
                FullName = profile.FullName,
                Bio = profile.Bio,
                AvatarUrl = profile.AvatarUrl,
                Country = profile.Country,
                City = profile.City,
                FavoriteGenresString = profile.FavoriteGenres != null ? string.Join(", ", profile.FavoriteGenres) : ""
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(EditProfileViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var profile = await _context.Profiles.FindAsync(userId);
            if (profile == null) return NotFound();

            profile.Username = model.Username;
            profile.FullName = model.FullName;
            profile.Bio = model.Bio;
            profile.Country = model.Country;
            profile.City = model.City;

            // Handle Image Upload -> Base64
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                using var ms = new MemoryStream();
                await model.ImageFile.CopyToAsync(ms);
                var fileBytes = ms.ToArray();
                string base64String = Convert.ToBase64String(fileBytes);
                profile.AvatarUrl = $"data:{model.ImageFile.ContentType};base64,{base64String}";
            }
            else if (!string.IsNullOrEmpty(model.AvatarUrl))
            {
                profile.AvatarUrl = model.AvatarUrl;
            }
            
            if (!string.IsNullOrEmpty(model.FavoriteGenresString))
            {
                profile.FavoriteGenres = model.FavoriteGenresString.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }

            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
