using System.Text;
using System.Text.Json;

namespace ContentRiskScanner.Services
{
    // Structured, parsed result from the Vision model — used to build a short Issues list
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
        private readonly string _url;

        public ImageDetectionService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                ?? configuration["GEMINI_API_KEY"]
                ?? throw new InvalidOperationException(
                    "GEMINI_API_KEY missing. .env file me GEMINI_API_KEY=... add karein.");

            _url = Environment.GetEnvironmentVariable("GEMINI_URL")
                ?? configuration["GEMINI_URL"]
                ?? throw new InvalidOperationException(
                    "GEMINI_URL missing. .env file me GEMINI_URL=... add karein.");
        }

        /// <summary>
        /// Image ko analyze karke seedha structured (parsed) result deta hai —
        /// controller ko raw JSON string khud parse nahi karni parti.
        /// </summary>
        public async Task<ImageAnalysisResult> AnalyzeImageStructuredAsync(byte[] imageData, string mimeType = "image/jpeg")
        {
            var rawText = await AnalyzeImageAsync(imageData, mimeType);

            // Gemini kabhi kabhi ```json ... ``` fences ke sath wrap kar deta hai — hata dete hain
            var cleaned = rawText.Trim();
            if (cleaned.StartsWith("```"))
            {
                var firstNewline = cleaned.IndexOf('\n');
                var lastFence = cleaned.LastIndexOf("```");
                if (firstNewline >= 0 && lastFence > firstNewline)
                {
                    cleaned = cleaned.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
                }
            }

            try
            {
                using var doc = JsonDocument.Parse(cleaned);
                var root = doc.RootElement;

                return new ImageAnalysisResult
                {
                    Description = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Nsfw = root.TryGetProperty("nsfw", out var n) && n.GetBoolean(),
                    ViolentOrOffensive = root.TryGetProperty("violent_or_offensive", out var v) && v.GetBoolean(),
                    IdentifiableRealPeople = root.TryGetProperty("identifiable_real_people", out var p) && p.GetBoolean(),
                    BrandLogos = root.TryGetProperty("brand_logos", out var b) && b.GetBoolean(),
                    SensitiveOrConfidential = root.TryGetProperty("sensitive_or_confidential", out var s) && s.GetBoolean(),
                };
            }
            catch (JsonException)
            {
                // Model ne valid JSON nahi diya — raw text ko description ki tarah rakh lete hain,
                // baqi sab false (safe default), taake pipeline crash na ho.
                return new ImageAnalysisResult
                {
                    Description = "Vision model returned an unparsable response.",
                };
            }
        }

        // Image bytes + optional prompt leke Gemini Vision se analysis karwata hai (raw text/JSON string wapas deta hai)
        public async Task<string> AnalyzeImageAsync(byte[] imageData, string mimeType = "image/jpeg", string? prompt = null)
        {
            var base64Image = Convert.ToBase64String(imageData);

            var userPrompt = prompt ?? "Analyze this image for a content-risk review. Respond with ONLY a valid JSON object " +
                "(no markdown, no code fences, no extra text) in exactly this shape: " +
                "{\"description\": \"<one short sentence, max 15 words, describing the image>\", " +
                "\"nsfw\": <true or false>, \"violent_or_offensive\": <true or false>, " +
                "\"identifiable_real_people\": <true or false>, \"brand_logos\": <true or false>, " +
                "\"sensitive_or_confidential\": <true or false>}. " +
                "Set each boolean to true only if that issue is actually present in the image. " +
                "Do not include any explanation, breakdown, or extra fields — only this JSON object.";

            var requestBody = new
            {
                contents = new object[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = userPrompt },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = mimeType,
                                    data = base64Image
                                }
                            }
                        }
                    }
                }
            };

            var endpoint = $"{_url}?key={_apiKey}";

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

            // Gemini response shape: candidates[0].content.parts[0].text
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