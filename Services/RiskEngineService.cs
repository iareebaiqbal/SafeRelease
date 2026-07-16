using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContentRiskScanner.Models;
using Microsoft.Extensions.Configuration;

namespace ContentRiskScanner.Services
{
    public class RiskEngineService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _url;

        private readonly Dictionary<string, (string issue, int score)> _rules = new(StringComparer.OrdinalIgnoreCase)
        {
            ["IBM"] = ("Trademark reference: IBM — verify usage rights", 15),
            ["Apple"] = ("Trademark reference: Apple — requires permission", 20),
            ["Google"] = ("Brand mention: Google — attribution required", 15),
            ["Microsoft"] = ("Brand mention: Microsoft — verify licensing", 15),
            ["Samsung"] = ("Brand mention: Samsung — attribution required", 10),
            ["Nike"] = ("Trademark: Nike slogan/brand — requires permission", 25),
            ["copyright"] = ("Copyright claim detected in content", 35),
            ["confidential"] = ("Confidential information detected", 40),
            ["competitor"] = ("Competitor mention — brand risk", 30),
            ["guaranteed return"] = ("Financial compliance risk: guaranteed returns claim", 40),
            ["zero risk"] = ("Financial compliance risk: misleading investment claim", 40),
            ["Sony Music"] = ("Copyright: Sony Music — DMCA risk", 45),
            ["biometric"] = ("Privacy risk: biometric data collection mentioned", 35),
            ["medical history"] = ("GDPR violation: medical data collection", 40),
            ["under 13"] = ("COPPA violation: collecting data from minors", 50),
            ["without user consent"] = ("GDPR violation: data sharing without consent", 45),
            ["without consent"] = ("GDPR violation: no user consent", 40),
            ["no license"] = ("Licensing issue detected", 30),
            ["without any formal licensing"] = ("IP Risk: using technology without licensing", 35),
            ["replicates"] = ("Copyright risk: software replication detected", 30),
            ["inspired by"] = ("Potential trademark infringement: design inspiration", 20),
        };

        public RiskEngineService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = Environment.GetEnvironmentVariable("WATSON_API_KEY") ?? "";
            _url = Environment.GetEnvironmentVariable("WATSON_URL") ?? "";
        }

        public async Task<ScanResponse> AnalyzeAsync(ScanRequest request)
        {
            var issues = new List<string>();
            int score = 0;
            var content = request.Content;

            // --- Existing keyword rule engine ---
            foreach (var rule in _rules)
            {
                if (content.Contains(rule.Key, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(rule.Value.issue);
                    score += rule.Value.score;
                }
            }

            // --- Watson NLU call ---
            try
            {
                var watsonResult = await CallWatsonNluAsync(content);
                if (watsonResult != null)
                {
                    // Sentiment risk
                    if (watsonResult.Value.TryGetProperty("sentiment", out var sentiment) &&
                        sentiment.TryGetProperty("document", out var doc) &&
                        doc.TryGetProperty("label", out var label))
                    {
                        var sentimentLabel = label.GetString();
                        if (sentimentLabel == "negative")
                        {
                            issues.Add("Watson NLU: Negative sentiment detected in content");
                            score += 15;
                        }
                    }

                    // Entities risk (e.g. detecting company/person names Watson finds)
                    if (watsonResult.Value.TryGetProperty("entities", out var entities))
                    {
                        foreach (var entity in entities.EnumerateArray())
                        {
                            var type = entity.GetProperty("type").GetString();
                            var text = entity.GetProperty("text").GetString();
                            if (type == "Company")
                            {
                                issues.Add($"Watson NLU: Company entity detected — {text}");
                                score += 10;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Don't crash the whole scan if Watson call fails — log and continue with rule-based results
                issues.Add($"Watson NLU check skipped: {ex.Message}");
            }

            score = Math.Min(score, 100);

            string status = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk";

            string recommendation = score > 50
                ? "Do not publish. Serious brand, copyright, or compliance violations detected. Consult legal team immediately."
                : score > 20
                ? "Review before publishing. Some brand mentions or compliance issues need attention."
                : "Content appears safe for release. Minor review recommended.";

            return new ScanResponse
            {
                RiskScore = score,
                Issues = issues,
                Status = status,
                Recommendation = recommendation
            };
        }

        private async Task<JsonElement?> CallWatsonNluAsync(string content)
        {
            var endpoint = $"{_url}/v1/analyze?version=2022-04-07";

            var payload = new
            {
                text = content,
                features = new
                {
                    sentiment = new { },
                    entities = new { },
                    keywords = new { }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            // IBM Watson auth: username is literally "apikey", password is your API key
            var authBytes = Encoding.ASCII.GetBytes($"apikey:{_apiKey}");
            requestMessage.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            var response = await _httpClient.SendAsync(requestMessage);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Watson NLU returned {(int)response.StatusCode}: {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement.Clone();
        }
    }
}