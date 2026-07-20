using Xunit;
using ContentRiskScanner.Controllers;
using ContentRiskScanner.Models;
using ContentRiskScanner.Services;
using ContentRiskScanner.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace ContentRiskScanner.Tests
{
    public class ScanControllerTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().String())
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public async Task Scan_WithEmptyContent_ReturnsBadRequest()
        {
            // Arrange
            var dbContext = GetInMemoryDbContext();
            var service = new RiskEngineService();
            var controller = new ScanController(service, dbContext);
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
            var service = new RiskEngineService();
            var controller = new ScanController(service, dbContext);
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
