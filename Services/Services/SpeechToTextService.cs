using System.Net.Http.Headers;
using System.Text.Json;

namespace ContentRiskScanner.Services
{
    public class SpeechToTextService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _url;

        public SpeechToTextService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            _apiKey = Environment.GetEnvironmentVariable("STT_API_KEY")
                ?? configuration["STT_API_KEY"]
                ?? throw new InvalidOperationException(
                    "STT_API_KEY missing. .env file me STT_API_KEY=... add karein.");

            _url = Environment.GetEnvironmentVariable("STT_URL")
                ?? configuration["STT_URL"]
                ?? throw new InvalidOperationException(
                    "STT_URL missing. .env file me STT_URL=... add karein.");
        }

        public async Task<string> TranscribeAsync(byte[] audioData, string contentType = "audio/wav")
        {
            var endpoint = $"{_url}/v1/recognize";

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new ByteArrayContent(audioData)
            };
            requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            var authBytes = System.Text.Encoding.ASCII.GetBytes($"apikey:{_apiKey}");
            requestMessage.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            var response = await _httpClient.SendAsync(requestMessage);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Watson Speech-to-Text returned {(int)response.StatusCode}: {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);

            var transcriptBuilder = new System.Text.StringBuilder();

            if (doc.RootElement.TryGetProperty("results", out var results))
            {
                foreach (var result in results.EnumerateArray())
                {
                    if (result.TryGetProperty("alternatives", out var alternatives) &&
                        alternatives.GetArrayLength() > 0)
                    {
                        var transcript = alternatives[0].GetProperty("transcript").GetString();
                        transcriptBuilder.Append(transcript);
                    }
                }
            }

            return transcriptBuilder.ToString().Trim();
        }
    }
}