using System;
using System.Threading.Tasks;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;
using Moq;
using Amazon;
using Amazon.Lambda.Core;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;

namespace UnifiWebhookEventReceiverTests
{
    public class EmailServiceTests
    {
        [Fact]
        public async Task SendFailureNotificationAsync_WithMissingSupportEmail_ReturnsFalse()
        {
            // Arrange
            Environment.SetEnvironmentVariable("SupportEmail", null);
            
            var mockSesClient = new Mock<AmazonSimpleEmailServiceClient>(Amazon.RegionEndpoint.USEast1);
            var mockCloudWatchLogsClient = new Mock<AmazonCloudWatchLogsClient>(Amazon.RegionEndpoint.USEast1);
            var mockLogger = new Mock<ILambdaLogger>();
            var mockS3Service = new Mock<IS3StorageService>();

            var emailService = new EmailService(mockSesClient.Object, mockCloudWatchLogsClient.Object, mockLogger.Object, mockS3Service.Object);
            
            var alarm = new Alarm
            {
                timestamp = 1672531200000,
                triggers = new System.Collections.Generic.List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        eventId = "test-event-id"
                    }
                }
            };

            // Act
            var result = await emailService.SendFailureNotificationAsync(alarm, "NoVideoFilesDownloaded", "msg-123", "2025-08-22T12:00:00Z");

            // Assert
            Assert.False(result);
            
            // Verify SES client was never called
            mockSesClient.Verify(x => x.SendRawEmailAsync(It.IsAny<SendRawEmailRequest>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SendFailureNotificationAsync_WithValidParameters_CallsSesAndReturnsTrue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("SupportEmail", "support@example.com");
            
            var mockSesClient = new Mock<AmazonSimpleEmailServiceClient>(Amazon.RegionEndpoint.USEast1);
            var mockCloudWatchLogsClient = new Mock<AmazonCloudWatchLogsClient>(Amazon.RegionEndpoint.USEast1);
            var mockLogger = new Mock<ILambdaLogger>();
            var mockS3Service = new Mock<IS3StorageService>();

            var sendRawEmailResponse = new SendRawEmailResponse
            {
                MessageId = "ses-message-id",
                HttpStatusCode = System.Net.HttpStatusCode.OK
            };

            // Mock the CloudWatch Logs and S3 service calls
            mockS3Service.Setup(x => x.GetFileAsync(It.IsAny<string>())).ReturnsAsync((byte[])null);

            mockSesClient
                .Setup(x => x.SendRawEmailAsync(It.IsAny<SendRawEmailRequest>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(sendRawEmailResponse);

            var emailService = new EmailService(mockSesClient.Object, mockCloudWatchLogsClient.Object, mockLogger.Object, mockS3Service.Object);
            
            var alarm = new Alarm
            {
                timestamp = 1672531200000,
                triggers = new System.Collections.Generic.List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        eventId = "test-event-id"
                    }
                }
            };

            // Act
            var result = await emailService.SendFailureNotificationAsync(alarm, "NoVideoFilesDownloaded", "msg-123", "2025-08-22T12:00:00Z");

            // Assert
            Assert.True(result);
            
            // Verify SES client was called with raw email
            mockSesClient.Verify(x => x.SendRawEmailAsync(It.Is<SendRawEmailRequest>(req =>
                req.Source == "support@example.com" &&
                req.Destinations.Contains("support@example.com")
            ), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SendFailureNotificationAsync_WithThumbnailAvailable_IncludesThumbnailInEmail()
        {
            // Arrange
            Environment.SetEnvironmentVariable("SupportEmail", "support@example.com");
            
            var mockSesClient = new Mock<AmazonSimpleEmailServiceClient>(Amazon.RegionEndpoint.USEast1);
            var mockCloudWatchLogsClient = new Mock<AmazonCloudWatchLogsClient>(Amazon.RegionEndpoint.USEast1);
            var mockLogger = new Mock<ILambdaLogger>();
            var mockS3Service = new Mock<IS3StorageService>();

            var sendRawEmailResponse = new SendRawEmailResponse
            {
                MessageId = "ses-message-id",
                HttpStatusCode = System.Net.HttpStatusCode.OK
            };

            // Mock thumbnail data
            var thumbnailData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG header bytes
            
            // Set up S3 service to return thumbnail data for the specific thumbnail key pattern
            mockS3Service.Setup(x => x.GetFileAsync(It.Is<string>(key => key.Contains("test-event-id") && key.EndsWith(".jpg"))))
                .ReturnsAsync(thumbnailData);
            
            // Return null for screenshot attempts
            mockS3Service.Setup(x => x.GetFileAsync(It.Is<string>(key => key.Contains("screenshot"))))
                .ReturnsAsync((byte[])null);

            mockSesClient
                .Setup(x => x.SendRawEmailAsync(It.IsAny<SendRawEmailRequest>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(sendRawEmailResponse);

            var emailService = new EmailService(mockSesClient.Object, mockCloudWatchLogsClient.Object, mockLogger.Object, mockS3Service.Object);
            
            var alarm = new Alarm
            {
                timestamp = 1672531200000, // 2023-01-01 00:00:00 UTC
                triggers = new System.Collections.Generic.List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        eventId = "test-event-id"
                    }
                }
            };

            // Act
            var result = await emailService.SendFailureNotificationAsync(alarm, "NoVideoFilesDownloaded", "msg-123", "2025-08-22T12:00:00Z");

            // Assert
            Assert.True(result);
            
            // Verify S3 service was called to retrieve the thumbnail with the correct key pattern
            mockS3Service.Verify(x => x.GetFileAsync(It.Is<string>(key => 
                key.Contains("2022-12-31") && // Date folder (local time for timestamp)
                key.Contains("test-event-id") && 
                key.Contains("AA:BB:CC:DD:EE:FF") &&
                key.Contains("1672531200000") &&
                key.EndsWith(".jpg")
            )), Times.Once);
            
            // Verify SES client was called
            mockSesClient.Verify(x => x.SendRawEmailAsync(It.IsAny<SendRawEmailRequest>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }
    }
}
