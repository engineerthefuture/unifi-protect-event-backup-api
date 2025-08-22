using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;
using Moq;
using Amazon;
using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace UnifiWebhookEventReceiverTests
{
    public class DlqIntegrationTests
    {
        [Fact]
        public async Task SqsService_SendAlarmToDlqAsync_WithValidAlarm_SendsMessageWithCorrectAttributes()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AlarmProcessingDlqUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-dlq");
            
            var mockSqsClient = new Mock<AmazonSQSClient>(Amazon.RegionEndpoint.USEast1);
            var mockAlarmProcessingService = new Mock<IAlarmProcessingService>();
            var mockEmailService = new Mock<IEmailService>();
            var mockResponseHelper = new Mock<IResponseHelper>();
            var mockLogger = new Mock<ILambdaLogger>();

            // Mock email service to return success
            mockEmailService
                .Setup(x => x.SendFailureNotificationAsync(It.IsAny<Alarm>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            mockEmailService
                .Setup(x => x.SendFailureNotificationAsync(It.IsAny<Alarm>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var alarm = new Alarm
            {
                timestamp = 1672531200000,
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        eventId = "test-event-id"
                    }
                }
            };

            // Mock the SendMessageAsync method to return a successful response
            var sendMessageResponse = new SendMessageResponse
            {
                MessageId = "test-message-id",
                HttpStatusCode = System.Net.HttpStatusCode.OK
            };

            mockSqsClient
                .Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(sendMessageResponse);

            var sqsService = new SqsService(
                mockSqsClient.Object,
                mockAlarmProcessingService.Object,
                mockEmailService.Object,
                mockResponseHelper.Object,
                mockLogger.Object);

            // Act
            var result = await sqsService.SendAlarmToDlqAsync(alarm, "NoVideoFilesDownloaded");

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            Assert.Equal("test-message-id", result);

            // Verify that SendMessageAsync was called on the SQS client
            mockSqsClient.Verify(
                x => x.SendMessageAsync(It.Is<SendMessageRequest>(req =>
                    req.QueueUrl == "https://sqs.us-east-1.amazonaws.com/123456789012/test-dlq" &&
                    req.MessageAttributes.ContainsKey("FailureReason") &&
                    req.MessageAttributes["FailureReason"].StringValue == "NoVideoFilesDownloaded" &&
                    req.MessageAttributes.ContainsKey("OriginalTimestamp") &&
                    req.MessageAttributes["OriginalTimestamp"].StringValue == "1672531200000" &&
                    req.MessageAttributes.ContainsKey("RetryAttempt")
                ), It.IsAny<System.Threading.CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task SqsService_SendAlarmToDlqAsync_WithMissingDlqUrl_ThrowsInvalidOperationException()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AlarmProcessingDlqUrl", null);
            
            var mockSqsClient = new Mock<AmazonSQSClient>(Amazon.RegionEndpoint.USEast1);
            var mockAlarmProcessingService = new Mock<IAlarmProcessingService>();
            var mockEmailService = new Mock<IEmailService>();
            var mockResponseHelper = new Mock<IResponseHelper>();
            var mockLogger = new Mock<ILambdaLogger>();

            var alarm = new Alarm
            {
                timestamp = 1672531200000,
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        eventId = "test-event-id"
                    }
                }
            };

            var sqsService = new SqsService(
                mockSqsClient.Object,
                mockAlarmProcessingService.Object,
                mockEmailService.Object,
                mockResponseHelper.Object,
                mockLogger.Object);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sqsService.SendAlarmToDlqAsync(alarm, "TestReason"));

            Assert.Equal("DLQ URL not configured", exception.Message);
        }
    }
}
