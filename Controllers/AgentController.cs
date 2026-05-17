using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MovieApi.Data;
using MovieApi.DTOs;
using MovieApi.Models;
using Pgvector.EntityFrameworkCore;
using MovieApi.Services;

namespace MovieApi.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AgentController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IVertexAIService _vertexAI;

        private static readonly TimeZoneInfo EcuadorTZ = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
        private static DateTime NowEcuadorDisplay() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EcuadorTZ);

        public AgentController(ApplicationDbContext context, IVertexAIService vertexAI)
        {
            _context = context;
            _vertexAI = vertexAI;
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] AgentQueryRequest request)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("admin");
            var stopwatch = Stopwatch.StartNew();

            string question = request.Question.Trim();
            string questionLower = question.ToLower();

            // ══════════════════════════════════════════
            //  1) CREACIÓN — se evalúa PRIMERO
            // ══════════════════════════════════════════
            string[] creationOnlyKeywords = { "registra", "crea", "inserta", "añade la película", "agrega la película", "añade película", "agrega película" };
            bool isCreationRequest = creationOnlyKeywords.Any(k => questionLower.Contains(k));

            if (isCreationRequest)
            {
                var createResult = await TryCreateMovie(question, userId, stopwatch);
                if (createResult != null)
                {
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, createResult, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = createResult, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }
            }

            // ══════════════════════════════════════════
            //  2) ELIMINACIÓN
            // ══════════════════════════════════════════
            string[] deleteKeywords = { "elimina", "borra", "eliminar", "borrar", "quita", "quitar", "suprime", "suprimir" };
            bool isDeleteRequest = !isCreationRequest && deleteKeywords.Any(k => questionLower.Contains(k));

            if (isDeleteRequest)
            {
                var deleteResult = await TryDeleteMovie(question, questionLower, userId, isAdmin);
                if (deleteResult != null)
                {
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, deleteResult, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = deleteResult, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }
            }

            // ══════════════════════════════════════════
            //  3) EDICIÓN
            //     "añade" y "agrega" aquí solo si NO es creación
            //     y van acompañados de un campo específico
            // ══════════════════════════════════════════
            string[] editKeywords = {
                "actualiza", "modifica", "edita", "cambia", "renombra", "renombrar",
                "llámale", "llamale", "cámbiale", "cambiale", "ponle",
                "pon la duración", "pon el año", "pon el género", "pon el director",
                "cambia la duración", "cambia el año", "cambia el género", "cambia el genero",
                "cambia el director", "cambia la sinopsis", "cambia la imagen",
                "cambia el título", "cambia el titulo", "cambia la portada",
                "actualiza la imagen", "actualiza la portada",
                "pon la imagen", "pon la portada", "pon el link",
                "agrega la imagen", "agrega la portada", "agrega el link", "agrega la duración",
                "añade la duración", "añade la imagen", "añade la portada", "añade el link",
                "cambia el link", "actualiza el link"
            };
            bool isEditRequest = !isCreationRequest && !isDeleteRequest && editKeywords.Any(k => questionLower.Contains(k));

            if (isEditRequest)
            {
                var editResult = await TryEditMovie(question, questionLower, userId, isAdmin);
                if (editResult != null)
                {
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, editResult, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = editResult, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }
            }

            // ══════════════════════════════════════════
            //  4) CONSULTA GENERAL → IA
            // ══════════════════════════════════════════
            var metrics = await _context.VDashboardSummaries.AsNoTracking().FirstOrDefaultAsync();
            var userProfile = await _context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == userId);

            var userMovies = await _context.Movies
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(20)
                .Select(m => new { m.Title, m.Genre, m.Director, m.Year, m.InsertionStatus, m.InsertionLatencyMs, m.DurationMin })
                .ToListAsync();

            var userLists = await _context.MovieLists
                .Where(l => l.UserId == userId)
                .Select(l => new { l.Name, l.Description })
                .ToListAsync();

            var userFavorites = await _context.FavoriteMovies
                .Where(f => f.UserId == userId)
                .Include(f => f.Movie)
                .Select(f => new { Title = f.Movie!.Title, f.Movie.Genre })
                .ToListAsync();

            var recentErrors = await _context.OperationLogs
                .Where(l => l.Status == "error" && (isAdmin || l.UserId == userId))
                .OrderByDescending(l => l.CreatedAt)
                .Take(10)
                .Select(l => new { l.Action, l.ErrorMessage, l.CreatedAt })
                .ToListAsync();

            var agentHistory = await _context.AgentQueries
                .Where(q => q.UserId == userId)
                .OrderByDescending(q => q.CreatedAt)
                .Take(5)
                .OrderBy(q => q.CreatedAt)
                .Select(q => new { q.Question, q.Answer })
                .ToListAsync();

            var vectorCount = await _context.Movies.CountAsync(m => m.Embedding != null);
            var moviesWithoutVector = await _context.Movies
                .Where(m => m.Embedding == null && (m.UserId == userId || isAdmin))
                .Select(m => m.Title)
                .ToListAsync();

            var allMoviesAdmin = isAdmin
                ? await _context.Movies.OrderByDescending(m => m.CreatedAt).Take(50)
                    .Select(m => new { m.Title, m.Genre, m.Director, m.Year, m.InsertionStatus }).ToListAsync()
                : null;

            var moviesText = userMovies.Any()
                ? string.Join(", ", userMovies.Select(m => $"'{m.Title}' ({m.Genre}, {m.Year}, {m.InsertionStatus}, duración: {m.DurationMin?.ToString() ?? "no registrada"} min)"))
                : "ninguna película registrada aún";
            var listsText = userLists.Any() ? string.Join(", ", userLists.Select(l => $"'{l.Name}'")) : "sin listas";
            var favoritesText = userFavorites.Any() ? string.Join(", ", userFavorites.Select(f => f.Title)) : "sin favoritos";
            var errorsText = recentErrors.Any()
                ? string.Join(" | ", recentErrors.Select(e => $"{e.Action}: {e.ErrorMessage} ({e.CreatedAt:dd/MM HH:mm})"))
                : "sin errores recientes";
            var historyText = agentHistory.Any()
                ? string.Join("\n", agentHistory.Select(h => $"Usuario: {h.Question}\nAgente: {h.Answer}"))
                : "";
            var adminSection = isAdmin && allMoviesAdmin != null
                ? $"\nCOMO ADMIN VES TODAS ({allMoviesAdmin.Count}): {string.Join(", ", allMoviesAdmin.Select(m => m.Title))}"
                : "";

            var nowDisplay = NowEcuadorDisplay();

            var systemPrompt = $@"Eres CineNova AI, asistente inteligente del catálogo de películas CineNova.
Respondes en español, de forma natural y conversacional. Usas exactamente los datos que se te dan, nunca inventas.
Hora actual en Ecuador: {nowDisplay:dd/MM/yyyy HH:mm:ss}

USUARIO: {userProfile?.FullName ?? "Usuario"} | Rol: {(isAdmin ? "Administrador" : "Cliente")}

SUS PELÍCULAS ({userMovies.Count}): {moviesText}
SUS LISTAS: {listsText}
SUS FAVORITOS: {favoritesText}

MÉTRICAS DEL SISTEMA:
- Total películas: {metrics?.TotalMovies} | Usuarios: {metrics?.TotalUsers}
- Latencia inserción: {metrics?.AvgInsertionLatencyMs}ms | Latencia semántica: {metrics?.AvgQueryLatencyMs}ms
- Tasa éxito: {metrics?.SuccessRatePct}% | Errores: {metrics?.TotalErrors} | Duplicados: {metrics?.TotalDuplicates}
- Vectores almacenados: {vectorCount} | Sin vector: {moviesWithoutVector.Count}
- Consultas al agente: {metrics?.TotalAgentQueries}
{adminSection}

ERRORES RECIENTES: {errorsText}

HISTORIAL:
{historyText}

INSTRUCCIONES:
- Responde directo, sin saludos repetidos si hay historial
- Usa datos reales, nunca estimes ni inventes
- Si preguntan géneros, analiza la lista de películas del usuario
- Si no tienes el dato, dilo honestamente";

            var answer = await _vertexAI.GetChatResponseAsync(
                request.Question,
                systemPrompt,
                agentHistory.Select(h => new ConversationTurn { Question = h.Question, Answer = h.Answer }).ToList()
            );

            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;
            await PersistQuery(userId, request.Question, answer, latency);

            return Ok(new AgentQueryResponse { Answer = answer, Data = null, LatencyMs = latency });
        }

        // ══════════════════════════════════════════════════════════════
        //  BUSCAR PELÍCULA — exacto → parcial → fuzzy
        //  Busca en TODAS las películas (para detectar permisos),
        //  pero el llamador verifica ownership antes de operar.
        // ══════════════════════════════════════════════════════════════
        private async Task<Movie?> FindMovieForUser(string message, string messageLower, Guid userId, bool isAdmin)
        {
            // Siempre buscamos en todas para poder dar mensaje de permisos correcto.
            // El filtro de userId se aplica DESPUÉS en el llamador.
            var movies = await _context.Movies.ToListAsync();

            // 1) Entre comillas — exacto, luego parcial, luego fuzzy
            var quotedMatches = Regex.Matches(message, @"[""«»\u201c\u201d]([^""«»\u201c\u201d]+)[""«»\u201c\u201d]");
            if (quotedMatches.Count > 0)
            {
                var q = quotedMatches[0].Groups[1].Value.ToLower().Trim();

                var found = movies.FirstOrDefault(m => m.Title.ToLower() == q)
                          ?? movies.FirstOrDefault(m => m.Title.ToLower().Contains(q))
                          ?? movies.FirstOrDefault(m => q.Contains(m.Title.ToLower()))
                          ?? movies
                                .Select(m => new { Movie = m, Score = LevenshteinSimilarity(m.Title.ToLower(), q) })
                                .Where(x => x.Score >= 0.80)
                                .OrderByDescending(x => x.Score)
                                .FirstOrDefault()?.Movie;

                if (found != null) return found;
            }

            // 2) Coincidencia libre exacta (título más largo primero)
            foreach (var m in movies.OrderByDescending(m => m.Title.Length))
                if (messageLower.Contains(m.Title.ToLower()))
                    return m;

            // 3) Fuzzy sobre ventanas del mensaje
            return movies
                .Select(m => new { Movie = m, Score = BestSubstringScore(m.Title.ToLower(), messageLower) })
                .Where(x => x.Score >= 0.80)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault()?.Movie;
        }

        // ══════════════════════════════════════════════════════════════
        //  ELIMINAR
        // ══════════════════════════════════════════════════════════════
        private async Task<string?> TryDeleteMovie(string message, string messageLower, Guid userId, bool isAdmin)
        {
            try
            {
                var movie = await FindMovieForUser(message, messageLower, userId, isAdmin);
                if (movie == null)
                    return "No encontré ninguna película con ese nombre. Escribe el título entre comillas, por ejemplo: elimina \"Inception\".";

                if (!isAdmin && movie.UserId != userId)
                    return $"⚠️ La película \"{movie.Title}\" existe pero no te pertenece, no puedes eliminarla.";

                var title = movie.Title;
                _context.Movies.Remove(movie);
                _context.OperationLogs.Add(new OperationLog
                {
                    UserId = userId,
                    Action = "delete_movie",
                    Status = "success",
                    RecordsAffected = 1,
                    ErrorMessage = $"El usuario eliminó la película \"{title}\" vía agente IA",
                    Metadata = $"{{\"title\":\"{title}\"}}",
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return $"✅ Película \"{title}\" eliminada correctamente.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AgentDelete Error] {ex.Message}");
                return null;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  EDITAR — cualquier campo
        // ══════════════════════════════════════════════════════════════
        private async Task<string?> TryEditMovie(string message, string messageLower, Guid userId, bool isAdmin)
        {
            try
            {
                var quotedMatches = Regex.Matches(message, @"[""«»\u201c\u201d]([^""«»\u201c\u201d]+)[""«»\u201c\u201d]");
                var movie = await FindMovieForUser(message, messageLower, userId, isAdmin);

                if (movie == null)
                    return "No encontré ninguna película con ese nombre. Escribe el título entre comillas, por ejemplo: edita \"Inception\".";

                if (!isAdmin && movie.UserId != userId)
                    return $"⚠️ La película \"{movie.Title}\" existe pero no te pertenece, no puedes editarla.";

                bool changed = false;

                // ── TÍTULO (segundo valor entre comillas) ──────────────
                if (quotedMatches.Count >= 2)
                {
                    var newTitle = quotedMatches[1].Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(newTitle) && !newTitle.Equals(movie.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        movie.Title = newTitle;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    var titlePatterns = new[]
                    {
                        @"(?:llámale|llamale|renómbrala|renombrala|cámbiale el nombre a|cambiale el nombre a|cambia el título a|cambia el titulo a|nuevo título[:\s]*|nuevo titulo[:\s]*)\s*[«»""]?(.+?)[«»""]?(?:\s*,|\s*$)",
                        @"(?:título|titulo)[:\s]+[«»""]?(.+?)[«»""]?(?:\s*,|\s*$)"
                    };
                    foreach (var pattern in titlePatterns)
                    {
                        var m2 = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                        if (m2.Success)
                        {
                            var newTitle = m2.Groups[1].Value.Trim().Trim('"', '«', '»', '\u201c', '\u201d');
                            if (!string.IsNullOrWhiteSpace(newTitle) && !newTitle.Equals(movie.Title, StringComparison.OrdinalIgnoreCase))
                            {
                                movie.Title = newTitle;
                                changed = true;
                                break;
                            }
                        }
                    }
                }

                // ── DURACIÓN ───────────────────────────────────────────
                var durationMatch = Regex.Match(message, @"(\d+)\s*(min|minutos|minutes)", RegexOptions.IgnoreCase);
                if (durationMatch.Success && int.TryParse(durationMatch.Groups[1].Value, out int duration))
                {
                    movie.DurationMin = duration;
                    changed = true;
                }

                // ── AÑO ────────────────────────────────────────────────
                if (messageLower.Contains("año") || messageLower.Contains("year"))
                {
                    var yearMatch = Regex.Match(message, @"\b(19[0-9]{2}|20[0-9]{2})\b");
                    if (yearMatch.Success && int.TryParse(yearMatch.Value, out int year))
                    {
                        movie.Year = year;
                        changed = true;
                    }
                }

                // ── GÉNERO ─────────────────────────────────────────────
                // Patrón mejorado: captura el valor DESPUÉS de "a" cuando viene
                // "cambia el género de 'X' a Acción" → evita capturar "de"
                var genrePatterns = new[]
                {
                    // "cambia el género de 'Película' a Acción" → captura "Acción"
                    @"(?:género|genero)[^a-záéíóúñ]*(?:de\s+[^,]+?\s+)?a\s+[«»""]?([^,\.\n""«»\u201c\u201d]+?)[«»""]?(?:\s*,|\s*$|\s+director|\s+año|\s+duración|\s+sinopsis)",
                    // "género: Acción" o "genero Acción"
                    @"(?:género|genero)[:\s]+(?:a\s+)?[«»""]?([^,\.\n""«»\u201c\u201d]+?)[«»""]?(?:\s*,|\s*$|\s+director|\s+año|\s+duración|\s+sinopsis)",
                    // "cambia el género a Acción"
                    @"(?:cambia el género a|cambia el genero a|pon el género|pon el genero)[:\s]+[«»""]?([^,\.\n""«»\u201c\u201d]+?)[«»""]?(?:\s*,|\s*$)"
                };

                foreach (var gp in genrePatterns)
                {
                    var gm = Regex.Match(message, gp, RegexOptions.IgnoreCase);
                    if (gm.Success)
                    {
                        var rawGenre = gm.Groups[1].Value.Trim().TrimStart('a', ' ').Trim();
                        // Elimina preposiciones sueltas al inicio: "de", "a", "al"
                        rawGenre = Regex.Replace(rawGenre, @"^(de|al?)\s+", "", RegexOptions.IgnoreCase).Trim();
                        if (!string.IsNullOrWhiteSpace(rawGenre))
                        {
                            movie.Genre = rawGenre;
                            changed = true;
                            break;
                        }
                    }
                }

                // ── DIRECTOR ───────────────────────────────────────────
                var directorPatterns = new[]
                {
                    @"(?:director)[:\s]+[«»""]?([^,\.\n""«»\u201c\u201d]+?)[«»""]?(?:\s*,|\s*$|\s+año|\s+duración|\s+sinopsis|\s+género|\s+genero)",
                    @"(?:cambia el director a|pon el director)[:\s]+[«»""]?([^,\.\n""«»\u201c\u201d]+?)[«»""]?(?:\s*,|\s*$)"
                };
                foreach (var dp in directorPatterns)
                {
                    var dm = Regex.Match(message, dp, RegexOptions.IgnoreCase);
                    if (dm.Success)
                    {
                        var rawDirector = dm.Groups[1].Value.Trim();
                        rawDirector = Regex.Replace(rawDirector, @"^(de|al?)\s+", "", RegexOptions.IgnoreCase).Trim();
                        if (!string.IsNullOrWhiteSpace(rawDirector))
                        {
                            movie.Director = rawDirector;
                            changed = true;
                            break;
                        }
                    }
                }

                // ── SINOPSIS ───────────────────────────────────────────
                var synopsisMatch = Regex.Match(message, @"(?:sinopsis|synopsis)[:\s]+(.+)", RegexOptions.IgnoreCase);
                if (synopsisMatch.Success)
                {
                    movie.Synopsis = synopsisMatch.Groups[1].Value.Trim().TrimEnd('.');
                    changed = true;
                }

                // ── IMAGEN / PORTADA (cover_url) ───────────────────────
                bool isImageContext = messageLower.Contains("imagen") || messageLower.Contains("portada")
                    || messageLower.Contains("cover") || messageLower.Contains("foto")
                    || messageLower.Contains("link") || messageLower.Contains("url");

                if (isImageContext)
                {
                    var urlMatch = Regex.Match(message, @"https?://[^\s""«»\u201c\u201d]+", RegexOptions.IgnoreCase);
                    if (urlMatch.Success)
                    {
                        movie.CoverUrl = urlMatch.Value.Trim();
                        changed = true;
                    }
                }

                if (!changed)
                    return "Entendí que quieres editar, pero no detecté qué campo cambiar.\n" +
                           "Puedes indicarme: título, duración, género, director, año, sinopsis o imagen/portada (URL).";

                movie.UpdatedAt = DateTime.UtcNow;
                _context.OperationLogs.Add(new OperationLog
                {
                    UserId = userId,
                    Action = "edit_movie",
                    Status = "success",
                    RecordsAffected = 1,
                    ErrorMessage = $"El usuario editó la película \"{movie.Title}\" vía agente IA",
                    Metadata = $"{{\"title\":\"{movie.Title}\"}}",
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return $"✅ Película actualizada correctamente:\n" +
                       $"- Título: {movie.Title}\n" +
                       $"- Género: {movie.Genre}\n" +
                       $"- Director: {movie.Director}\n" +
                       $"- Año: {movie.Year}\n" +
                       $"- Duración: {(movie.DurationMin.HasValue ? $"{movie.DurationMin} min" : "no especificada")}\n" +
                       $"- Sinopsis: {movie.Synopsis}\n" +
                       $"- Portada: {(string.IsNullOrWhiteSpace(movie.CoverUrl) ? "sin imagen" : movie.CoverUrl)}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AgentEdit Error] {ex.Message}");
                return null;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  CREAR
        // ══════════════════════════════════════════════════════════════
        private async Task<string?> TryCreateMovie(string message, Guid userId, Stopwatch stopwatch)
        {
            try
            {
                string title = ExtractField(message, new[] { "llamada", "titulada", "título", "titulo", "película" }, new[] { "género", "genero", "director", "año", "sinopsis", "imagen", "portada", "cover", "dura", "duración", "," });
                string genre = ExtractField(message, new[] { "género", "genero" }, new[] { "director", "año", "sinopsis", "imagen", "portada", "dura", "duración", "," });
                string director = ExtractField(message, new[] { "director" }, new[] { "año", "sinopsis", "género", "genero", "imagen", "portada", "dura", "duración", "," });
                string synopsis = ExtractSynopsis(message);
                int year = ExtractYear(message);
                int? duration = ExtractDuration(message);

                // Portada opcional
                string? coverUrl = null;
                var urlMatch = Regex.Match(message, @"https?://[^\s""«»\u201c\u201d]+", RegexOptions.IgnoreCase);
                if (urlMatch.Success) coverUrl = urlMatch.Value.Trim();

                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(title)) missing.Add("título");
                if (string.IsNullOrWhiteSpace(genre)) missing.Add("género");
                if (string.IsNullOrWhiteSpace(director)) missing.Add("director");
                if (string.IsNullOrWhiteSpace(synopsis)) missing.Add("sinopsis");
                if (year == 0) missing.Add("año");

                if (missing.Any())
                    return $"Para registrar la película necesito: {string.Join(", ", missing)}. Por favor completa la información.";

                var exists = await _context.Movies
                    .AnyAsync(m => m.Title.ToLower() == title.ToLower() && m.UserId == userId);
                if (exists)
                    return $"Ya tienes una película registrada con el título \"{title}\".";

                var embeddingInput = $"{title} {genre} {director} {synopsis}";
                var embeddingStart = stopwatch.ElapsedMilliseconds;
                var embeddingValues = await _vertexAI.GetEmbeddingAsync(embeddingInput);
                var embeddingLatency = (int)(stopwatch.ElapsedMilliseconds - embeddingStart);

                var now = DateTime.UtcNow;
                var movie = new Movie
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = title.Trim(),
                    Genre = genre.Trim(),
                    Director = director.Trim(),
                    Year = year,
                    Synopsis = synopsis.Trim(),
                    DurationMin = duration,
                    CoverUrl = coverUrl,
                    Embedding = new Pgvector.Vector(embeddingValues),
                    EmbeddingLatencyMs = embeddingLatency,
                    InsertionLatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    InsertionStatus = "success",
                    CreatedAt = now,
                    UpdatedAt = now
                };

                _context.Movies.Add(movie);
                _context.OperationLogs.Add(new OperationLog
                {
                    UserId = userId,
                    Action = "insert_movie",
                    Status = "success",
                    LatencyMs = (int)stopwatch.ElapsedMilliseconds,
                    RecordsAffected = 1,
                    ErrorMessage = $"El usuario registró la película \"{movie.Title}\" vía agente IA",
                    Metadata = $"{{\"title\":\"{movie.Title}\",\"genre\":\"{movie.Genre}\",\"director\":\"{movie.Director}\",\"year\":{movie.Year}}}",
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return $"✅ Película registrada exitosamente:\n" +
                       $"- Título: {movie.Title}\n" +
                       $"- Género: {movie.Genre}\n" +
                       $"- Director: {movie.Director}\n" +
                       $"- Año: {movie.Year}\n" +
                       $"- Duración: {(movie.DurationMin.HasValue ? $"{movie.DurationMin} min" : "no especificada")}\n" +
                       $"- Sinopsis: {movie.Synopsis}\n" +
                       $"- Portada: {(string.IsNullOrWhiteSpace(movie.CoverUrl) ? "sin imagen" : movie.CoverUrl)}\n" +
                       $"- Inserción: {movie.InsertionLatencyMs}ms | Embedding: {embeddingLatency}ms";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AgentCreate Error] {ex.Message}");
                return null;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  PERSISTIR QUERY
        // ══════════════════════════════════════════════════════════════
        private async Task PersistQuery(Guid userId, string question, string answer, int latencyMs)
        {
            _context.AgentQueries.Add(new AgentQuery
            {
                UserId = userId,
                Question = question,
                Answer = answer,
                ResponseLatencyMs = latencyMs,
                ResultsCount = 1,
                HasResults = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS — extracción de campos
        // ══════════════════════════════════════════════════════════════
        private string ExtractField(string text, string[] startMarkers, string[] endMarkers)
        {
            string lower = text.ToLower();
            foreach (var marker in startMarkers)
            {
                int idx = lower.IndexOf(marker);
                if (idx < 0) continue;
                int start = idx + marker.Length;
                while (start < text.Length && (text[start] == ':' || text[start] == ' ' || text[start] == '"' || text[start] == '\u201c')) start++;
                int end = text.Length;
                foreach (var endMarker in endMarkers)
                {
                    int endIdx = lower.IndexOf(endMarker, start);
                    if (endIdx > start && endIdx < end) end = endIdx;
                }
                var value = text.Substring(start, end - start).Trim().TrimEnd(',').Trim('"', '\u201c', '\u201d', '«', '»').Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "";
        }

        private string ExtractSynopsis(string text)
        {
            string lower = text.ToLower();
            foreach (var marker in new[] { "sinopsis:", "sinopsis " })
            {
                int idx = lower.IndexOf(marker);
                if (idx < 0) continue;

                int start = idx + marker.Length;
                string raw = text.Substring(start);
                string rawLower = raw.ToLower();

                // Cortar cuando aparece cualquier campo posterior conocido o una URL
                var stopMarkers = new[]
                {
                    ", duración", ", duracion", ", portada", ", imagen",
                    ", director", ", género", ", genero", ", año", ", cover",
                    ", url", " duración ", " duracion ", " portada ", " imagen ",
                    " https://", " http://"
                };

                int end = raw.Length;
                foreach (var stop in stopMarkers)
                {
                    int stopIdx = rawLower.IndexOf(stop);
                    if (stopIdx > 0 && stopIdx < end)
                        end = stopIdx;
                }

                var value = raw.Substring(0, end).Trim().TrimEnd('.').Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "";
        }

        private int ExtractYear(string text)
        {
            var match = Regex.Match(text, @"\b(19[0-9]{2}|20[0-9]{2})\b");
            return match.Success && int.TryParse(match.Value, out int year) ? year : 0;
        }

        private int? ExtractDuration(string text)
        {
            var patterns = new[]
            {
                @"dura[:\s]+(\d+)\s*(min|minutos|minutes)?",
                @"duración[:\s]+(\d+)\s*(min|minutos|minutes)?",
                @"duracion[:\s]+(\d+)\s*(min|minutos|minutes)?",
                @"(\d+)\s*(min|minutos|minutes)"
            };
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int d))
                    return d;
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════
        //  FUZZY MATCHING — Levenshtein
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Similitud normalizada entre 0.0 y 1.0.
        /// 1.0 = idénticos, 0.0 = completamente distintos.
        /// </summary>
        private static double LevenshteinSimilarity(string a, string b)
        {
            int dist = LevenshteinDistance(a, b);
            int maxLen = Math.Max(a.Length, b.Length);
            return maxLen == 0 ? 1.0 : 1.0 - (double)dist / maxLen;
        }

        /// <summary>
        /// Busca la mejor similitud del título contra todas las ventanas
        /// del texto del mismo tamaño que el título.
        /// Útil para encontrar el título dentro de una oración más larga.
        /// </summary>
        private static double BestSubstringScore(string title, string text)
        {
            if (string.IsNullOrEmpty(title)) return 0;
            if (text.Contains(title)) return 1.0;

            int len = title.Length;
            if (len > text.Length) return LevenshteinSimilarity(title, text);

            double best = 0;
            for (int i = 0; i <= text.Length - len; i++)
            {
                var window = text.Substring(i, len);
                var score = LevenshteinSimilarity(title, window);
                if (score > best) best = score;
                if (best >= 1.0) break; // no puede mejorar
            }
            return best;
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length, m = t.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + (s[i - 1] == t[j - 1] ? 0 : 1));

            return d[n, m];
        }
    }
}