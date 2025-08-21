/************************
 * UnifiWebhookEventHandler Integration Tests
 * UnifiWebhookEventHandlerAdditionalTests.cs
 * Additional integration testing for edge cases and error scenarios
 * Brent Foster
 * 08-20-2025
 ***********************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Moq;
using Newtonsoft.Json;
using Xunit;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services;

namespace UnifiWebhookEventReceiverTests
{
    public class UnifiWebhookEventHandlerAdditionalTests
    {
        private readonly Mock<IRequestRouter> _mockRequestRouter;
        private readonly Mock<ISqsService> _mockSqsService;
        private readonly Mock<IResponseHelper> _mockResponseHelper;
        private readonly Mock<ILambdaLogger> _mockLogger;
        private readonly Mock<ILambdaContext> _mockContext;
        private readonly UnifiWebhookEventHandler _handler;

        public UnifiWebhookEventHandlerAdditionalTests()
        {
            _mockRequestRouter = new Mock<IRequestRouter>();
            _mockSqsService = new Mock<ISqsService>();
            _mockResponseHelper = new Mock<IResponseHelper>();
            _mockLogger = new Mock<ILambdaLogger>();
            _mockContext = new Mock<ILambdaContext>();

            // Setup basic environment variables
            Environment.SetEnvironmentVariable("FunctionName", "TestFunction");
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");

            // Setup mock context
            _mockContext.Setup(x => x.Logger).Returns(_mockLogger.Object);
            _mockContext.Setup(x => x.FunctionName).Returns("TestFunction");
            _mockContext.Setup(x => x.AwsRequestId).Returns("test-request-id");

            // Setup default response helper behaviors
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(It.IsAny<HttpStatusCode>(), It.IsAny<string>()))
                .Returns(new APIGatewayProxyResponse 
                { 
                    StatusCode = 500, 
                    Body = "{\"error\":\"test error\"}",
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                });

            _mockResponseHelper.Setup(x => x.CreateSuccessResponse(It.IsAny<object>()))
                .Returns(new APIGatewayProxyResponse 
                { 
                    StatusCode = 200, 
                    Body = "{\"success\":true}",
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                });

            _handler = new UnifiWebhookEventHandler(_mockRequestRouter.Object, _mockSqsService.Object, _mockResponseHelper.Object, _mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullRequestRouter_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new UnifiWebhookEventHandler(null, _mockSqsService.Object, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullSqsService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new UnifiWebhookEventHandler(_mockRequestRouter.Object, null, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullResponseHelper_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new UnifiWebhookEventHandler(_mockRequestRouter.Object, _mockSqsService.Object, null, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new UnifiWebhookEventHandler(_mockRequestRouter.Object, _mockSqsService.Object, _mockResponseHelper.Object, null));
        }

        [Fact]
        public void DefaultConstructor_CreateInstanceSuccessfully()
        {
            // This test verifies the default constructor can create services
            // We need to set environment variables for it to work
            Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", "arn:aws:secretsmanager:us-east-1:123456789012:secret:test");
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue");
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", "120");

            // Act & Assert - Should not throw
            var handler = new UnifiWebhookEventHandler();
            Assert.NotNull(handler);
        }

        [Fact]
        public async Task FunctionHandler_WithInvalidJson_ReturnsErrorResponse()
        {
            // Arrange
            var invalidJson = "invalid json content";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalidJson));

            // Act
            var result = await _handler.FunctionHandler(stream, _mockContext.Object);

            // Assert
            Assert.Equal(500, result.StatusCode);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, It.IsAny<string>()), Times.Once);
        }

    }
}
