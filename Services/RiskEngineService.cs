using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContentRiskScanner.Models;
using Microsoft.Extensions.Configuration;

namespace ContentRiskScanner.Services
{
    public class RiskEngineService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _url;

        // Category names match the UI "SCAN FOR" buttons exactly.
        // "Safety" is not a UI category — those rules always run regardless of
        // selected categories, since violence/self-harm/etc. checks shouldn't be skippable.
        private readonly Dictionary<string, (string issue, int score, string category)> _rules = new(StringComparer.OrdinalIgnoreCase)
        {
            // --- Brand / Trademark risk ---
            ["IBM"] = ("Trademark reference: IBM — verify usage rights", 15, "Brand risk"),
            ["Apple"] = ("Trademark reference: Apple — requires permission", 20, "Brand risk"),
            ["Google"] = ("Brand mention: Google — attribution required", 15, "Brand risk"),
            ["Microsoft"] = ("Brand mention: Microsoft — verify licensing", 15, "Brand risk"),
            ["Samsung"] = ("Brand mention: Samsung — attribution required", 10, "Brand risk"),
            ["Nike"] = ("Trademark: Nike slogan/brand — requires permission", 25, "Brand risk"),

            // --- Copyright risk ---
            ["copyright"] = ("Copyright claim detected in content", 35, "Copyright"),
            ["Sony Music"] = ("Copyright: Sony Music — DMCA risk", 45, "Copyright"),
            ["no license"] = ("Licensing issue detected", 30, "Copyright"),
            ["without any formal licensing"] = ("IP Risk: using technology without licensing", 35, "Copyright"),
            ["replicates"] = ("Copyright risk: software replication detected", 30, "Copyright"),
            ["inspired by"] = ("Potential trademark infringement: design inspiration", 20, "Copyright"),

            // --- Compliance / Financial risk ---
            ["guaranteed return"] = ("Financial compliance risk: guaranteed returns claim", 40, "Compliance"),
            ["zero risk"] = ("Financial compliance risk: misleading investment claim", 40, "Compliance"),
            ["confidential"] = ("Confidential information detected", 40, "Compliance"),
            ["competitor"] = ("Competitor mention — brand risk", 30, "Compliance"),

            // --- Privacy / GDPR / COPPA risk ---
            ["biometric"] = ("Privacy risk: biometric data collection mentioned", 35, "PII / GDPR"),
            ["medical history"] = ("GDPR violation: medical data collection", 40, "PII / GDPR"),
            ["under 13"] = ("COPPA violation: collecting data from minors", 50, "PII / GDPR"),
            ["without user consent"] = ("GDPR violation: data sharing without consent", 45, "PII / GDPR"),
            ["without consent"] = ("GDPR violation: no user consent", 40, "PII / GDPR"),

            // --- Violence / Threats / Safety risk — always checked, not filterable ---
            ["threaten"] = ("Violence risk: threatening language detected", 45, "Safety"),
            ["threatened"] = ("Violence risk: threatening language detected", 45, "Safety"),
            ["threat"] = ("Violence risk: threatening language detected", 40, "Safety"),
            ["attack"] = ("Violence risk: language suggesting physical harm", 40, "Safety"),
            ["hurt"] = ("Violence risk: language suggesting intent to harm", 35, "Safety"),
            ["kill"] = ("Violence risk: severe/lethal language detected", 55, "Safety"),
            ["shoot"] = ("Violence risk: weapon-related language detected", 55, "Safety"),
            ["weapon"] = ("Violence risk: weapon reference detected", 45, "Safety"),
            ["bomb"] = ("Violence risk: explosive/terrorism reference detected", 60, "Safety"),
            ["violence"] = ("Violence risk: explicit violence reference", 45, "Safety"),
            ["abuse"] = ("Safety risk: abuse-related language detected", 40, "Safety"),
            ["assault"] = ("Violence risk: assault reference detected", 45, "Safety"),
            ["harass"] = ("Harassment risk: harassment-related language detected", 35, "Safety"),
            ["harassment"] = ("Harassment risk: harassment-related language detected", 35, "Safety"),
            ["self-harm"] = ("Safety risk: self-harm reference detected", 55, "Safety"),
            ["suicide"] = ("Safety risk: suicide-related language detected", 55, "Safety"),
            ["hate speech"] = ("Safety risk: hate speech reference detected", 50, "Safety"),
            ["terrorism"] = ("Safety risk: terrorism-related language detected", 60, "Safety"),
            ["extremist"] = ("Safety risk: extremist language detected", 50, "Safety"),
        };

        // --- PII detection patterns (email, phone number) — category "PII / GDPR" ---
        private static readonly Regex EmailPattern = new(
            @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
            RegexOptions.Compiled);

        private static readonly Regex PhonePattern = new(
            @"(\+?\d[\d\s\-\.\(\)]{7,}\d)",
            RegexOptions.Compiled);

        public RiskEngineService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            _apiKey = Environment.GetEnvironmentVariable("WATSON_API_KEY")
                ?? configuration["WATSON_API_KEY"]
                ?? throw new InvalidOperationException(
                    "WATSON_API_KEY missing. Root folder me .env file banayein aur usme WATSON_API_KEY=... likhein, " +
                    "aur confirm karein Program.cs me Env.Load() call ho raha hai.");

            _url = Environment.GetEnvironmentVariable("WATSON_URL")
                ?? configuration["WATSON_URL"]
                ?? throw new InvalidOperationException(
                    "WATSON_URL missing. Root folder me .env file banayein aur usme WATSON_URL=... likhein, " +
                    "aur confirm karein Program.cs me Env.Load() call ho raha hai.");
        }

        public async Task<ScanResponse> AnalyzeAsync(ScanRequest request)
        {
            var issues = new List<string>();
            int score = 0;
            var content = request.Content;

            // --- Parse selected categories from the request ---
            // Empty/null Categories = no filter, scan for everything (backward compatible
            // with older frontend calls that don't send this field yet).
            var selectedCategories = (request.Categories ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool noFilter = selectedCategories.Count == 0;

            bool CategoryActive(string category) =>
                noFilter || category == "Safety" || selectedCategories.Contains(category);

            // --- Keyword rule engine (word-boundary match, filtered by selected category) ---
            foreach (var rule in _rules)
            {
                if (!CategoryActive(rule.Value.category)) continue;

                var pattern = $@"\b{Regex.Escape(rule.Key)}\b";
                if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
                {
                    issues.Add(rule.Value.issue);
                    score += rule.Value.score;
                }
            }

            // --- PII detection: email address (category "PII / GDPR") ---
            if (CategoryActive("PII / GDPR"))
            {
                var emailMatches = EmailPattern.Matches(content);
                if (emailMatches.Count > 0)
                {
                    issues.Add($"PII risk: email address detected in content ({emailMatches.Count} found) — verify consent/redaction before publishing");
                    score += 35;
                }

                var phoneMatches = PhonePattern.Matches(content);
                if (phoneMatches.Count > 0)
                {
                    issues.Add($"PII risk: phone number detected in content ({phoneMatches.Count} found) — verify consent/redaction before publishing");
                    score += 35;
                }
            }

            // --- Watson NLU call (Sentiment category = sentiment; Brand risk category = company entities) ---
            try
            {
                var watsonResult = await CallWatsonNluAsync(content);
                if (watsonResult != null)
                {
                    if (CategoryActive("Sentiment") &&
                        watsonResult.Value.TryGetProperty("sentiment", out var sentiment) &&
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

                    if (CategoryActive("Brand risk") &&
                        watsonResult.Value.TryGetProperty("entities", out var entities))
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
                issues.Add($"Watson NLU check skipped: {ex.Message}");
            }

            score = Math.Min(score, 100);

            string status = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk";

            string recommendation = score > 50
                ? "Do not publish. Serious brand, copyright, compliance, or safety violations detected. Consult legal team immediately."
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