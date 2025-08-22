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
            var mockLogger = new Mock<ILambdaLogger>();
            var mockS3Service = new Mock<IS3StorageService>();

            var emailService = new EmailService(mockSesClient.Object, mockLogger.Object, mockS3Service.Object);
            
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
            mockSesClient.Verify(x => x.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task SendFailureNotificationAsync_WithValidParameters_CallsSesAndReturnsTrue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("SupportEmail", "support@example.com");
            
            var mockSesClient = new Mock<AmazonSimpleEmailServiceClient>(Amazon.RegionEndpoint.USEast1);
            var mockLogger = new Mock<ILambdaLogger>();
            var mockS3Service = new Mock<IS3StorageService>();

            var sendEmailResponse = new SendEmailResponse
            {
                MessageId = "ses-message-id",
                HttpStatusCode = System.Net.HttpStatusCode.OK
            };

            mockSesClient
                .Setup(x => x.SendEmailAsync(It.IsAny<SendEmailRequest>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(sendEmailResponse);

            var emailService = new EmailService(mockSesClient.Object, mockLogger.Object, mockS3Service.Object);
            
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
            
            // Verify SES client was called
            mockSesClient.Verify(x => x.SendEmailAsync(It.Is<SendEmailRequest>(req =>
                req.Source == "support@example.com" &&
                req.Destination.ToAddresses.Contains("support@example.com") &&
                req.Message.Subject.Data.Contains("Unifi Protect Video Download Failure") &&
                req.Message.Body.Html.Data.Contains("NoVideoFilesDownloaded")
            ), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }
    }
}
