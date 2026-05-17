using Microsoft.AspNetCore.Mvc;
using MovieApi.Models.ViewModels;
using MovieApi.Services;
using MovieApi.DTOs;
using System.Diagnostics;
using MovieApi.Data;
using MovieApi.Models;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace MovieApi.Controllers
{
    public class AuthController : Controller
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "MoviesView");

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var stopwatch = Stopwatch.StartNew();
            var loginRequest = new LoginRequest { Email = model.Email, Password = model.Password };
            var response = await _authService.LoginAsync(loginRequest);

            using (var scope = HttpContext.RequestServices.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var status = response != null ? "success" : "error";
                var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ecuadorZone);

                var log = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    Action = "login",
                    Status = status,
                    ErrorMessage = response != null
                        ? $"El usuario {model.Email} ha iniciado sesión exitosamente a las {localNow:HH:mm:ss}"
                        : $"Intento de inicio de sesión fallido para {model.Email} a las {localNow:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { email = model.Email }),
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.OperationLogs.Add(log);
                await dbContext.SaveChangesAsync();
            }

            if (response == null)
            {
                ModelState.AddModelError(string.Empty, "Credenciales inválidas o correo no verificado");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, response.Id.ToString()),
                new Claim(ClaimTypes.Email, response.Email),
                new Claim(ClaimTypes.Name, response.Email),
                new Claim(ClaimTypes.Role, response.Role)
            };

            using (var scope = HttpContext.RequestServices.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var profile = await dbContext.Profiles
                    .FirstOrDefaultAsync(p => p.Id == response.Id);

                if (profile != null)
                {
                    claims.Add(new Claim("FullName", profile.FullName ?? ""));
                }
            }

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            return RedirectToAction("Index", "MoviesView");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var stopwatch = Stopwatch.StartNew();
            var registerRequest = new RegisterRequest
            {
                Email = model.Email,
                Username = model.Username,
                Password = model.Password,
                FullName = model.FullName
            };
            var result = await _authService.RegisterAsync(registerRequest);

            using (var scope = HttpContext.RequestServices.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var status = result ? "success" : "error";
                var ecuadorZone = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
                var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ecuadorZone);

                var log = new OperationLog
                {
                    Id = Guid.NewGuid(),
                    Action = "register",
                    Status = status,
                    ErrorMessage = result
                        ? $"El usuario {model.Email} se ha registrado exitosamente a las {localNow:HH:mm:ss}"
                        : $"Fallo en el registro del usuario {model.Email} a las {localNow:HH:mm:ss}",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    Metadata = JsonSerializer.Serialize(new { email = model.Email, username = model.Username }),
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.OperationLogs.Add(log);
                await dbContext.SaveChangesAsync();
            }

            if (!result)
            {
                ModelState.AddModelError(string.Empty, "Error al registrar el usuario");
                return View(model);
            }

            TempData["UserEmail"] = model.Email;
            return RedirectToAction("ConfirmEmail", new { email = model.Email });
        }

        [HttpGet]
        public IActionResult ConfirmEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return RedirectToAction("Register");
            return View(new ConfirmEmailViewModel { Email = email });
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmEmail(ConfirmEmailViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _authService.VerifyEmailAsync(model.Email, model.Code);

            if (result)
            {
                TempData["SuccessMessage"] = "Correo verificado exitosamente. Ya puedes iniciar sesión.";
                return RedirectToAction("Login");
            }

            ModelState.AddModelError(string.Empty, "Código de verificación inválido");
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            Response.Cookies.Delete("jwt_token");
            return RedirectToAction("Index", "Home");
        }
    }
}