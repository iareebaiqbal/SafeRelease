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

        public ScanController(RiskEngineService riskEngine)
        {
            _riskEngine = riskEngine;
        }

        [HttpPost("scan")]
        public async Task<IActionResult> Scan([FromBody] ScanRequest request)
        {
            if (string.IsNullOrEmpty(request.Content))
                return BadRequest("Content is required");

            var response = await _riskEngine.AnalyzeAsync(request);
            return Ok(response);
        }

        [HttpGet("report/{id}")]
        public IActionResult Report(int id)
        {
            return Ok("Report coming soon");
        }
    }
}