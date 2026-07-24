using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ContentRiskScanner.Services
{
    /// <summary>
    /// IBM Cloud Object Storage — stores original scanned files as an audit trail.
    /// Uses the S3-compatible REST API with HMAC credentials (AWS Signature V4).
    /// Free tier: 25 GB storage + 25 GB outbound/month (Lite plan).
    /// </summary>
    public class CosService
    {
        private readonly HttpClient _httpClient;
        private readonly string? _endpoint;
        private readonly string? _bucket;
        private readonly string? _accessKey;
        private readonly string? _secretKey;

        public CosService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _endpoint  = Environment.GetEnvironmentVariable("COS_ENDPOINT")  ?? configuration["COS_ENDPOINT"];
            _bucket    = Environment.GetEnvironmentVariable("COS_BUCKET")    ?? configuration["COS_BUCKET"];
            _accessKey = Environment.GetEnvironmentVariable("COS_ACCESS_KEY_ID")     ?? configuration["COS_ACCESS_KEY_ID"];
            _secretKey = Environment.GetEnvironmentVariable("COS_SECRET_ACCESS_KEY") ?? configuration["COS_SECRET_ACCESS_KEY"];
        }

        public bool IsConfigured =>
            !string.IsNullOrEmpty(_endpoint) && !string.IsNullOrEmpty(_bucket) &&
            !string.IsNullOrEmpty(_accessKey) && !string.IsNullOrEmpty(_secretKey);

        /// <summary>
        /// Uploads <paramref name="data"/> to COS under key <c>scans/{scanId}/{fileName}</c>.
        /// Returns the object URL on success, or null on failure/disabled.
        /// </summary>
        public async Task<string?> UploadScanFileAsync(int scanId, string fileName, byte[] data, string contentType)
        {
            if (!IsConfigured) return null;

            var key = $"scans/{scanId}/{fileName}";
            var url = $"{_endpoint}/{_bucket}/{key}";

            try
            {
                var now = DateTime.UtcNow;
                var dateStamp  = now.ToString("yyyyMMdd");
                var amzDate    = now.ToString("yyyyMMddTHHmmssZ");
                var region     = "us-standard"; // COS uses "us-standard" for global

                // Build canonical request for AWS Sig V4
                var payloadHash = BitConverter.ToString(SHA256.HashData(data)).Replace("-", "").ToLower();
                var canonicalHeaders = $"host:{new Uri(url).Host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";
                var signedHeaders    = "host;x-amz-content-sha256;x-amz-date";
                var canonicalRequest = $"PUT\n/{_bucket}/{key}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

                var credentialScope = $"{dateStamp}/{region}/s3/aws4_request";
                var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{credentialScope}\n" +
                    BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))).Replace("-", "").ToLower();

                byte[] SignKey(byte[] key, string msg) =>
                    HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(msg));

                var signingKey = SignKey(SignKey(SignKey(SignKey(
                    Encoding.UTF8.GetBytes($"AWS4{_secretKey}"),
                    dateStamp), region), "s3"), "aws4_request");

                var signature = BitConverter.ToString(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(stringToSign)))
                    .Replace("-", "").ToLower();

                var authHeader = $"AWS4-HMAC-SHA256 Credential={_accessKey}/{credentialScope}," +
                    $" SignedHeaders={signedHeaders}, Signature={signature}";

                using var request = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = new ByteArrayContent(data)
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
                request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode ? url : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: COS upload failed ({ex.Message}). Scan saved without file archive.");
                return null;
            }
        }
    }
}
