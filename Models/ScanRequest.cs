namespace ContentRiskScanner.Models
{
    public class ScanRequest
    {
        public string Content { get; set; } = string.Empty;
        public string ContentType { get; set; } = "text";

        // Comma-separated list of selected "SCAN FOR" categories from the UI,
        // e.g. "Brand risk,Copyright,Compliance". Empty/null = scan for everything (default).
        public string Categories { get; set; } = string.Empty;
    }
}