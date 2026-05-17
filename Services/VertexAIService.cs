using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MovieApi.Services
{
    public interface IVertexAIService
    {
        Task<float[]> GetEmbeddingAsync(string text);
        Task<string> GetChatResponseAsync(string question, string context);
    }

    public class VertexAIService : IVertexAIService
    {
        private readonly string _projectId;
        private readonly string _location;
        private readonly HttpClient _httpClient;
        private readonly GoogleCredential _credential;

        public VertexAIService(IConfiguration configuration, HttpClient httpClient)
        {
            _projectId = configuration["GoogleCloud:ProjectId"] ?? "cinenova-ai-496607";
            _location = configuration["GoogleCloud:Location"] ?? "us-central1";
            _httpClient = httpClient;

            var credentialsPath = Path.Combine(Directory.GetCurrentDirectory(), "google-credentials.json");
            if (File.Exists(credentialsPath))
            {
                _credential = GoogleCredential.FromFile(credentialsPath)
                    .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            }
            else
            {
                throw new FileNotFoundException("google-credentials.json not found");
            }
        }

        private async Task<string> GetAccessTokenAsync()
        {
            return await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
        }

        public async Task<float[]> GetEmbeddingAsync(string text)
        {
            try
            {
                var accessToken = await GetAccessTokenAsync();
                var url = $"https://{_location}-aiplatform.googleapis.com/v1/projects/{_projectId}/locations/{_location}/publishers/google/models/text-embedding-005:predict";

                var requestBody = new
                {
                    instances = new[]
                    {
                        new { content = text, task_type = "RETRIEVAL_DOCUMENT" }
                    }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonResponse);
                
                var values = doc.RootElement
                    .GetProperty("predictions")[0]
                    .GetProperty("embeddings")
                    .GetProperty("values");

                return values.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Embedding Error] {ex.Message}");
                throw;
            }
        }

        public async Task<string> GetChatResponseAsync(string question, string contextData)
        {
            try
            {
                var accessToken = await GetAccessTokenAsync();
                var url = $"https://{_location}-aiplatform.googleapis.com/v1/projects/{_projectId}/locations/{_location}/publishers/google/models/gemini-2.5-flash:generateContent";

                var promptText = $@"Eres 'CineNova AI v3.1', un agente de última generación basado en Gemini 2.5 Flash.
                CONTEXTO ACTUAL DEL SISTEMA: {contextData}

                INSTRUCCIONES PRO:
                1. Analiza los datos del sistema y responde con autoridad técnica pero amabilidad.
                2. Si el usuario pide registrar algo, confirma que has iniciado el proceso.
                3. Usa los indicadores de latencia para comentar sobre la salud del sistema.

                PREGUNTA: {question}";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = promptText } }
                        }
                    }
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var jsonResponse = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonResponse);

                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return text ?? "Lo siento, no pude generar una respuesta.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gemini Error] {ex.Message}");
                return $"Hola, soy el asistente de CineNova. Veo que tienes {contextData}. ¿En qué puedo ayudarte hoy?";
            }
        }
    }
}
