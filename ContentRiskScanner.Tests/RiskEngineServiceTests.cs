using System.Net;
using System.Text.Json;
using ContentRiskScanner.Models;
using ContentRiskScanner.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using Xunit;

namespace ContentRiskScanner.Tests
{
    public class RiskEngineServiceTests
    {
        private RiskEngineService CreateService(string? mockWatsonResponse = null, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var configurationMock = new Mock<IConfiguration>();

            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            
            if (mockWatsonResponse != null)
            {
                var response = new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(mockWatsonResponse)
                };

                handlerMock.Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>()
                    )
                    .ReturnsAsync(response)
                    .Verifiable();
            }
            else
            {
                // Fallback for when we expect no Watson call, or we want it to fail nicely without data
                handlerMock.Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>()
                    )
                    .ThrowsAsync(new HttpRequestException("Network down"));
            }

            var httpClient = new HttpClient(handlerMock.Object);
            
            // Set dummy env variables for the test
            Environment.SetEnvironmentVariable("WATSON_API_KEY", "dummy");
            Environment.SetEnvironmentVariable("WATSON_URL", "http://dummy");

            return new RiskEngineService(httpClient, configurationMock.Object);
        }

        [Fact]
        public async Task AnalyzeAsync_WithSafeContent_ReturnsLowRisk()
        {
            var watsonResponse = JsonSerializer.Serialize(new { });
            var service = CreateService(watsonResponse);
            var request = new ScanRequest { Content = "This is a safe and friendly message about nothing in particular." };

            var response = await service.AnalyzeAsync(request);

            Assert.Equal(0, response.RiskScore);
            Assert.Equal("Low Risk", response.Status);
            Assert.Empty(response.Issues);
        }

        [Fact]
        public async Task AnalyzeAsync_WithKeywordViolation_IncreasesScore()
        {
            var watsonResponse = JsonSerializer.Serialize(new { });
            var service = CreateService(watsonResponse);
            var request = new ScanRequest { Content = "We guarantee a zero risk investment with a guaranteed return." };

            var response = await service.AnalyzeAsync(request);

            Assert.True(response.RiskScore >= 80); // "zero risk" + "guaranteed return"
            Assert.Contains(response.Issues, i => i.Contains("guaranteed returns"));
            Assert.Contains(response.Issues, i => i.Contains("misleading investment"));
            Assert.Equal("High Risk", response.Status);
        }

        [Fact]
        public async Task AnalyzeAsync_WatsonNegativeSentiment_IncreasesScore()
        {
            var watsonResponse = JsonSerializer.Serialize(new
            {
                sentiment = new
                {
                    document = new { label = "negative" }
                }
            });
            var service = CreateService(watsonResponse);
            
            // "safe" text from rule perspective, but watson says negative
            var request = new ScanRequest { Content = "I am sad." }; 

            var response = await service.AnalyzeAsync(request);

            Assert.Equal(15, response.RiskScore);
            Assert.Contains(response.Issues, i => i.Contains("Negative sentiment detected"));
        }

        [Fact]
        public async Task AnalyzeAsync_WatsonApiFails_HandlesGracefully()
        {
            var service = CreateService(null); // Will throw HttpRequestException
            var request = new ScanRequest { Content = "Safe content." };

            var response = await service.AnalyzeAsync(request);

            // Should not crash, just adds a skipped note
            Assert.Contains(response.Issues, i => i.Contains("Watson NLU check skipped"));
            Assert.Equal("Low Risk", response.Status);
        }
    }
}
