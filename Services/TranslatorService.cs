using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ContentRiskScanner.Services
{
    /// <summary>
    /// IBM Watson Language Translator — detects language and translates to English
    /// before the risk scan pipeline runs. Ensures non-English content is not
    /// silently passed through as Low Risk due to zero keyword matches.
    /// Free tier: 1,000,000 characters/month (Lite plan).
    /// </summary>
    public class TranslatorService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;
        private readonly string? _url;

        public TranslatorService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = Environment.GetEnvironmentVariable("TRANSLATOR_API_KEY")
                ?? configuration["TRANSLATOR_API_KEY"];
            _url = Environment.GetEnvironmentVariable("TRANSLATOR_URL")
                ?? configuration["TRANSLATOR_URL"];
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_url);

        /// <summary>
        /// Translates <paramref name="text"/> to English if it is not already English.
        /// Returns the original text unchanged when Translator is not configured or the call fails.
        /// </summary>
        public async Task<(string text, string? detectedLanguage)> EnsureEnglishAsync(string text)
        {
            if (!IsConfigured)
                return (text, null);

            try
            {
                // Step 1: identify the language
                var identifyEndpoint = $"{_url}/v3/identify?version=2018-05-01";
                using var identifyRequest = new HttpRequestMessage(HttpMethod.Post, identifyEndpoint)
                {
                    Content = new StringContent(text, Encoding.UTF8, "text/plain")
                };
                var authBytes = Encoding.ASCII.GetBytes($"apikey:{_apiKey}");
                identifyRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                var identifyResponse = await _httpClient.SendAsync(identifyRequest);
                if (!identifyResponse.IsSuccessStatusCode)
                    return (text, null);

                var identifyBody = await identifyResponse.Content.ReadAsStringAsync();
                using var identifyDoc = JsonDocument.Parse(identifyBody);
                var topLang = identifyDoc.RootElement
                    .GetProperty("languages")[0]
                    .GetProperty("language").GetString();

                // Already English — nothing to do
                if (topLang == null || topLang.StartsWith("en"))
                    return (text, topLang);

                // Step 2: translate to English
                var translateEndpoint = $"{_url}/v3/translate?version=2018-05-01";
                var payload = JsonSerializer.Serialize(new { text = new[] { text }, source = topLang, target = "en" });
                using var translateRequest = new HttpRequestMessage(HttpMethod.Post, translateEndpoint)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                translateRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                var translateResponse = await _httpClient.SendAsync(translateRequest);
                if (!translateResponse.IsSuccessStatusCode)
                    return (text, topLang);

                var translateBody = await translateResponse.Content.ReadAsStringAsync();
                using var translateDoc = JsonDocument.Parse(translateBody);
                var translated = translateDoc.RootElement
                    .GetProperty("translations")[0]
                    .GetProperty("translation").GetString();

                return (translated ?? text, topLang);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: Language Translator failed ({ex.Message}). Scanning original text.");
                return (text, null);
            }
        }
    }
}
