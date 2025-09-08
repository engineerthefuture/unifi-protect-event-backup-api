/************************
 * Unifi Webhook Event Receiver
 * SummaryEventQueueServiceTests.cs
 * 
 * Unit tests for SummaryEventQueueService class.
 * Tests SQS message sending functionality and error handling.
 * 
 * Author: GitHub Copilot
 * Created: 09-08-2025
 ***********************/

using Xunit;
using Moq;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.Lambda.Core;
using Amazon;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnifiWebhookEventReceiver.Services.Implementations;
using UnifiWebhookEventReceiver.Models;
using UnifiWebhookEventReceiver.Configuration;

namespace UnifiWebhookEventReceiver.Tests
{
    /// <summary>
    /// Tests for SummaryEventQueueService to ensure proper SQS integration and error handling.
    /// </summary>
    public class SummaryEventQueueServiceTests
    {
        private readonly Mock<AmazonSQSClient> _mockSqsClient;
        private readonly Mock<ILambdaLogger> _mockLogger;
        private readonly SummaryEventQueueService _service;

        public SummaryEventQueueServiceTests()
        {
            _mockSqsClient = new Mock<AmazonSQSClient>(Amazon.RegionEndpoint.USEast1);
            _mockLogger = new Mock<ILambdaLogger>();
            _service = new SummaryEventQueueService(_mockSqsClient.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task SendSummaryEventAsync_WithNullQueueUrl_ThrowsInvalidOperationException()
        {
            // Arrange
            var summaryEvent = new SummaryEvent
            {
                EventId = "test-event-123",
                AlarmName = "Test Alarm"
            };

            // Temporarily clear the environment variable
            var originalValue = Environment.GetEnvironmentVariable("SummaryEventQueueUrl");
            Environment.SetEnvironmentVariable("SummaryEventQueueUrl", null);

            try
            {
                // Act & Assert
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => _service.SendSummaryEventAsync(summaryEvent));
                
                Assert.Equal("SummaryEventQueueUrl environment variable is not configured", exception.Message);
                
                // Verify logger was called
                _mockLogger.Verify(x => x.LogLine("SummaryEventQueueUrl environment variable is not configured"), 
                    Times.Once);
            }
            finally
            {
                // Restore original value
                Environment.SetEnvironmentVariable("SummaryEventQueueUrl", originalValue);
            }
        }

        [Fact]
        public async Task SendSummaryEventAsync_WithEmptyQueueUrl_ThrowsInvalidOperationException()
        {
            // Arrange
            var summaryEvent = new SummaryEvent
            {
                EventId = "test-event-123",
                AlarmName = "Test Alarm"
            };

            // Temporarily set empty queue URL
            var originalValue = Environment.GetEnvironmentVariable("SummaryEventQueueUrl");
            Environment.SetEnvironmentVariable("SummaryEventQueueUrl", "");

            try
            {
                // Act & Assert
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => _service.SendSummaryEventAsync(summaryEvent));
                
                Assert.Equal("SummaryEventQueueUrl environment variable is not configured", exception.Message);
            }
            finally
            {
                // Restore original value
                Environment.SetEnvironmentVariable("SummaryEventQueueUrl", originalValue);
            }
        }

        [Fact]
        public async Task SendSummaryEventAsync_WithValidInput_SendsMessageSuccessfully()
        {
            // Arrange
            var summaryEvent = new SummaryEvent
            {
                EventId = "test-event-123",
                Device = "cam-001",
                Timestamp = 1642077600000,
                AlarmS3Key = "events/test-event-123.json",
                VideoS3Key = "videos/test-event-123.mp4",
                AlarmName = "Motion Detected",
                DeviceName = "Front Door Camera",
                EventType = "motion"
            };

            var expectedMessageId = "msg-12345";
            var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue";

            // Set up environment variable
            var originalValue = Environment.GetEnvironmentVariable("SummaryEventQueueUrl");
            Environment.SetEnvironmentVariable("SummaryEventQueueUrl", queueUrl);

            // Set up mock SQS response
            var sendMessageResponse = new SendMessageResponse
            {
                MessageId = expectedMessageId
            };

            _mockSqsClient.Setup(x => x.SendMessageAsync(
                It.Is<SendMessageRequest>(req => 
                    req.QueueUrl == queueUrl && 
                    req.MessageBody.Contains("test-event-123")),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(sendMessageResponse);

            try
            {
                // Act
                var result = await _service.SendSummaryEventAsync(summaryEvent);

                // Assert
                Assert.Equal(expectedMessageId, result);
                
                // Verify SQS client was called with correct parameters
                _mockSqsClient.Verify(x => x.SendMessageAsync(
                    It.Is<SendMessageRequest>(req => 
                        req.QueueUrl == queueUrl && 
                        req.MessageBody.Contains("test-event-123") &&
                        req.MessageBody.Contains("Motion Detected")),
                    It.IsAny<CancellationToken>()), 
                    Times.Once);

                // Verify success was logged
                _mockLogger.Verify(x => x.LogLine($"Successfully queued summary event. MessageId: {expectedMessageId}"), 
                    Times.Once);
            }
            finally
            {
                // Restore original value
                Environment.SetEnvironmentVariable("SummaryEventQueueUrl", originalValue);
            }
        }

        [Fact]
        public async Task SendSummaryEventAsync_WithSqsException_ThrowsException()
        {
            // Arrange
            var summaryEvent = new SummaryEvent
            {
                EventId = "test-event-123",
                AlarmName = "Test Alarm"
            };

            var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue";
            var originalValue = Environment.GetEnvironmentVariable("SummaryEventQueueUrl");
            Environment.SetEnvironmentVariable("SummaryEventQueueUrl", queueUrl);

            // Set up mock to throw exception
            var sqsException = new AmazonSQSException("Queue does not exist");
            _mockSqsClient.Setup(x => x.SendMessageAsync(
                It.IsAny<SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
                .ThrowsAsync(sqsException);

            try
            {
                // Act & Assert
                var exception = await Assert.ThrowsAsync<AmazonSQSException>(
                    () => _service.SendSummaryEventAsync(summaryEvent));
                
                Assert.Equal("Queue does not exist", exception.Message);
            }
            finally
            {
                // Restore original value
                Environment.SetEnvironmentVariable("SummaryEventQueueUrl", originalValue);
            }
        }

        [Fact]
        public async Task SendSummaryEventAsync_WithNullSummaryEvent_HandlesGracefully()
        {
            // Arrange
            var queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue";
            var originalValue = Environment.GetEnvironmentVariable("SummaryEventQueueUrl");
            Environment.SetEnvironmentVariable("SummaryEventQueueUrl", queueUrl);

            var sendMessageResponse = new SendMessageResponse
            {
                MessageId = "msg-null-test"
            };

            _mockSqsClient.Setup(x => x.SendMessageAsync(
                It.IsAny<SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(sendMessageResponse);

            try
            {
                // Act
                var result = await _service.SendSummaryEventAsync(null!);

                // Assert
                Assert.Equal("msg-null-test", result);
                
                // Verify SQS was called (JSON serialization of null should work)
                _mockSqsClient.Verify(x => x.SendMessageAsync(
                    It.IsAny<SendMessageRequest>(),
                    It.IsAny<CancellationToken>()), 
                    Times.Once);
            }
            finally
            {
                // Restore original value
                Environment.SetEnvironmentVariable("SummaryEventQueueUrl", originalValue);
            }
        }
    }
}
