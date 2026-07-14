using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ContentRiskScanner.Services
{
    public class TextToSpeechService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _url;

        public TextToSpeechService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            _apiKey = Environment.GetEnvironmentVariable("TTS_API_KEY")
                ?? configuration["TTS_API_KEY"]
                ?? throw new InvalidOperationException(
                    "TTS_API_KEY missing. .env file me TTS_API_KEY=... add karein.");

            _url = Environment.GetEnvironmentVariable("TTS_URL")
                ?? configuration["TTS_URL"]
                ?? throw new InvalidOperationException(
                    "TTS_URL missing. .env file me TTS_URL=... add karein.");
        }

        public async Task<byte[]> SynthesizeAsync(string text, string voice = "en-US_AllisonV3Voice")
        {
            var endpoint = $"{_url}/v1/synthesize?voice={Uri.EscapeDataString(voice)}";

            var payload = new { text };
            var json = JsonSerializer.Serialize(payload);

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var authBytes = Encoding.ASCII.GetBytes($"apikey:{_apiKey}");
            requestMessage.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mp3"));

            var response = await _httpClient.SendAsync(requestMessage);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Watson Text-to-Speech returned {(int)response.StatusCode}: {errorBody}");
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
    }
}