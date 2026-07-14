namespace ContentRiskScanner.Models
{
    public class ScanResponse
    {
        public int RiskScore { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<string> Issues { get; set; } = new();
        public string Recommendation { get; set; } = string.Empty;
    }
}