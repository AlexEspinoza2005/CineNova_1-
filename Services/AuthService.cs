using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MovieApi.Data;
using MovieApi.DTOs;
using MovieApi.Models;
using System.Text.Json;
using System;

namespace MovieApi.Services
{
    public interface IAuthService
    {
        Task<AuthResponse?> LoginAsync(LoginRequest request);
        Task<bool> RegisterAsync(RegisterRequest request);
        Task<bool> VerifyEmailAsync(string email, string code);
    }

    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ISendGridEmailService _emailService;

        public AuthService(
            ApplicationDbContext context,
            IConfiguration configuration,
            HttpClient httpClient,
            ISendGridEmailService emailService)
        {
            _context = context;
            _configuration = configuration;
            _httpClient = httpClient;
            _emailService = emailService;
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            Console.WriteLine($"[Login] Intentando iniciar sesión para: {request.Email}");
            try
            {
                var profile = await _context.Profiles
                    .FirstOrDefaultAsync(p => p.Email == request.Email && p.Password == request.Password);

                if (profile == null)
                {
                    Console.WriteLine($"[Login] No se encontró el perfil o contraseña incorrecta para: {request.Email}");
                    return null;
                }

                if (!profile.IsActive)
                {
                    Console.WriteLine($"[Login] Usuario baneado intentando ingresar: {request.Email}");
                    return null;
                }

                if (!profile.EmailConfirmed)
                {
                    Console.WriteLine($"[Login] Correo no confirmado para: {request.Email}");
                    return null;
                }

                Console.WriteLine($"[Login] Perfil encontrado: {profile.Id}. Buscando roles...");

                var userRole = await _context.UserRoles
                    .Include(ur => ur.Role)
                    .FirstOrDefaultAsync(ur => ur.UserId == profile.Id);

                var roleName = userRole?.Role?.Name ?? "client";
                Console.WriteLine($"[Login] Rol asignado: {roleName}");

                var token = GenerateJwtToken(profile.Id, profile.Email, roleName, profile.Username ?? profile.Email, profile.FullName, profile.AvatarUrl);

                return new AuthResponse
                {
                    Token = token,
                    Email = profile.Email,
                    Role = roleName,
                    Id = profile.Id
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Login] ERROR CRÍTICO: {ex.Message}");
                if (ex.InnerException != null) Console.WriteLine($"[Login] INNER ERROR: {ex.InnerException.Message}");
                throw;
            }
        }

        public async Task<bool> RegisterAsync(RegisterRequest request)
        {
            Console.WriteLine($"[Register] Intentando registrar usuario: {request.Email}");

            var strategy = _context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    if (await _context.Profiles.AnyAsync(p => p.Email == request.Email))
                    {
                        Console.WriteLine("[Register] El correo ya existe.");
                        return false;
                    }

                    var profile = new Profile
                    {
                        Id = Guid.NewGuid(),
                        Email = request.Email,
                        Username = request.Username,
                        FullName = request.FullName,
                        Password = request.Password,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    Console.WriteLine("[Register] Agregando perfil...");
                    _context.Profiles.Add(profile);
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"[Register] Perfil creado con ID: {profile.Id}");

                    var clientRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "client");
                    if (clientRole == null)
                    {
                        Console.WriteLine("[Register] Creando rol 'client'...");
                        clientRole = new Role { Name = "client", Description = "Default client role" };
                        _context.Roles.Add(clientRole);
                        await _context.SaveChangesAsync();
                    }

                    var adminRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "admin");
                    if (adminRole == null)
                    {
                        Console.WriteLine("[Register] Creando rol 'admin'...");
                        adminRole = new Role { Name = "admin", Description = "Administrator role" };
                        _context.Roles.Add(adminRole);
                        await _context.SaveChangesAsync();
                    }

                    var isFirstUser = await _context.UserRoles.CountAsync() == 0;
                    var roleToAssign = isFirstUser ? adminRole : clientRole;
                    Console.WriteLine($"[Register] Asignando rol: {roleToAssign.Name}");

                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = profile.Id,
                        RoleId = roleToAssign.Id,
                        AssignedAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

                    // Generar código de verificación de 6 dígitos
                    var verificationCode = new Random().Next(100000, 999999).ToString();
                    profile.VerificationCode = verificationCode;
                    _context.Profiles.Update(profile);
                    await _context.SaveChangesAsync();

                    // --- ENVÍO DE CORREO DE VERIFICACIÓN ---
                    try
                    {
                        string subject = "Verifica tu cuenta en CineNova";
                        string nombreUsuario = !string.IsNullOrEmpty(request.FullName) ? request.FullName : request.Username;

                        string htmlContent = $@"
                            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: auto; border: 1px solid #ddd; padding: 20px; border-radius: 10px;'>
                                <h2 style='color: #e50914; text-align: center;'>¡Bienvenido a CineNova!</h2>
                                <p>Hola {nombreUsuario}, gracias por registrarte. Para completar tu registro, utiliza el siguiente código de verificación:</p>
                                <div style='background: #f4f4f4; padding: 15px; text-align: center; font-size: 24px; font-weight: bold; letter-spacing: 5px;'>
                                    {verificationCode}
                                </div>
                                <p style='margin-top: 20px;'>Si no solicitaste este registro, puedes ignorar este correo.</p>
                                <hr style='border: 0; border-top: 1px solid #eee; margin: 20px 0;'>
                                <p style='font-size: 12px; color: #888; text-align: center;'>© 2026 CineNova AI. Todos los derechos reservados.</p>
                            </div>";

                        bool emailSent = await _emailService.SendEmailAsync(request.Email, subject, $"Tu código de verificación es: {verificationCode}", htmlContent);

                        if (emailSent)
                            Console.WriteLine($"[Register] Correo de verificación enviado a {request.Email}");
                        else
                            Console.WriteLine($"[Register] Falló el envío del correo de verificación a {request.Email}");
                    }
                    catch (Exception emailEx)
                    {
                        Console.WriteLine($"[Register] ERROR AL ENVIAR CORREO: {emailEx.Message}");
                    }
                    // -------------------------------------

                    await transaction.CommitAsync();
                    Console.WriteLine("[Register] Transacción completada con éxito.");

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Register] ERROR: {ex.Message}");
                    if (ex.InnerException != null) Console.WriteLine($"[Register] INNER ERROR: {ex.InnerException.Message}");
                    await transaction.RollbackAsync();
                    return false;
                }
            });
        }

        public async Task<bool> VerifyEmailAsync(string email, string code)
        {
            var profile = await _context.Profiles.FirstOrDefaultAsync(p => p.Email == email);
            if (profile == null || profile.VerificationCode != code)
            {
                return false;
            }

            profile.EmailConfirmed = true;
            profile.VerificationCode = null; // Limpiar el código usado
            _context.Profiles.Update(profile);
            await _context.SaveChangesAsync();

            return true;
        }

        private string GenerateJwtToken(Guid userId, string email, string role, string username, string? fullName, string? avatarUrl)
        {
            var jwtSettings = _configuration.GetSection("Jwt");
            var key = Encoding.ASCII.GetBytes(jwtSettings["Key"]!);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role)
            };

            if (!string.IsNullOrEmpty(fullName)) claims.Add(new Claim("FullName", fullName));
            if (!string.IsNullOrEmpty(avatarUrl)) claims.Add(new Claim("AvatarUrl", avatarUrl));

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(2),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = jwtSettings["Issuer"],
                Audience = jwtSettings["Audience"]
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}