using Microsoft.AspNetCore.Mvc;
using ContentRiskScanner.Models;
using ContentRiskScanner.Services;
using Xabe.FFmpeg;

namespace ContentRiskScanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScanController : ControllerBase
    {
        private readonly RiskEngineService _riskEngine;
        private readonly SpeechToTextService _speechToText;
        private readonly ImageDetectionService _imageDetection;
        private readonly CosService _cos;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ContentRiskScanner.Data.AppDbContext _context;

        private static readonly string[] RiskyFilenameWords =
        [
            "leaked", "confidential", "nsfw", "private", "unreleased",
            "banned", "explicit", "restricted", "internal"
        ];

        public ScanController(
            RiskEngineService riskEngine,
            SpeechToTextService speechToText,
            ImageDetectionService imageDetection,
            CosService cos,
            IHttpClientFactory httpClientFactory,
            ContentRiskScanner.Data.AppDbContext context)
        {
            _riskEngine        = riskEngine;
            _speechToText      = speechToText;
            _imageDetection    = imageDetection;
            _cos               = cos;
            _httpClientFactory = httpClientFactory;
            _context           = context;
        }

        [HttpPost("scan")]
        public async Task<IActionResult> Scan([FromBody] ScanRequest request)
        {
            if (string.IsNullOrEmpty(request.Content))
                return BadRequest("Content is required");

            var response = await _riskEngine.AnalyzeAsync(request);

            var result = new ScanResult
            {
                Content    = request.Content,
                RiskScore  = response.RiskScore,
                Status     = response.Status,
                Issues     = string.Join(", ", response.Issues),
                CreatedAt  = DateTime.UtcNow
            };

            _context.Scans.Add(result);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                id             = result.Id,
                riskScore      = response.RiskScore,
                status         = response.Status,
                issues         = response.Issues,
                recommendation = response.Recommendation
            });
        }

        // --- THE "UNBREAKABLE" FALLBACK ARCHITECTURE ---
        // 1. Attempts Python Sidecar (IBM Watson/Granite) first.
        // 2. If it fails, catches the error and runs native C# processing safely.
        [HttpPost("scan-media")]
        [RequestSizeLimit(150_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 150_000_000)]
        public async Task<IActionResult> ScanMedia(IFormFile file, [FromForm] string contentType, [FromForm] string? categories)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required");

            var mediaType      = contentType ?? "media";
            var categoriesValue = categories ?? string.Empty;
            ScanResponse? response = null;
            string rawAnalysisString = "";

            // --- STEP 1: Attempt Primary Microservice (Python Sidecar + IBM) ---
            try
            {
                // FIXED: use IHttpClientFactory instead of new HttpClient() to avoid socket exhaustion
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5);

                using var formContent = new MultipartFormDataContent();
                using var stream = file.OpenReadStream();
                formContent.Add(new StreamContent(stream), "file", file.FileName);
                formContent.Add(new StringContent(mediaType), "contentType");

                var pythonSidecarUrl = Environment.GetEnvironmentVariable("PYTHON_SIDECAR_URL")
                    ?? "http://localhost:8000/api/parse";

                var sidecarResponse = await httpClient.PostAsync(pythonSidecarUrl, formContent);

                if (sidecarResponse.IsSuccessStatusCode)
                {
                    rawAnalysisString = await sidecarResponse.Content.ReadAsStringAsync();
                    var sidecarResult = System.Text.Json.JsonDocument.Parse(rawAnalysisString).RootElement;
                    var analysis      = sidecarResult.GetProperty("analysis");

                    response = new ScanResponse
                    {
                        RiskScore      = analysis.GetProperty("risk_score").GetInt32(),
                        Status         = analysis.GetProperty("status").GetString() ?? "Unknown",
                        Issues         = analysis.GetProperty("issues").EnumerateArray().Select(x => x.GetString()).ToList(),
                        Recommendation = analysis.TryGetProperty("recommendation", out var rec) ? rec.GetString() ?? "" : ""
                    };

                    Console.WriteLine("SUCCESS: Processed media via Python Sidecar (IBM Granite/Watson)");
                }
                else
                {
                    Console.WriteLine($"WARNING: Python sidecar returned HTTP {(int)sidecarResponse.StatusCode}. Falling back to C# Native...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WARNING: Python sidecar connection failed ({ex.Message}). Falling back to C# Native...");
            }

            // --- STEP 2: Fallback to C# if sidecar failed ---
            if (response == null)
            {
                if (mediaType.Equals("voice", StringComparison.OrdinalIgnoreCase))
                {
                    string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{file.FileName}");
                    try
                    {
                        using (var fs = new FileStream(tempAudioPath, FileMode.Create))
                            await file.CopyToAsync(fs);

                        var audioContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "audio/wav" : file.ContentType;
                        response = await AnalyzeAudioAsync(tempAudioPath, audioContentType, file.FileName ?? string.Empty, categoriesValue);
                    }
                    finally
                    {
                        if (System.IO.File.Exists(tempAudioPath)) System.IO.File.Delete(tempAudioPath);
                    }
                }
                else if (mediaType.Equals("video", StringComparison.OrdinalIgnoreCase))
                    response = await AnalyzeVideoFileAsync(file, categoriesValue);
                else if (mediaType.Equals("image", StringComparison.OrdinalIgnoreCase))
                    response = await AnalyzeImageFileAsync(file);
                else
                    response = AnalyzeMediaFile(file, mediaType);
            }

            // Save to database
            var dbResult = new ScanResult
            {
                Content   = $"[{mediaType.ToUpper()}] {file.FileName}",
                RiskScore = response.RiskScore,
                Status    = response.Status,
                Issues    = string.Join(", ", response.Issues),
                CreatedAt = DateTime.UtcNow
            };

            _context.Scans.Add(dbResult);
            await _context.SaveChangesAsync();

            // NEW: IBM Cloud Object Storage — archive the scanned file for audit trail
            string? cosUrl = null;
            if (_cos.IsConfigured)
            {
                using var ms = new MemoryStream();
                await file.OpenReadStream().CopyToAsync(ms);
                cosUrl = await _cos.UploadScanFileAsync(dbResult.Id, file.FileName, ms.ToArray(),
                    string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            }

            return Ok(new
            {
                id             = dbResult.Id,
                fileName       = file.FileName,
                contentType    = mediaType,
                riskScore      = response.RiskScore,
                status         = response.Status,
                issues         = response.Issues,
                recommendation = response.Recommendation,
                rawAnalysis    = rawAnalysisString,
                auditFileUrl   = cosUrl
            });
        }

        private async Task<ScanResponse> AnalyzeImageFileAsync(IFormFile file)
        {
            var issues   = new List<string>();
            var fileName = file.FileName ?? string.Empty;
            int score    = 0;

            foreach (var word in RiskyFilenameWords)
            {
                if (fileName.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Filename risk: '{word}' found in image filename — review before release");
                    score += 30;
                }
            }

            try
            {
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();
                var mimeType   = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType;

                var vision = await _imageDetection.AnalyzeImageStructuredAsync(imageBytes, mimeType);

                if (vision.Nsfw)                    { issues.Add("Vision: NSFW/adult content detected");               score += 60; }
                if (vision.ViolentOrOffensive)      { issues.Add("Vision: violent or offensive content detected");     score += 55; }
                if (vision.IdentifiableRealPeople)  { issues.Add("Vision: identifiable real person detected");         score += 30; }
                if (vision.BrandLogos)              { issues.Add("Vision: brand logo/trademark detected");             score += 20; }
                if (vision.SensitiveOrConfidential) { issues.Add("Vision: sensitive/confidential info visible");       score += 40; }

                if (!vision.Nsfw && !vision.ViolentOrOffensive && !vision.IdentifiableRealPeople &&
                    !vision.BrandLogos && !vision.SensitiveOrConfidential)
                    issues.Add($"Vision: no risk flags. ({vision.Description})");
            }
            catch (Exception ex)
            {
                issues.Add($"Image analysis failed: {ex.Message}. Filename-only checks ran.");
            }

            score = Math.Min(score, 100);
            return new ScanResponse
            {
                RiskScore      = score,
                Issues         = issues,
                Status         = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk",
                Recommendation = score > 20 ? "Review before publishing." : "Content appears safe."
            };
        }

        private async Task<ScanResponse> AnalyzeVideoFileAsync(IFormFile file, string categories)
        {
            var issues        = new List<string>();
            var fileName      = file.FileName ?? string.Empty;
            int filenameScore = 0;

            foreach (var word in RiskyFilenameWords)
            {
                if (fileName.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Filename risk: '{word}' found in video filename");
                    filenameScore += 30;
                }
            }

            string tempVideoPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{fileName}");
            string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

            try
            {
                using (var fs = new FileStream(tempVideoPath, FileMode.Create))
                    await file.CopyToAsync(fs);

                IMediaInfo mediaInfo   = await FFmpeg.GetMediaInfo(tempVideoPath);
                IAudioStream? audioStream = mediaInfo.AudioStreams.FirstOrDefault();

                if (audioStream == null)
                {
                    issues.Add("No audio track found in video — voice analysis skipped.");
                    int score = Math.Min(filenameScore, 100);
                    return new ScanResponse { RiskScore = score, Issues = issues, Status = score > 20 ? "Medium Risk" : "Low Risk", Recommendation = "Manual review recommended." };
                }

                await FFmpeg.Conversions.New().AddStream(audioStream).SetOutput(tempAudioPath).Start();
                var voiceResult    = await AnalyzeAudioAsync(tempAudioPath, "audio/wav", string.Empty, categories);
                var combinedIssues = new List<string>(issues);
                combinedIssues.AddRange(voiceResult.Issues);
                int combinedScore = Math.Min(filenameScore + voiceResult.RiskScore, 100);

                return new ScanResponse
                {
                    RiskScore      = combinedScore,
                    Issues         = combinedIssues,
                    Status         = combinedScore > 50 ? "High Risk" : combinedScore > 20 ? "Medium Risk" : "Low Risk",
                    Recommendation = combinedScore > 20 ? "Review video audio before publishing." : "Safe."
                };
            }
            catch (Exception ex)
            {
                issues.Add($"Video fallback processing failed: {ex.Message}");
                return new ScanResponse { RiskScore = Math.Min(filenameScore, 100), Issues = issues, Status = "Medium Risk", Recommendation = "Processing failed. Manual review." };
            }
            finally
            {
                if (System.IO.File.Exists(tempVideoPath)) System.IO.File.Delete(tempVideoPath);
                if (System.IO.File.Exists(tempAudioPath)) System.IO.File.Delete(tempAudioPath);
            }
        }

        private async Task<ScanResponse> AnalyzeAudioAsync(string filePath, string audioContentType, string fileName, string categories = "")
        {
            var issues = new List<string>();
            int score  = 0;

            foreach (var word in RiskyFilenameWords)
            {
                if (fileName.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Filename risk: '{word}' found in filename");
                    score += 30;
                }
            }

            string transcript;
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                transcript = await _speechToText.TranscribeStreamAsync(fileStream, audioContentType);
            }
            catch (Exception ex)
            {
                issues.Add($"Speech-to-Text failed: {ex.Message}");
                return new ScanResponse { RiskScore = Math.Min(score, 100), Issues = issues, Status = score > 20 ? "Medium Risk" : "Low Risk", Recommendation = "Transcription failed." };
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                issues.Add("No speech detected in audio.");
            }
            else
            {
                issues.Add($"Transcribed content: \"{transcript}\"");
                var nluResult = await _riskEngine.AnalyzeAsync(new ScanRequest { Content = transcript, Categories = categories });
                issues.AddRange(nluResult.Issues);
                score += nluResult.RiskScore;
            }

            score = Math.Min(score, 100);
            return new ScanResponse
            {
                RiskScore      = score,
                Issues         = issues,
                Status         = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk",
                Recommendation = "Review audio."
            };
        }

        private static ScanResponse AnalyzeMediaFile(IFormFile file, string mediaType)
        {
            var issues   = new List<string>();
            var fileName = file.FileName ?? string.Empty;
            if (RiskyFilenameWords.Any(w => fileName.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add($"Filename risk found in {mediaType}");
                return new ScanResponse { RiskScore = 30, Issues = issues, Status = "Medium Risk", Recommendation = "Review" };
            }
            return new ScanResponse { RiskScore = 0, Issues = issues, Status = "Low Risk", Recommendation = "Safe" };
        }

        // FIXED: structured error response + IHttpClientFactory instead of new HttpClient()
        [HttpPost("scan-file")]
        public async Task<IActionResult> ScanFile(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("No file uploaded");

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                using var content = new MultipartFormDataContent();
                using var stream  = file.OpenReadStream();
                content.Add(new StreamContent(stream), "file", file.FileName);

                var sidecarUrl   = Environment.GetEnvironmentVariable("PYTHON_SIDECAR_URL") ?? "http://localhost:8000/api/parse";
                var httpResponse = await httpClient.PostAsync(sidecarUrl, content);

                if (!httpResponse.IsSuccessStatusCode)
                    return Ok(new
                    {
                        id = 0, riskScore = 0, status = "Error",
                        issues = new[] { $"Sidecar returned HTTP {(int)httpResponse.StatusCode}" },
                        recommendation = "Python sidecar is unreachable. Ensure it is running."
                    });

                var sidecarResultString = await httpResponse.Content.ReadAsStringAsync();
                var analysis = System.Text.Json.JsonDocument.Parse(sidecarResultString).RootElement.GetProperty("analysis");

                var dbResult = new ScanResult
                {
                    Content   = file.FileName,
                    RiskScore = analysis.GetProperty("risk_score").GetInt32(),
                    Status    = analysis.GetProperty("status").GetString() ?? "Unknown",
                    Issues    = string.Join(", ", analysis.GetProperty("issues").EnumerateArray().Select(x => x.GetString())),
                    CreatedAt = DateTime.UtcNow
                };
                _context.Scans.Add(dbResult);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    id          = dbResult.Id,
                    riskScore   = dbResult.RiskScore,
                    status      = dbResult.Status,
                    issues      = dbResult.Issues.Split(", "),
                    rawAnalysis = sidecarResultString
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    id = 0, riskScore = 0, status = "Error",
                    issues = new[] { $"Scan failed: {ex.Message}" },
                    recommendation = "An unexpected error occurred. Check server logs."
                });
            }
        }

        [HttpGet("report/{id}")]
        public async Task<IActionResult> Report(int id)
        {
            var result = await _context.Scans.FindAsync(id);
            if (result == null) return NotFound("Report not found");
            return Ok(result);
        }
    }
}
