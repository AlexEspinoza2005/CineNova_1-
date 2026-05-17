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

        private static readonly TimeZoneInfo EcuadorTZ =
            TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
        private static DateTime NowEcuadorDisplay() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EcuadorTZ);

        public AgentController(ApplicationDbContext context, IVertexAIService vertexAI)
        {
            _context = context;
            _vertexAI = vertexAI;
        }

        // ══════════════════════════════════════════════════════════════
        //  ENDPOINT PRINCIPAL
        // ══════════════════════════════════════════════════════════════
        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] AgentQueryRequest request)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var isAdmin = User.IsInRole("admin");
            var stopwatch = Stopwatch.StartNew();

            string question = request.Question.Trim();

            // ── Detección de intención con IA (multiidioma) ────────────
            var intent = await ParseIntentWithAI(question);
            string lang = intent.Lang ?? "es";

            // ── Mensajes por idioma ────────────────────────────────────
            var msgs = new Dictionary<string, Dictionary<string, string>>
            {
                ["not_found"] = new()
                {
                    ["es"] = "No encontré ninguna película con ese nombre. Escribe el título entre comillas, por ejemplo: elimina \"Inception\".",
                    ["en"] = "I couldn't find any movie with that name. Write the title in quotes, for example: delete \"Inception\".",
                    ["pt"] = "Não encontrei nenhum filme com esse nome. Escreva o título entre aspas, por exemplo: apaga \"Inception\"."
                },
                ["no_permission"] = new()
                {
                    ["es"] = "La película existe pero no te pertenece, no puedes modificarla.",
                    ["en"] = "That movie exists but doesn't belong to you, you can't modify it.",
                    ["pt"] = "Esse filme existe mas não é seu, você não pode modificá-lo."
                },
                ["deleted"] = new()
                {
                    ["es"] = "✅ Película eliminada correctamente:",
                    ["en"] = "✅ Movie deleted successfully:",
                    ["pt"] = "✅ Filme excluído com sucesso:"
                },
                ["created"] = new()
                {
                    ["es"] = "✅ Película registrada exitosamente:",
                    ["en"] = "✅ Movie registered successfully:",
                    ["pt"] = "✅ Filme registrado com sucesso:"
                },
                ["updated"] = new()
                {
                    ["es"] = "✅ Película actualizada correctamente:",
                    ["en"] = "✅ Movie updated successfully:",
                    ["pt"] = "✅ Filme atualizado com sucesso:"
                },
                ["missing_fields"] = new()
                {
                    ["es"] = "Para registrar la película necesito:",
                    ["en"] = "To register the movie I need:",
                    ["pt"] = "Para registrar o filme preciso de:"
                },
                ["no_changes"] = new()
                {
                    ["es"] = "Entendí que quieres editar, pero no detecté qué campo cambiar. Puedes indicarme: título, duración, género, director, año, sinopsis o portada (URL).",
                    ["en"] = "I understood you want to edit, but I couldn't detect which field to change. You can specify: title, duration, genre, director, year, synopsis or cover (URL).",
                    ["pt"] = "Entendi que quer editar, mas não detectei qual campo alterar. Você pode indicar: título, duração, gênero, diretor, ano, sinopse ou capa (URL)."
                },
                ["duplicate"] = new()
                {
                    ["es"] = "Ya tienes una película registrada con ese título.",
                    ["en"] = "You already have a movie registered with that title.",
                    ["pt"] = "Você já tem um filme registrado com esse título."
                }
            };

            string T(string key) =>
                msgs.ContainsKey(key) && msgs[key].ContainsKey(lang)
                    ? msgs[key][lang]
                    : (msgs.ContainsKey(key) ? msgs[key]["es"] : key);

            // ══════════════════════════════════════════
            //  1) CREACIÓN
            // ══════════════════════════════════════════
            if (intent.Intent == "create")
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(intent.Title)) missing.Add("title/título/título");
                if (string.IsNullOrWhiteSpace(intent.Genre)) missing.Add("genre/género/gênero");
                if (string.IsNullOrWhiteSpace(intent.Director)) missing.Add("director");
                if (string.IsNullOrWhiteSpace(intent.Synopsis)) missing.Add("synopsis/sinopsis/sinopse");
                if (intent.Year == null) missing.Add("year/año/ano");

                if (missing.Any())
                {
                    var ans = $"{T("missing_fields")} {string.Join(", ", missing)}.";
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, ans, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = ans, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }

                var exists = await _context.Movies
                    .AnyAsync(m => m.Title.ToLower() == intent.Title!.ToLower() && m.UserId == userId);
                if (exists)
                {
                    var ans = T("duplicate");
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, ans, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = ans, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }

                var embeddingInput = $"{intent.Title} {intent.Genre} {intent.Director} {intent.Synopsis}";
                var embeddingStart = stopwatch.ElapsedMilliseconds;
                var embeddingValues = await _vertexAI.GetEmbeddingAsync(embeddingInput);
                var embeddingLatency = (int)(stopwatch.ElapsedMilliseconds - embeddingStart);
                var now = DateTime.UtcNow;

                var movie = new Movie
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = intent.Title!.Trim(),
                    Genre = intent.Genre!.Trim(),
                    Director = intent.Director!.Trim(),
                    Year = intent.Year!.Value,
                    Synopsis = intent.Synopsis!.Trim(),
                    DurationMin = intent.DurationMin,
                    CoverUrl = intent.CoverUrl,
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

                var answer = $"{T("created")}\n" +
                             $"- Título: {movie.Title}\n" +
                             $"- Género: {movie.Genre}\n" +
                             $"- Director: {movie.Director}\n" +
                             $"- Año: {movie.Year}\n" +
                             $"- Duración: {(movie.DurationMin.HasValue ? $"{movie.DurationMin} min" : "no especificada")}\n" +
                             $"- Sinopsis: {movie.Synopsis}\n" +
                             $"- Portada: {(string.IsNullOrWhiteSpace(movie.CoverUrl) ? "sin imagen" : movie.CoverUrl)}\n" +
                             $"- Inserción: {movie.InsertionLatencyMs}ms | Embedding: {embeddingLatency}ms";

                stopwatch.Stop();
                await PersistQuery(userId, request.Question, answer, (int)stopwatch.ElapsedMilliseconds);
                return Ok(new AgentQueryResponse { Answer = answer, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
            }

            // ══════════════════════════════════════════
            //  2) ELIMINACIÓN
            // ══════════════════════════════════════════
            if (intent.Intent == "delete")
            {
                var questionLower = question.ToLower();
                var movie = await FindMovieForUser(question, questionLower, userId, isAdmin);

                if (movie == null)
                {
                    var ans = T("not_found");
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, ans, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = ans, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }
                if (!isAdmin && movie.UserId != userId)
                {
                    var ans = $"⚠️ \"{movie.Title}\" — {T("no_permission")}";
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, ans, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = ans, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }

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

                var answer = $"{T("deleted")} \"{title}\"";
                stopwatch.Stop();
                await PersistQuery(userId, request.Question, answer, (int)stopwatch.ElapsedMilliseconds);
                return Ok(new AgentQueryResponse { Answer = answer, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
            }

            // ══════════════════════════════════════════
            //  3) EDICIÓN
            // ══════════════════════════════════════════
            if (intent.Intent == "edit")
            {
                var questionLower = question.ToLower();
                var movie = await FindMovieForUser(question, questionLower, userId, isAdmin);

                if (movie == null)
                {
                    var ans = T("not_found");
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, ans, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = ans, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }
                if (!isAdmin && movie.UserId != userId)
                {
                    var ans = $"⚠️ \"{movie.Title}\" — {T("no_permission")}";
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, ans, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = ans, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }

                bool changed = false;
                if (!string.IsNullOrWhiteSpace(intent.NewTitle)) { movie.Title = intent.NewTitle; changed = true; }
                if (!string.IsNullOrWhiteSpace(intent.Genre)) { movie.Genre = intent.Genre; changed = true; }
                if (!string.IsNullOrWhiteSpace(intent.Director)) { movie.Director = intent.Director; changed = true; }
                if (intent.Year.HasValue) { movie.Year = intent.Year.Value; changed = true; }
                if (intent.DurationMin.HasValue) { movie.DurationMin = intent.DurationMin; changed = true; }
                if (!string.IsNullOrWhiteSpace(intent.Synopsis)) { movie.Synopsis = intent.Synopsis; changed = true; }
                if (!string.IsNullOrWhiteSpace(intent.CoverUrl)) { movie.CoverUrl = intent.CoverUrl; changed = true; }

                if (!changed)
                {
                    var ans = T("no_changes");
                    stopwatch.Stop();
                    await PersistQuery(userId, request.Question, ans, (int)stopwatch.ElapsedMilliseconds);
                    return Ok(new AgentQueryResponse { Answer = ans, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
                }

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

                var answer = $"{T("updated")}\n" +
                             $"- Título: {movie.Title}\n" +
                             $"- Género: {movie.Genre}\n" +
                             $"- Director: {movie.Director}\n" +
                             $"- Año: {movie.Year}\n" +
                             $"- Duración: {(movie.DurationMin.HasValue ? $"{movie.DurationMin} min" : "no especificada")}\n" +
                             $"- Sinopsis: {movie.Synopsis}\n" +
                             $"- Portada: {(string.IsNullOrWhiteSpace(movie.CoverUrl) ? "sin imagen" : movie.CoverUrl)}";

                stopwatch.Stop();
                await PersistQuery(userId, request.Question, answer, (int)stopwatch.ElapsedMilliseconds);
                return Ok(new AgentQueryResponse { Answer = answer, Data = null, LatencyMs = (int)stopwatch.ElapsedMilliseconds });
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
                .Select(m => new { m.Title, m.Genre, m.Director, m.Year, m.InsertionStatus, m.InsertionLatencyMs, m.DurationMin, m.CreatedAt })
                .ToListAsync();

            var userLists = await _context.MovieLists
                .Where(l => l.UserId == userId)
                .Select(l => new { l.Name })
                .ToListAsync();

            var userFavorites = await _context.FavoriteMovies
                .Where(f => f.UserId == userId)
                .Include(f => f.Movie)
                .Select(f => new { Title = f.Movie!.Title })
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

            // ── Textos para el prompt ──────────────────────────────────
            var moviesDetail = userMovies.Any()
                ? string.Join("\n", userMovies.Select(m =>
                    $"  - '{m.Title}' | {m.Genre} | {m.Director} | {m.Year} | {m.InsertionStatus} | inserción: {m.InsertionLatencyMs}ms | duración: {m.DurationMin?.ToString() ?? "N/A"} min | registrada: {TimeZoneInfo.ConvertTimeFromUtc(m.CreatedAt, EcuadorTZ):dd/MM/yyyy HH:mm}"))
                : "  (ninguna)";

            var listsText = userLists.Any()
                ? string.Join(", ", userLists.Select(l => l.Name))
                : "none";

            var favoritesText = userFavorites.Any()
                ? string.Join(", ", userFavorites.Select(f => f.Title))
                : "none";

            var errorsText = recentErrors.Any()
                ? string.Join(" | ", recentErrors.Select(e => $"{e.Action}: {e.ErrorMessage} ({e.CreatedAt:dd/MM HH:mm})"))
                : "none";

            var historyText = agentHistory.Any()
                ? string.Join("\n", agentHistory.Select(h => $"User: {h.Question}\nAgent: {h.Answer}"))
                : "";

            var adminSection = isAdmin && allMoviesAdmin != null
                ? $"\nALL MOVIES ADMIN ({allMoviesAdmin.Count}): {string.Join(", ", allMoviesAdmin.Select(m => m.Title))}"
                : "";

            var nowDisplay = NowEcuadorDisplay();

            var systemPrompt = $@"You are CineNova AI, assistant for the CineNova movie catalog.
CRITICAL: Detect the language of the user message and respond ALWAYS in that exact language (Spanish, English or Portuguese).
CRITICAL: Never use Markdown formatting. No **, no *, no # headers. For lists use a simple dash (-) at the start of each line. Plain text only.
Use ONLY the data provided below. Never invent or estimate data you don't have.
Current time (Ecuador): {nowDisplay:dd/MM/yyyy HH:mm:ss}

USER: {userProfile?.FullName ?? "User"} | Role: {(isAdmin ? "Admin" : "User")}

MOVIES OF THIS USER ({userMovies.Count} total):
{moviesDetail}

LISTS: {listsText}
FAVORITES: {favoritesText}

SYSTEM METRICS:
- Total movies in system: {metrics?.TotalMovies}
- Total users: {metrics?.TotalUsers}
- Avg insertion latency: {metrics?.AvgInsertionLatencyMs}ms
- Avg embedding latency: {metrics?.AvgEmbeddingLatencyMs}ms
- Avg query latency: {metrics?.AvgQueryLatencyMs}ms
- Success rate: {metrics?.SuccessRatePct}%
- Total errors: {metrics?.TotalErrors}
- Total duplicates: {metrics?.TotalDuplicates}
- Total agent queries: {metrics?.TotalAgentQueries}
- Vectors stored (movies with embedding): {vectorCount}
- Movies without vector: {(moviesWithoutVector.Any() ? string.Join(", ", moviesWithoutVector) : "none")}
{adminSection}

RECENT ERRORS: {errorsText}

CONVERSATION HISTORY:
{historyText}

RULES:
- Answer directly, no greetings if there is conversation history
- Use real data only, never estimate
- If asked about genres, analyze the MOVIES list above field by field
- If asked which movie took longest to insert, check the insercion ms value in the movies list
- If asked about movies without embeddings, check the 'Movies without vector' field above
- If you don't have the data, say so honestly in one sentence
- Never use Markdown formatting under any circumstance";

            var aiAnswer = await _vertexAI.GetChatResponseAsync(
                request.Question,
                systemPrompt,
                agentHistory.Select(h => new ConversationTurn { Question = h.Question, Answer = h.Answer }).ToList()
            );

            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;
            await PersistQuery(userId, request.Question, aiAnswer, latency);
            return Ok(new AgentQueryResponse { Answer = aiAnswer, Data = null, LatencyMs = latency });
        }

        // ══════════════════════════════════════════════════════════════
        //  PARSEAR INTENCIÓN CON IA
        // ══════════════════════════════════════════════════════════════
        private async Task<ParsedIntent> ParseIntentWithAI(string message)
        {
            var prompt = $@"Analyze this message and extract the intent and fields.
Respond ONLY with pure JSON, no markdown, no explanations, no backticks.

Message: ""{message}""

Expected JSON:
{{
  ""Intent"": ""create"" or ""edit"" or ""delete"" or ""query"",
  ""Lang"": ""es"" or ""en"" or ""pt"",
  ""Title"": ""current movie title or null"",
  ""NewTitle"": ""new title if renaming or null"",
  ""Genre"": ""genre or null"",
  ""Director"": ""director or null"",
  ""Year"": number or null,
  ""DurationMin"": number or null,
  ""Synopsis"": ""synopsis or null"",
  ""CoverUrl"": ""url if present or null""
}}

Rules:
- Intent=create if user wants to register/create/add/insert a NEW movie
- Intent=edit if user wants to modify/edit/update/change a field of an EXISTING movie
- Intent=delete if user wants to delete/remove/erase an existing movie
- Intent=query for everything else (questions, summaries, stats)
- Lang: detect the language of the message (es=Spanish, en=English, pt=Portuguese)
- For edit: Title is the movie to find, NewTitle is the new name only if renaming
- Extract CoverUrl from any http/https URL present in the message";

            try
            {
                var raw = await _vertexAI.GetChatResponseAsync(
                    prompt,
                    "You are a data extractor. You respond ONLY with valid JSON, no additional text, no markdown.",
                    null);

                // Gemini sometimes wraps in ```json ... ``` despite instructions
                var clean = Regex.Replace(raw, @"```json|```", "").Trim();

                // Extract JSON object if there's surrounding text
                var jsonMatch = Regex.Match(clean, @"\{[\s\S]*\}");
                if (jsonMatch.Success) clean = jsonMatch.Value;

                return System.Text.Json.JsonSerializer.Deserialize<ParsedIntent>(
                    clean,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new ParsedIntent();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ParseIntent Error] {ex.Message}");
                return new ParsedIntent(); // fallback: treat as query
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  BUSCAR PELÍCULA — exacto → parcial → fuzzy
        // ══════════════════════════════════════════════════════════════
        private async Task<Movie?> FindMovieForUser(string message, string messageLower, Guid userId, bool isAdmin)
        {
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
        //  FUZZY MATCHING — Levenshtein
        // ══════════════════════════════════════════════════════════════
        private static double LevenshteinSimilarity(string a, string b)
        {
            int dist = LevenshteinDistance(a, b);
            int maxLen = Math.Max(a.Length, b.Length);
            return maxLen == 0 ? 1.0 : 1.0 - (double)dist / maxLen;
        }

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
                if (best >= 1.0) break;
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

    // ══════════════════════════════════════════════════════════════
    //  MODELO DE INTENCIÓN PARSEADA
    // ══════════════════════════════════════════════════════════════
    public class ParsedIntent
    {
        public string Intent { get; set; } = "query"; // create | edit | delete | query
        public string Lang { get; set; } = "es";    // es | en | pt
        public string? Title { get; set; }
        public string? NewTitle { get; set; }
        public string? Genre { get; set; }
        public string? Director { get; set; }
        public int? Year { get; set; }
        public int? DurationMin { get; set; }
        public string? Synopsis { get; set; }
        public string? CoverUrl { get; set; }
    }
}