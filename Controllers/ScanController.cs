using Microsoft.AspNetCore.Mvc;
using ContentRiskScanner.Models;
using ContentRiskScanner.Services;

namespace ContentRiskScanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScanController : ControllerBase
    {
        private readonly RiskEngineService _riskEngine;
        private readonly ContentRiskScanner.Data.AppDbContext _context;

        public ScanController(RiskEngineService riskEngine, ContentRiskScanner.Data.AppDbContext context)
        {
            _riskEngine = riskEngine;
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

            return Ok(new { 
                id = dbResult.Id, 
                fileName = file.FileName, 
                riskScore = riskScore, 
                status = status, 
                issues = issuesList,
                rawAnalysis = sidecarResultString
            });
        }

        // Frontend calls THIS endpoint for image/video/voice tabs
        // It sends: multipart/form-data with 'file' + 'contentType' fields
        [HttpPost("scan-media")]
        public async Task<IActionResult> ScanMedia([FromForm] IFormFile file, [FromForm] string contentType)
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

            var dbResult = new ScanResult
            {
                Content = $"[{contentType?.ToUpper()}] {file.FileName}",
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
                contentType,
                riskScore,
                status,
                issues = issuesList,
                recommendation,
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