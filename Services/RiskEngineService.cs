using ContentRiskScanner.Models;

namespace ContentRiskScanner.Services
{
    public class RiskEngineService
    {
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

        public async Task<ScanResponse> AnalyzeAsync(ScanRequest request)
        {
            var issues = new List<string>();
            int score = 0;
            var content = request.Content;

            foreach (var rule in _rules)
            {
                if (content.Contains(rule.Key, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(rule.Value.issue);
                    score += rule.Value.score;
                }
            }

            score = Math.Min(score, 100);

            string status = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk";

            string recommendation = score > 50
                ? "Do not publish. Serious brand, copyright, or compliance violations detected. Consult legal team immediately."
                : score > 20
                ? "Review before publishing. Some brand mentions or compliance issues need attention."
                : "Content appears safe for release. Minor review recommended.";

            var response = new ScanResponse
            {
                RiskScore = score,
                Issues = issues,
                Status = status,
                Recommendation = recommendation
            };

            return await Task.FromResult(response);
        }
    }
}