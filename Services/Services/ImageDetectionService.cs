using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ContentRiskScanner.Services
{
    public class ImageAnalysisResult
    {
        public string Description { get; set; } = string.Empty;
        public bool Nsfw { get; set; }
        public bool ViolentOrOffensive { get; set; }
        public bool IdentifiableRealPeople { get; set; }
        public bool BrandLogos { get; set; }
        public bool SensitiveOrConfidential { get; set; }
    }

    public class ImageDetectionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _projectId;
        private readonly string _geminiApiKey;
        private readonly string _geminiUrl;

        // FIXED: SemaphoreSlim(1,1) makes IAM token refresh thread-safe.
        // Plain fields + async methods = race condition under concurrent requests.
        private string _iamToken = string.Empty;
        private DateTime _tokenExpiration = DateTime.MinValue;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);

        public ImageDetectionService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            _apiKey = Environment.GetEnvironmentVariable("IBM_CLOUD_APIKEY")
                ?? configuration["IBM_CLOUD_APIKEY"]
                ?? throw new InvalidOperationException("IBM_CLOUD_APIKEY missing. .env file me add karein.");

            _projectId = Environment.GetEnvironmentVariable("IBM_PROJECT_ID")
                ?? configuration["IBM_PROJECT_ID"]
                ?? throw new InvalidOperationException("IBM_PROJECT_ID missing. .env file me add karein.");

            _geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? configuration["GEMINI_API_KEY"] ?? "";
            _geminiUrl    = Environment.GetEnvironmentVariable("GEMINI_URL")    ?? configuration["GEMINI_URL"]    ?? "";
        }

        private async Task<string> GetIamTokenAsync()
        {
            // Fast path — token still valid, no lock needed
            if (!string.IsNullOrEmpty(_iamToken) && DateTime.UtcNow < _tokenExpiration)
                return _iamToken;

            await _tokenLock.WaitAsync();
            try
            {
                // Re-check inside the lock — another thread may have refreshed already
                if (!string.IsNullOrEmpty(_iamToken) && DateTime.UtcNow < _tokenExpiration)
                    return _iamToken;

                using var tokenClient = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Post, "https://iam.cloud.ibm.com/identity/token");
                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "urn:ibm:params:oauth:grant-type:apikey"),
                    new KeyValuePair<string, string>("apikey", _apiKey)
                });

                var response = await tokenClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);
                _iamToken = doc.RootElement.GetProperty("access_token").GetString() ?? "";

                int expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
                _tokenExpiration = DateTime.UtcNow.AddSeconds(expiresIn - 60);

                return _iamToken;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        public async Task<ImageAnalysisResult> AnalyzeImageStructuredAsync(byte[] imageData, string mimeType = "image/jpeg")
        {
            var rawText = await AnalyzeImageAsync(imageData, mimeType);

            var cleaned = rawText.Trim();
            if (cleaned.StartsWith("```"))
            {
                var firstNewline = cleaned.IndexOf('\n');
                var lastFence    = cleaned.LastIndexOf("```");
                if (firstNewline >= 0 && lastFence > firstNewline)
                    cleaned = cleaned.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }
            if (cleaned.StartsWith("json")) cleaned = cleaned.Substring(4).Trim();

            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;

                return new ImageAnalysisResult
                {
                    Description            = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Nsfw                   = root.TryGetProperty("nsfw", out var n) && IsTruthy(n),
                    ViolentOrOffensive     = root.TryGetProperty("violent_or_offensive", out var v) && IsTruthy(v),
                    IdentifiableRealPeople = root.TryGetProperty("identifiable_real_people", out var p) && IsTruthy(p),
                    BrandLogos             = root.TryGetProperty("brand_logos", out var b) && IsTruthy(b),
                    SensitiveOrConfidential = root.TryGetProperty("sensitive_or_confidential", out var s) && IsTruthy(s)
                };
            }
            catch (JsonException)
            {
                return new ImageAnalysisResult
                {
                    Description = "IBM Granite Vision returned an unparsable response: " +
                        (cleaned.Length > 100 ? cleaned[..100] : cleaned)
                };
            }
        }

        private static bool IsTruthy(JsonElement el) =>
            el.ValueKind == JsonValueKind.True ||
            (el.ValueKind == JsonValueKind.String && el.GetString()?.ToLower() == "true");

        public async Task<string> AnalyzeImageAsync(byte[] imageData, string mimeType = "image/jpeg")
        {
            try
            {
                var base64Image = Convert.ToBase64String(imageData);
                var token = await GetIamTokenAsync();

                var prompt = "Analyze this image for a content-risk review. Respond with ONLY a valid JSON object " +
                    "(no markdown, no extra text) in exactly this shape: " +
                    "{\"description\": \"<one short sentence describing image>\", " +
                    "\"nsfw\": <true or false>, \"violent_or_offensive\": <true or false>, " +
                    "\"identifiable_real_people\": <true or false>, \"brand_logos\": <true or false>, " +
                    "\"sensitive_or_confidential\": <true or false>}. " +
                    "Do not include any explanation.";

                var requestBody = new
                {
                    model_id   = "ibm/granite-vision-3-2-2b",
                    project_id = _projectId,
                    input = new
                    {
                        text = $"<|system|>\nYou are an AI vision assistant.\n<|end|>\n<|user|>\n<image>\n{prompt}\n<|end|>\n<|assistant|>\n",
                        images = new[]
                        {
                            new { data = base64Image, mime_type = mimeType }
                        }
                    },
                    parameters = new { max_new_tokens = 500, decoding_method = "greedy" }
                };

                var endpoint = "https://us-south.ml.cloud.ibm.com/ml/v1/text/generation?version=2023-05-29";

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                };
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(requestMessage);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    throw new Exception($"IBM Granite Vision returned {(int)response.StatusCode}: {errorBody}");
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);

                if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                    return results[0].GetProperty("generated_text").GetString() ?? string.Empty;

                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: IBM Granite Vision failed ({ex.Message}). Falling back to Google Gemini...");
                if (!string.IsNullOrEmpty(_geminiApiKey) && !string.IsNullOrEmpty(_geminiUrl))
                    return await AnalyzeImageWithGeminiAsync(imageData, mimeType);
                throw;
            }
        }

        private async Task<string> AnalyzeImageWithGeminiAsync(byte[] imageData, string mimeType = "image/jpeg")
        {
            var base64Image = Convert.ToBase64String(imageData);
            var prompt = "Analyze this image for a content-risk review. Respond with ONLY a valid JSON object " +
                "(no markdown, no extra text) in exactly this shape: " +
                "{\"description\": \"<one short sentence describing image>\", " +
                "\"nsfw\": <true or false>, \"violent_or_offensive\": <true or false>, " +
                "\"identifiable_real_people\": <true or false>, \"brand_logos\": <true or false>, " +
                "\"sensitive_or_confidential\": <true or false>}. " +
                "Do not include any explanation.";

            var requestBody = new
            {
                contents = new object[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new { inline_data = new { mime_type = mimeType, data = base64Image } }
                        }
                    }
                }
            };

            var endpoint = $"{_geminiUrl}?key={_geminiApiKey}";
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(requestMessage);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini Vision API returned {(int)response.StatusCode}: {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return text?.Trim() ?? string.Empty;
        }
    }
}
