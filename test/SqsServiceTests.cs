/************************
 * SqsService Tests
 * SqsServiceTests.cs
 * Testing SQS message processing and queue operations
 * Brent Foster
 * 08-20-2025
 ***********************/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Moq;
using Newtonsoft.Json;
using System.Net;
using Xunit;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;

namespace UnifiWebhookEventReceiverTests
{
    public class SqsServiceTests
    {
        private readonly Mock<AmazonSQSClient> _mockSqsClient;
        private readonly Mock<IAlarmProcessingService> _mockAlarmProcessingService;
        private readonly Mock<IEmailService> _mockEmailService;
        private readonly Mock<IResponseHelper> _mockResponseHelper;
        private readonly Mock<ILambdaLogger> _mockLogger;
        private readonly SqsService _sqsService;

        public SqsServiceTests()
        {
            _mockSqsClient = new Mock<AmazonSQSClient>(Amazon.RegionEndpoint.USEast1);
            _mockAlarmProcessingService = new Mock<IAlarmProcessingService>();
            _mockEmailService = new Mock<IEmailService>();
            _mockResponseHelper = new Mock<IResponseHelper>();
            _mockLogger = new Mock<ILambdaLogger>();

            // Set required environment variables for testing
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue");
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", "120");

            // Setup email service mock to return success by default
            _mockEmailService.Setup(x => x.SendFailureNotificationAsync(It.IsAny<Alarm>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            // Setup default response helper behaviors
            _mockResponseHelper.Setup(x => x.GetStandardHeaders())
                .Returns(new Dictionary<string, string> { { "Content-Type", "application/json" } });

            _mockResponseHelper.Setup(x => x.CreateSuccessResponse(It.IsAny<object>()))
                .Returns(new APIGatewayProxyResponse 
                { 
                    StatusCode = 200, 
                    Body = "{\"success\":true}",
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                });

            _mockResponseHelper.Setup(x => x.CreateErrorResponse(It.IsAny<HttpStatusCode>(), It.IsAny<string>()))
                .Returns(new APIGatewayProxyResponse 
                { 
                    StatusCode = 500, 
                    Body = "{\"error\":\"test error\"}",
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                });

            _sqsService = new SqsService(_mockSqsClient.Object, _mockAlarmProcessingService.Object, _mockEmailService.Object, _mockResponseHelper.Object, _mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullSqsClient_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new SqsService(null, _mockAlarmProcessingService.Object, _mockEmailService.Object, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullAlarmProcessingService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new SqsService(_mockSqsClient.Object, null, _mockEmailService.Object, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullEmailService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new SqsService(_mockSqsClient.Object, _mockAlarmProcessingService.Object, null, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullResponseHelper_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new SqsService(_mockSqsClient.Object, _mockAlarmProcessingService.Object, _mockEmailService.Object, null, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new SqsService(_mockSqsClient.Object, _mockAlarmProcessingService.Object, _mockEmailService.Object, _mockResponseHelper.Object, null));
        }

        [Fact]
        public void IsSqsEvent_WithValidSqsEvent_ReturnsTrue()
        {
            // Arrange
            var sqsEventJson = @"{
                ""Records"": [
                    {
                        ""messageId"": ""test-message-id"",
                        ""body"": ""test-body"",
                        ""eventSource"":""aws:sqs""
                    }
                ]
            }";

            // Act
            var result = _sqsService.IsSqsEvent(sqsEventJson);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsSqsEvent_WithNullRequestBody_ReturnsFalse()
        {
            // Act
            var result = _sqsService.IsSqsEvent(null);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsSqsEvent_WithEmptyRequestBody_ReturnsFalse()
        {
            // Act
            var result = _sqsService.IsSqsEvent("");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsSqsEvent_WithoutRecords_ReturnsFalse()
        {
            // Arrange
            var nonSqsEventJson = @"{""test"": ""data""}";

            // Act
            var result = _sqsService.IsSqsEvent(nonSqsEventJson);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsSqsEvent_WithoutEventSource_ReturnsFalse()
        {
            // Arrange
            var invalidSqsEventJson = @"{""Records"": [{}]}";

            // Act
            var result = _sqsService.IsSqsEvent(invalidSqsEventJson);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsSqsEvent_WithWrongEventSource_ReturnsFalse()
        {
            // Arrange
            var nonSqsEventJson = @"{
                ""Records"": [
                    {
                        ""eventSource"": ""aws:s3""
                    }
                ]
            }";

            // Act
            var result = _sqsService.IsSqsEvent(nonSqsEventJson);

            // Assert
            Assert.False(result);
        }

        [Fact]
    public async Task ProcessSqsEventAsync_WithValidEvent_ProcessesAlarm()
        {
            // Arrange
            var alarm = new Alarm
            {
                name = "Motion Detection",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                triggers = new List<Trigger>
                {
                    new Trigger { key = "motion", eventId = "test-event-id", device = "test-device" }
                }
            };

            var sqsEvent = new SQSEvent
            {
                Records = new List<SQSEvent.SQSMessage>
                {
                    new SQSEvent.SQSMessage
                    {
                        MessageId = "test-message-id",
                        Body = JsonConvert.SerializeObject(alarm)
                    }
                }
            };

            var sqsEventJson = JsonConvert.SerializeObject(sqsEvent);

            // Act
            await _sqsService.ProcessSqsEventAsync(sqsEventJson);

            // Assert
            _mockAlarmProcessingService.Verify(x => x.ProcessAlarmForSqsAsync(It.IsAny<Alarm>()), Times.Once);
        }

        [Fact]
    public async Task ProcessSqsEventAsync_WithException_Throws()
        {
            // Arrange
            var invalidJson = "invalid json";
            
            // Act
            await Assert.ThrowsAsync<Newtonsoft.Json.JsonReaderException>(() => _sqsService.ProcessSqsEventAsync(invalidJson));
        }

        [Fact]
    public async Task ProcessSqsEventAsync_WithEmptyRecords_ProcessesNoAlarms()
        {
            // Arrange
            var sqsEvent = new SQSEvent { Records = new List<SQSEvent.SQSMessage>() };
            var sqsEventJson = JsonConvert.SerializeObject(sqsEvent);

            // Act
            await _sqsService.ProcessSqsEventAsync(sqsEventJson);

            // Assert
            _mockAlarmProcessingService.Verify(x => x.ProcessAlarmForSqsAsync(It.IsAny<Alarm>()), Times.Never);
        }

        [Fact]
    public async Task ProcessSqsEventAsync_WithNullRecords_ProcessesNoAlarms()
        {
            // Arrange
            var sqsEvent = new SQSEvent { Records = null };
            var sqsEventJson = JsonConvert.SerializeObject(sqsEvent);

            // Act
            await _sqsService.ProcessSqsEventAsync(sqsEventJson);

            // Assert
            _mockAlarmProcessingService.Verify(x => x.ProcessAlarmForSqsAsync(It.IsAny<Alarm>()), Times.Never);
        }

        [Fact]
    public async Task ProcessSqsEventAsync_WithProcessingException_ContinuesProcessing()
        {
            // Arrange
            var alarm1 = new Alarm { 
                name = "Alarm1", 
                timestamp = 1, 
                triggers = new List<Trigger>() 
            };
            var alarm2 = new Alarm { 
                name = "Alarm2", 
                timestamp = 2, 
                triggers = new List<Trigger>() 
            };

            var sqsEvent = new SQSEvent
            {
                Records = new List<SQSEvent.SQSMessage>
                {
                    new SQSEvent.SQSMessage
                    {
                        MessageId = "message-1",
                        Body = JsonConvert.SerializeObject(alarm1)
                    },
                    new SQSEvent.SQSMessage
                    {
                        MessageId = "message-2", 
                        Body = JsonConvert.SerializeObject(alarm2)
                    }
                }
            };

            // Setup first alarm to throw exception, second should still process
            _mockAlarmProcessingService.SetupSequence(x => x.ProcessAlarmForSqsAsync(It.IsAny<Alarm>()))
                .ThrowsAsync(new Exception("Test exception"))
                .Returns(Task.CompletedTask);

            var sqsEventJson = JsonConvert.SerializeObject(sqsEvent);

            // Act
            await _sqsService.ProcessSqsEventAsync(sqsEventJson);

            // Assert
            _mockAlarmProcessingService.Verify(x => x.ProcessAlarmForSqsAsync(It.IsAny<Alarm>()), Times.Exactly(2));
        }

        [Fact]
        public async Task QueueAlarmForProcessingAsync_WithValidAlarm_ReturnsSuccess()
        {
            // Arrange - ensure environment variables are set
            var originalQueueUrl = Environment.GetEnvironmentVariable("AlarmProcessingQueueUrl");
            var originalProcessingDelay = Environment.GetEnvironmentVariable("ProcessingDelaySeconds");
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue");
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", "120");
            
            try
            {
                var alarm = new Alarm
                {
                    name = "Motion Detection",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    triggers = new List<Trigger>
                    {
                        new Trigger { key = "motion", eventId = "test-event-id", device = "test-device" }
                    }
                };

                _mockSqsClient.Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), default))
                    .ReturnsAsync(new SendMessageResponse { MessageId = "test-message-id" });

                // Act
                var result = await _sqsService.QueueAlarmForProcessingAsync(alarm);

                // Assert
                Assert.Equal(200, result.StatusCode);
                _mockResponseHelper.Verify(x => x.CreateSuccessResponse(It.IsAny<object>()), Times.Once);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", originalQueueUrl);
                Environment.SetEnvironmentVariable("ProcessingDelaySeconds", originalProcessingDelay);
            }
        }

        [Fact]
        public async Task QueueAlarmForProcessingAsync_WithMissingQueueUrl_ReturnsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", "");
            var alarm = new Alarm { 
                name = "Test", 
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 
                triggers = new List<Trigger>() 
            };

            // Act
            var result = await _sqsService.QueueAlarmForProcessingAsync(alarm);

            // Assert
            Assert.Equal(500, result.StatusCode);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, It.IsAny<string>()), Times.Once);

            // Cleanup
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue");
        }

        [Fact]
        public async Task QueueAlarmForProcessingAsync_WithSqsException_ReturnsError()
        {
            // Arrange
            var alarm = new Alarm { 
                name = "Test", 
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 
                triggers = new List<Trigger>() 
            };
            _mockSqsClient.Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), default))
                .ThrowsAsync(new Exception("SQS error"));

            // Act
            var result = await _sqsService.QueueAlarmForProcessingAsync(alarm);

            // Assert
            Assert.Equal(500, result.StatusCode);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, It.IsAny<string>()), Times.Once);
        }

        [Fact]
    public async Task ProcessSqsEventAsync_WithInvalidAlarmJson_LogsErrorAndContinues()
        {
            // Arrange
            var sqsEvent = new SQSEvent
            {
                Records = new List<SQSEvent.SQSMessage>
                {
                    new SQSEvent.SQSMessage
                    {
                        MessageId = "test-message-id",
                        Body = "invalid alarm json"
                    }
                }
            };

            var sqsEventJson = JsonConvert.SerializeObject(sqsEvent);

            // Act
            await _sqsService.ProcessSqsEventAsync(sqsEventJson);

            // Assert
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Error processing SQS message"))), Times.Once);
        }

        [Fact]
        public void IsSqsEvent_LogsRequestBodyLength()
        {
            // Arrange
            var testJson = @"{""test"": ""data""}";

            // Act
            _sqsService.IsSqsEvent(testJson);

            // Assert
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Request body length:"))), Times.Once);
        }

        [Fact]
        public void IsSqsEvent_LogsRequestBodySnippet()
        {
            // Arrange
            var testJson = @"{""test"": ""data""}";

            // Act
            _sqsService.IsSqsEvent(testJson);

            // Assert
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Request body snippet:"))), Times.Once);
        }

        [Fact]
        public void IsSqsEvent_TruncatesLongRequestBody()
        {
            // Arrange
            var longJson = new string('x', 1000);

            // Act
            _sqsService.IsSqsEvent(longJson);

            // Assert
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("...") && s.Contains("Request body snippet:"))), Times.Once);
        }

        [Fact]
        public async Task GetDlqMessageCountAsync_WithNullDlqUrl_ReturnsZero()
        {
            // Arrange
            var originalValue = Environment.GetEnvironmentVariable("AlarmProcessingDlqUrl");
            Environment.SetEnvironmentVariable("AlarmProcessingDlqUrl", null);

            try
            {
                // Act
                var result = await _sqsService.GetDlqMessageCountAsync();

                // Assert
                Assert.Equal(0, result);
                _mockLogger.Verify(x => x.LogLine("DLQ URL is not configured."), Times.Once);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AlarmProcessingDlqUrl", originalValue);
            }
        }

        [Fact]
        public async Task GetDlqMessageCountAsync_WithEmptyDlqUrl_ReturnsZero()
        {
            // Arrange
            var originalValue = Environment.GetEnvironmentVariable("AlarmProcessingDlqUrl");
            Environment.SetEnvironmentVariable("AlarmProcessingDlqUrl", "");

            try
            {
                // Act
                var result = await _sqsService.GetDlqMessageCountAsync();

                // Assert
                Assert.Equal(0, result);
                _mockLogger.Verify(x => x.LogLine("DLQ URL is not configured."), Times.Once);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AlarmProcessingDlqUrl", originalValue);
            }
        }
    }
}
