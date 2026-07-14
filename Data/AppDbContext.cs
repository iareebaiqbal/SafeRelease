namespace ContentRiskScanner.Models
{
    public class ScanResult
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public int RiskScore { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Issues { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
