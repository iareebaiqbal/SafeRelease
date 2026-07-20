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
        private readonly ContentRiskScanner.Data.AppDbContext _context;

        private static readonly string[] RiskyFilenameWords = new[]
        {
            "leaked", "confidential", "nsfw", "private", "unreleased",
            "banned", "explicit", "restricted", "internal"
        };

        public ScanController(
            RiskEngineService riskEngine,
            SpeechToTextService speechToText,
            ImageDetectionService imageDetection,
            ContentRiskScanner.Data.AppDbContext context)
        {
            _riskEngine = riskEngine;
            _speechToText = speechToText;
            _imageDetection = imageDetection;
            _context = context;
        }

        [HttpPost("scan")]
        public async Task<IActionResult> Scan([FromBody] ScanRequest request)
        {
            if (string.IsNullOrEmpty(request.Content))
                return BadRequest("Content is required");

            var response = await _riskEngine.AnalyzeAsync(request);

            // Save to database
            var result = new ScanResult
            {
                Content = request.Content,
                RiskScore = response.RiskScore,
                Status = response.Status,
                Issues = string.Join(", ", response.Issues),
                CreatedAt = DateTime.UtcNow
            };

            _context.Scans.Add(result);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = result.Id,
                riskScore = response.RiskScore,
                status = response.Status,
                issues = response.Issues,
                recommendation = response.Recommendation
            });
        }

        // Matches frontend fetch('/api/scan/scan-media', { method: 'POST', body: fd })
        // fd contains: file (the uploaded file), contentType ('image' | 'video' | 'voice')
        [HttpPost("scan-media")]
        [RequestSizeLimit(150_000_000)] // allow up to ~150MB for video uploads
        [RequestFormLimits(MultipartBodyLengthLimit = 150_000_000)]
        public async Task<IActionResult> ScanMedia(IFormFile file, [FromForm] string contentType, [FromForm] string? categories)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required");

            var mediaType = contentType ?? "media";
            var categoriesValue = categories ?? string.Empty;
            ScanResponse response;

            // --- Voice files: actual detection via Speech-to-Text + NLU ---
            if (mediaType.Equals("voice", StringComparison.OrdinalIgnoreCase))
            {
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                var audioBytes = memoryStream.ToArray();

                var audioContentType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "audio/wav"
                    : file.ContentType;

                response = await AnalyzeAudioAsync(audioBytes, audioContentType, file.FileName ?? string.Empty, categoriesValue);
            }
            // --- Video files: extract audio via FFmpeg, then reuse voice pipeline ---
            else if (mediaType.Equals("video", StringComparison.OrdinalIgnoreCase))
            {
                response = await AnalyzeVideoFileAsync(file, categoriesValue);
            }
            // --- Image: actual detection via Vision model ---
            else if (mediaType.Equals("image", StringComparison.OrdinalIgnoreCase))
            {
                response = await AnalyzeImageFileAsync(file);
            }
            // --- Anything else: filename + size checks only (deep analysis not configured) ---
            else
            {
                response = AnalyzeMediaFile(file, mediaType);
            }

            // Save to database
            var dbResult = new ScanResult
            {
                Content = $"[{mediaType.ToUpper()}] {file.FileName}",
                RiskScore = response.RiskScore,
                Status = response.Status,
                Issues = string.Join(", ", response.Issues),
                CreatedAt = DateTime.UtcNow
            };

            _context.Scans.Add(dbResult);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = dbResult.Id,
                fileName = file.FileName,
                contentType = mediaType,
                riskScore = response.RiskScore,
                status = response.Status,
                issues = response.Issues,
                recommendation = response.Recommendation
            });
        }

        private async Task<ScanResponse> AnalyzeImageFileAsync(IFormFile file)
        {
            var issues = new List<string>();
            var fileName = file.FileName ?? string.Empty;
            int score = 0;

            // Filename keyword check (quick first pass)
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

                var mimeType = string.IsNullOrWhiteSpace(file.ContentType)
                    ? "image/jpeg"
                    : file.ContentType;

                // --- Structured result: JSON true/false flags, NOT a long description ---
                var vision = await _imageDetection.AnalyzeImageStructuredAsync(imageBytes, mimeType);

                if (vision.Nsfw)
                {
                    issues.Add("Vision: NSFW/adult content detected in image");
                    score += 60;
                }
                if (vision.ViolentOrOffensive)
                {
                    issues.Add("Vision: violent or offensive content detected in image");
                    score += 55;
                }
                if (vision.IdentifiableRealPeople)
                {
                    issues.Add("Vision: identifiable real person detected in image — verify consent/rights");
                    score += 30;
                }
                if (vision.BrandLogos)
                {
                    issues.Add("Vision: brand logo/trademark detected in image — verify usage rights");
                    score += 20;
                }
                if (vision.SensitiveOrConfidential)
                {
                    issues.Add("Vision: sensitive or confidential information visible in image");
                    score += 40;
                }

                // Koi risk flag nahi mila to sirf chhota summary (poori description nahi)
                if (!vision.Nsfw && !vision.ViolentOrOffensive && !vision.IdentifiableRealPeople &&
                    !vision.BrandLogos && !vision.SensitiveOrConfidential)
                {
                    var shortDescription = vision.Description.Length > 100
                        ? vision.Description.Substring(0, 100) + "..."
                        : vision.Description;
                    issues.Add($"Vision: no risk flags detected. ({shortDescription})");
                }
            }
            catch (Exception ex)
            {
                issues.Add($"Image analysis failed: {ex.Message}. Falling back to filename-only checks.");
            }

            score = Math.Min(score, 100);
            string status = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk";

            string recommendation = score > 50
                ? "Do not publish. Serious issues detected in image content. Consult legal team immediately."
                : score > 20
                ? "Review before publishing. Some risk detected in image content."
                : "Image content appears safe for release. Minor review recommended.";

            return new ScanResponse
            {
                RiskScore = score,
                Issues = issues,
                Status = status,
                Recommendation = recommendation
            };
        }

        private async Task<ScanResponse> AnalyzeVideoFileAsync(IFormFile file, string categories)
        {
            var issues = new List<string>();
            var fileName = file.FileName ?? string.Empty;

            // Filename keyword check (quick first pass, same as before)
            int filenameScore = 0;
            foreach (var word in RiskyFilenameWords)
            {
                if (fileName.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Filename risk: '{word}' found in video filename — review before release");
                    filenameScore += 30;
                }
            }

            string tempVideoPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{fileName}");
            string tempAudioPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

            try
            {
                // Save uploaded video to a temp file (FFmpeg needs a file path, not a stream)
                using (var fileStream = new FileStream(tempVideoPath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                // Extract audio track as WAV
                IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(tempVideoPath);
                IAudioStream? audioStream = mediaInfo.AudioStreams.FirstOrDefault();

                if (audioStream == null)
                {
                    issues.Add("No audio track found in video — voice analysis skipped.");
                    int score = Math.Min(filenameScore, 100);
                    return new ScanResponse
                    {
                        RiskScore = score,
                        Issues = issues,
                        Status = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk",
                        Recommendation = "Video has no audio track. Only filename-based checks were run."
                    };
                }

                await FFmpeg.Conversions.New()
                    .AddStream(audioStream)
                    .SetOutput(tempAudioPath)
                    .Start();

                var audioBytes = await System.IO.File.ReadAllBytesAsync(tempAudioPath);

                // Reuse the same voice analysis pipeline (STT + NLU).
                // NOTE: pass empty string for fileName here — the video filename was
                // already checked above (filenameScore). Passing it again would
                // double-count the same filename match inside AnalyzeAudioAsync.
                var voiceResult = await AnalyzeAudioAsync(audioBytes, "audio/wav", string.Empty, categories);

                // Merge filename issues (from the video file itself) with voice/transcript issues
                var combinedIssues = new List<string>(issues);
                combinedIssues.AddRange(voiceResult.Issues);

                int combinedScore = Math.Min(filenameScore + voiceResult.RiskScore, 100);
                string status = combinedScore > 50 ? "High Risk" : combinedScore > 20 ? "Medium Risk" : "Low Risk";

                string recommendation = combinedScore > 50
                    ? "Do not publish. Serious issues detected in video's audio content. Consult legal team immediately."
                    : combinedScore > 20
                    ? "Review before publishing. Some risk detected in video's audio content."
                    : "Video audio content appears safe for release. Minor review recommended.";

                return new ScanResponse
                {
                    RiskScore = combinedScore,
                    Issues = combinedIssues,
                    Status = status,
                    Recommendation = recommendation
                };
            }
            catch (Exception ex)
            {
                issues.Add($"Audio extraction from video failed: {ex.Message}. Falling back to filename-only checks.");
                int score = Math.Min(filenameScore, 100);
                return new ScanResponse
                {
                    RiskScore = score,
                    Issues = issues,
                    Status = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk",
                    Recommendation = "Video audio could not be processed. Manual review recommended."
                };
            }
            finally
            {
                // Clean up temp files
                if (System.IO.File.Exists(tempVideoPath)) System.IO.File.Delete(tempVideoPath);
                if (System.IO.File.Exists(tempAudioPath)) System.IO.File.Delete(tempAudioPath);
            }
        }

        // Shared by voice uploads and video-extracted audio
        private async Task<ScanResponse> AnalyzeAudioAsync(byte[] audioBytes, string audioContentType, string fileName, string categories = "")
        {
            var issues = new List<string>();
            int score = 0;

            foreach (var word in RiskyFilenameWords)
            {
                if (fileName.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Filename risk: '{word}' found in filename — review before release");
                    score += 30;
                }
            }

            string transcript;
            try
            {
                transcript = await _speechToText.TranscribeAsync(audioBytes, audioContentType);
            }
            catch (Exception ex)
            {
                issues.Add($"Speech-to-Text failed: {ex.Message}. Falling back to filename-only checks.");
                score = Math.Min(score, 100);
                return new ScanResponse
                {
                    RiskScore = score,
                    Issues = issues,
                    Status = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk",
                    Recommendation = "Audio could not be transcribed. Manual review recommended."
                };
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
            string status = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk";

            string recommendation = score > 50
                ? "Do not publish. Serious issues detected in audio content. Consult legal team immediately."
                : score > 20
                ? "Review before publishing. Some risk detected in transcribed content."
                : "Audio content appears safe for release. Minor review recommended.";

            return new ScanResponse
            {
                RiskScore = score,
                Issues = issues,
                Status = status,
                Recommendation = recommendation
            };
        }

        private ScanResponse AnalyzeMediaFile(IFormFile file, string mediaType)
        {
            var issues = new List<string>();
            int score = 0;
            var fileName = file.FileName ?? string.Empty;
            var sizeInMb = Math.Round(file.Length / 1024.0 / 1024.0, 2);

            foreach (var word in RiskyFilenameWords)
            {
                if (fileName.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Filename risk: '{word}' found in {mediaType} filename — review before release");
                    score += 30;
                }
            }

            if (sizeInMb > 50)
            {
                issues.Add($"Large {mediaType} file ({sizeInMb} MB) — manual review recommended before publishing");
                score += 10;
            }

            issues.Add($"Note: Deep {mediaType} content analysis is not yet configured. " +
                       $"This scan currently checks filename and file properties only.");

            score = Math.Min(score, 100);
            string status = score > 50 ? "High Risk" : score > 20 ? "Medium Risk" : "Low Risk";

            string recommendation = score > 50
                ? "Do not publish. Filename or size flags detected — manual review required."
                : score > 20
                ? "Review before publishing."
                : $"No filename-based risks detected. Note: full {mediaType} content analysis is limited in this version.";

            return new ScanResponse
            {
                RiskScore = score,
                Issues = issues,
                Status = status,
                Recommendation = recommendation
            };
        }

        // Collaborator's original scan-media implementation, kept intact and
        // available at a separate route (Python Sidecar based analysis).
        // The primary /scan-media route above uses in-process C# analysis instead,
        // but this version is preserved rather than deleted.
        [HttpPost("scan-media-sidecar")]
        public async Task<IActionResult> ScanMediaSidecar([FromForm] IFormFile file, [FromForm] string contentType)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5); // Video/audio can take time

            using var formContent = new MultipartFormDataContent();

            using var stream = file.OpenReadStream();
            var streamContent = new StreamContent(stream);
            formContent.Add(streamContent, "file", file.FileName);
            formContent.Add(new StringContent(contentType ?? "image"), "contentType");

            var pythonSidecarUrl = Environment.GetEnvironmentVariable("PYTHON_SIDECAR_URL") ?? "http://localhost:8000/api/parse";
            var sidecarResponse = await httpClient.PostAsync(pythonSidecarUrl, formContent);

            if (!sidecarResponse.IsSuccessStatusCode)
            {
                var errorBody = await sidecarResponse.Content.ReadAsStringAsync();
                return StatusCode(500, $"Python sidecar error ({contentType}): {errorBody}");
            }

            var sidecarResultString = await sidecarResponse.Content.ReadAsStringAsync();
            var sidecarResult = System.Text.Json.JsonDocument.Parse(sidecarResultString).RootElement;

            var analysis = sidecarResult.GetProperty("analysis");

            int riskScore = analysis.GetProperty("risk_score").GetInt32();
            string status = analysis.GetProperty("status").GetString() ?? "Unknown";
            var issuesList = analysis.GetProperty("issues").EnumerateArray().Select(x => x.GetString()).ToList();
            string recommendation = analysis.TryGetProperty("recommendation", out var rec) ? rec.GetString() ?? "" : "";
            string issuesStr = string.Join(", ", issuesList);

            var sidecarDbResult = new ScanResult
            {
                Content = $"[{contentType?.ToUpper()}] {file.FileName}",
                RiskScore = riskScore,
                Status = status,
                Issues = issuesStr,
                CreatedAt = DateTime.UtcNow
            };

            _context.Scans.Add(sidecarDbResult);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = sidecarDbResult.Id,
                fileName = file.FileName,
                contentType,
                riskScore,
                status,
                issues = issuesList,
                recommendation,
                rawAnalysis = sidecarResultString
            });
        }

        // Document uploads (pdf, docx, etc.) still go through the Python Sidecar,
        // since text-document parsing is a separate concern from media analysis.
        [HttpPost("scan-file")]
        public async Task<IActionResult> ScanFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            using var httpClient = new HttpClient();
            using var content = new MultipartFormDataContent();

            using var stream = file.OpenReadStream();
            var streamContent = new StreamContent(stream);
            content.Add(streamContent, "file", file.FileName);

            var pythonSidecarUrl = Environment.GetEnvironmentVariable("PYTHON_SIDECAR_URL") ?? "http://localhost:8000/api/parse";
            var sidecarResponse = await httpClient.PostAsync(pythonSidecarUrl, content);

            if (!sidecarResponse.IsSuccessStatusCode)
            {
                return StatusCode(500, "Error connecting to Python Sidecar for document parsing.");
            }

            var sidecarResultString = await sidecarResponse.Content.ReadAsStringAsync();
            var sidecarResult = System.Text.Json.JsonDocument.Parse(sidecarResultString).RootElement;

            var analysis = sidecarResult.GetProperty("analysis");

            // Extract the result from the sidecar
            int riskScore = analysis.GetProperty("risk_score").GetInt32();
            string status = analysis.GetProperty("status").GetString() ?? "Unknown";
            var issuesList = analysis.GetProperty("issues").EnumerateArray().Select(x => x.GetString()).ToList();
            string issuesStr = string.Join(", ", issuesList);

            // Save to database
            var dbResult = new ScanResult
            {
                Content = file.FileName,
                RiskScore = riskScore,
                Status = status,
                Issues = issuesStr,
                CreatedAt = DateTime.UtcNow
            };

            _context.Scans.Add(dbResult);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                id = dbResult.Id,
                fileName = file.FileName,
                riskScore = riskScore,
                status = status,
                issues = issuesList,
                rawAnalysis = sidecarResultString
            });
        }

        [HttpGet("report/{id}")]
        public async Task<IActionResult> Report(int id)
        {
            var result = await _context.Scans.FindAsync(id);
            if (result == null)
            {
                return NotFound("Report not found");
            }
            return Ok(result);
        }
    }
}