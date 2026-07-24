using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

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
                ?? ""; // Don't throw immediately so app boots even if TTS is disabled

            _url = Environment.GetEnvironmentVariable("TTS_URL")
                ?? configuration["TTS_URL"]
                ?? "";
        }

        // Returns raw audio bytes (WAV or MP3) from IBM TTS
        public async Task<byte[]> SynthesizeAudioAsync(string text, string acceptType = "audio/wav")
        {
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_url))
            {
                throw new InvalidOperationException("IBM TTS keys are missing. Configure TTS_API_KEY and TTS_URL in .env");
            }

            // Using AllisonV3Voice as a clear, professional American English voice
            var endpoint = $"{_url}/v1/synthesize?voice=en-US_AllisonV3Voice";

            var payload = new { text = text };
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            
            // Specify the audio format we want back (e.g. audio/wav)
            requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptType));

            var authBytes = Encoding.ASCII.GetBytes($"apikey:{_apiKey}");
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

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
