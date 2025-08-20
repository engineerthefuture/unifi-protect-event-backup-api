using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Moq;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    public class RequestRouterTests
    {
        private readonly Mock<ISqsService> _mockSqsService;
        private readonly Mock<IS3StorageService> _mockS3StorageService;
        private readonly Mock<IResponseHelper> _mockResponseHelper;
        private readonly Mock<ILambdaLogger> _mockLogger;
        private readonly RequestRouter _requestRouter;

        public RequestRouterTests()
        {
            _mockSqsService = new Mock<ISqsService>();
            _mockS3StorageService = new Mock<IS3StorageService>();
            _mockResponseHelper = new Mock<IResponseHelper>();
            _mockLogger = new Mock<ILambdaLogger>();
            
            // Setup default return values for SQS service
            _mockSqsService.Setup(x => x.QueueAlarmForProcessingAsync(It.IsAny<Alarm>()))
                .ReturnsAsync(new APIGatewayProxyResponse
                {
                    StatusCode = 202,
                    Body = "Alarm queued for processing"
                });
            
            // Setup default return values for response helper
            _mockResponseHelper.Setup(x => x.GetStandardHeaders())
                .Returns(new Dictionary<string, string>
                {
                    ["Access-Control-Allow-Origin"] = "*",
                    ["Access-Control-Allow-Headers"] = "Content-Type,X-Amz-Date,Authorization,X-Api-Key",
                    ["Content-Type"] = "application/json"
                });
            
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(It.IsAny<HttpStatusCode>(), It.IsAny<string>()))
                .Returns((HttpStatusCode statusCode, string message) => new APIGatewayProxyResponse
                {
                    StatusCode = (int)statusCode,
                    Body = JsonConvert.SerializeObject(new { error = message }),
                    Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" }
                });
            
            _requestRouter = new RequestRouter(_mockSqsService.Object, _mockS3StorageService.Object, _mockResponseHelper.Object, _mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullSqsService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new RequestRouter(null, _mockS3StorageService.Object, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullS3StorageService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new RequestRouter(_mockSqsService.Object, null, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullResponseHelper_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new RequestRouter(_mockSqsService.Object, _mockS3StorageService.Object, null, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new RequestRouter(_mockSqsService.Object, _mockS3StorageService.Object, _mockResponseHelper.Object, null));
        }

        [Fact]
        public async Task RouteRequestAsync_WithOptionsMethod_ReturnsOptionsResponse()
        {
            // Arrange
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "OPTIONS",
                Path = "/alarmevent"
            };

            var headers = new Dictionary<string, string>
            {
                { "Access-Control-Allow-Origin", "*" },
                { "Access-Control-Allow-Methods", "GET,POST,OPTIONS" }
            };

            var optionsResponse = new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Headers = headers
            };

            _mockResponseHelper.Setup(x => x.GetStandardHeaders())
                .Returns(headers);

            // Act
            var result = await _requestRouter.RouteRequestAsync(request);

            // Assert
            Assert.Equal(200, result.StatusCode);
            Assert.True(result.Headers.ContainsKey("Access-Control-Allow-Origin"));
            _mockResponseHelper.Verify(x => x.GetStandardHeaders(), Times.Once);
        }

        [Fact]
        public async Task RouteRequestAsync_WithPostMethodAndValidBody_SendsToSqs()
        {
            // Arrange
            var alarmJson = "{\"timestamp\":1234567890,\"alarm\":{\"triggers\":[{\"key\":\"test\",\"device\":\"camera1\",\"eventId\":\"event123\"}]}}";
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = "/alarmevent",
                Body = alarmJson
            };

            var successResponse = new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Body = "Success"
            };

            _mockResponseHelper.Setup(x => x.CreateSuccessResponse(It.IsAny<object>()))
                .Returns(successResponse);

            // Act
            var result = await _requestRouter.RouteRequestAsync(request);

            // Assert
            Assert.Equal(202, result.StatusCode);
            _mockSqsService.Verify(x => x.QueueAlarmForProcessingAsync(It.IsAny<Alarm>()), Times.Once);
        }

        [Fact]
        public async Task RouteRequestAsync_WithInvalidJson_ReturnsErrorResponse()
        {
            // Arrange
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = "/alarmevent",
                Body = "invalid json"
            };

            var errorResponse = new APIGatewayProxyResponse
            {
                StatusCode = 400,
                Body = "Invalid JSON"
            };

            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()))
                .Returns(errorResponse);

            // Act
            var result = await _requestRouter.RouteRequestAsync(request);

            // Assert
            Assert.Equal(400, result.StatusCode);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RouteRequestAsync_WithUnsupportedMethod_ReturnsMethodNotAllowed()
        {
            // Arrange
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "DELETE",
                Path = "/alarmevent"
            };

            var errorResponse = new APIGatewayProxyResponse
            {
                StatusCode = 405,
                Body = "Method not allowed"
            };

            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.MethodNotAllowed, It.IsAny<string>()))
                .Returns(errorResponse);

            // Act
            var result = await _requestRouter.RouteRequestAsync(request);

            // Assert
            Assert.Equal(405, result.StatusCode);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.MethodNotAllowed, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RouteRequestAsync_WithNullRequest_ReturnsErrorResponse()
        {
            // Arrange
            var errorResponse = new APIGatewayProxyResponse
            {
                StatusCode = 400,
                Body = "Invalid request"
            };

            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()))
                .Returns(errorResponse);

            // Act
            var result = await _requestRouter.RouteRequestAsync(null);

            // Assert
            Assert.Equal(400, result.StatusCode);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RouteRequestAsync_WithEmptyBody_ReturnsErrorResponse()
        {
            // Arrange
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = "/alarmevent",
                Body = ""
            };

            var errorResponse = new APIGatewayProxyResponse
            {
                StatusCode = 400,
                Body = "Empty body"
            };

            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()))
                .Returns(errorResponse);

            // Act
            var result = await _requestRouter.RouteRequestAsync(request);

            // Assert
            Assert.Equal(400, result.StatusCode);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task RouteRequestAsync_WithSqsException_ReturnsErrorResponse()
        {
            // Arrange
            var alarmJson = "{\"timestamp\":1234567890,\"alarm\":{\"triggers\":[{\"key\":\"test\",\"device\":\"camera1\",\"eventId\":\"event123\"}]}}";
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = "/alarmevent",
                Body = alarmJson
            };

            _mockSqsService.Setup(x => x.QueueAlarmForProcessingAsync(It.IsAny<Alarm>()))
                .ThrowsAsync(new Exception("SQS failed"));

            var errorResponse = new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = "Internal server error"
            };

            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, It.IsAny<string>()))
                .Returns(errorResponse);

            // Act
            var result = await _requestRouter.RouteRequestAsync(request);

            // Assert
            Assert.Equal(500, result.StatusCode);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, It.IsAny<string>()), Times.Once);
        }

        [Theory]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("HEAD")]
        [InlineData("DELETE")]
        public async Task RouteRequestAsync_WithUnsupportedMethods_ReturnsMethodNotAllowed(string method)
        {
            // Arrange
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = method,
                Path = "/alarmevent"
            };

            var errorResponse = new APIGatewayProxyResponse
            {
                StatusCode = 405,
                Body = "Method not allowed"
            };

            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.MethodNotAllowed, It.IsAny<string>()))
                .Returns(errorResponse);

            // Act
            var result = await _requestRouter.RouteRequestAsync(request);

            // Assert
            Assert.Equal(405, result.StatusCode);
        }

        [Fact]
        public async Task RouteRequestAsync_WithGetMethodMissingParameters_ReturnsBadRequest()
        {
            // Arrange
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "GET",
                Path = "/alarmevent"
            };

            var errorResponse = new APIGatewayProxyResponse
            {
                StatusCode = 400,
                Body = "Missing required parameter"
            };

            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()))
                .Returns(errorResponse);

            // Act
            var result = await _requestRouter.RouteRequestAsync(request);

            // Assert
            Assert.Equal(400, result.StatusCode);
        }
    }
}
