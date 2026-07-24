using System;
using System.Net.Http;
using Xunit;
using ContentRiskScanner.Controllers;
using ContentRiskScanner.Models;
using ContentRiskScanner.Services;
using ContentRiskScanner.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace ContentRiskScanner.Tests
{
    public class ScanControllerTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public async Task Scan_WithEmptyContent_ReturnsBadRequest()
        {
            // Arrange
            var dbContext = GetInMemoryDbContext();
            var config = new ConfigurationManager
            {
                ["WATSON_API_KEY"] = "dummy",
                ["WATSON_URL"] = "http://dummy",
                ["STT_API_KEY"] = "dummy",
                ["STT_URL"] = "http://dummy",
                ["IBM_CLOUD_APIKEY"] = "dummy",
                ["IBM_PROJECT_ID"] = "dummy"
            };
            var httpClient = new HttpClient();
            var service = new RiskEngineService(httpClient, config);
            var speechService = new SpeechToTextService(httpClient, config);
            var imageDetection = new ImageDetectionService(httpClient, config);
            var controller = new ScanController(service, speechService, imageDetection, dbContext);
            var request = new ScanRequest { Content = "" };

            // Act
            var result = await controller.Scan(request);

            // Assert
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Scan_WithValidContent_ReturnsOkAndSavesToDb()
        {
            // Arrange
            var dbContext = GetInMemoryDbContext();
            var config = new ConfigurationManager
            {
                ["WATSON_API_KEY"] = "dummy",
                ["WATSON_URL"] = "http://dummy",
                ["STT_API_KEY"] = "dummy",
                ["STT_URL"] = "http://dummy",
                ["IBM_CLOUD_APIKEY"] = "dummy",
                ["IBM_PROJECT_ID"] = "dummy"
            };
            var httpClient = new HttpClient();
            var service = new RiskEngineService(httpClient, config);
            var speechService = new SpeechToTextService(httpClient, config);
            var imageDetection = new ImageDetectionService(httpClient, config);
            var controller = new ScanController(service, speechService, imageDetection, dbContext);
            var request = new ScanRequest { Content = "Safe test content without any bad words" };

            // Act
            var result = await controller.Scan(request);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            
            // Verify DB save
            var savedScan = await dbContext.Scans.FirstOrDefaultAsync();
            Assert.NotNull(savedScan);
            Assert.Equal("Safe test content without any bad words", savedScan.Content);
        }
    }
}
