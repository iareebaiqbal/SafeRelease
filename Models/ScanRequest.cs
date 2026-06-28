namespace ContentRiskScanner.Models
{
    public class ScanRequest
    {
        public string Content { get; set; } = string.Empty;
        public string ContentType { get; set; } = "text";
    }
}