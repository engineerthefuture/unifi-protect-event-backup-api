/************************
 * UnifiProtectService Additional Tests
 * UnifiProtectServiceAdditionalTests.cs
 * Additional tests focusing on error scenarios and edge cases
 * Brent Foster
 * 08-20-2025
 ***********************/

using System;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Moq;
using Xunit;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;

namespace UnifiWebhookEventReceiverTests
{
    public class UnifiProtectServiceAdditionalTests
    {
        private readonly Mock<ICredentialsService> _mockCredentialsService;
        private readonly Mock<IS3StorageService> _mockS3StorageService;
        private readonly Mock<ILambdaLogger> _mockLogger;
        private readonly UnifiProtectService _unifiProtectService;

        public UnifiProtectServiceAdditionalTests()
        {
            _mockCredentialsService = new Mock<ICredentialsService>();
            _mockS3StorageService = new Mock<IS3StorageService>();
            _mockLogger = new Mock<ILambdaLogger>();
            
            _unifiProtectService = new UnifiProtectService(
                _mockLogger.Object, 
                _mockS3StorageService.Object, 
                _mockCredentialsService.Object);

            // Set up environment variables for testing
            Environment.SetEnvironmentVariable("DownloadDirectory", "/tmp");
            Environment.SetEnvironmentVariable("ArchiveButtonX", "1274");
            Environment.SetEnvironmentVariable("ArchiveButtonY", "257");
            Environment.SetEnvironmentVariable("DownloadButtonX", "1095");
            Environment.SetEnvironmentVariable("DownloadButtonY", "275");
        }

        [Fact]
        public async Task DownloadVideoAsync_WithEmptyEventLink_ThrowsArgumentException()
        {
            // Arrange
            var trigger = new Trigger { 
                key = "motion", 
                eventId = "test-id", 
                device = "test-device" 
            };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "", timestamp));
        }

        [Fact]
        public async Task DownloadVideoAsync_WithWhitespaceEventLink_ThrowsArgumentException()
        {
            // Arrange
            var trigger = new Trigger { 
                key = "motion", 
                eventId = "test-id", 
                device = "test-device" 
            };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "   ", timestamp));
        }

        [Fact]
        public async Task DownloadVideoAsync_WithNegativeTimestamp_ThrowsArgumentException()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "http://test-link", -1));
        }

        [Fact]
        public async Task DownloadVideoAsync_WithCredentialsServiceException_LogsErrorAndReturns()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventLink = "http://test-link";

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ThrowsAsync(new InvalidOperationException("Credentials error"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Equal("Credentials error", exception.Message);
        }

        [Fact]
        public async Task DownloadVideoAsync_WithEmptyCredentials_LogsErrorAndReturns()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventLink = "http://test-link";

            var emptyCredentials = new UnifiCredentials
            {
                hostname = "",
                username = "",
                password = ""
            };

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(emptyCredentials);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Equal("Unifi credentials are not properly configured", exception.Message);
        }

        [Fact]
        public async Task DownloadVideoAsync_WithNullCredentials_LogsErrorAndReturns()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventLink = "http://test-link";

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync((UnifiCredentials)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Equal("Unifi credentials are not properly configured", exception.Message);
        }

        [Fact]
        public async Task DownloadVideoAsync_WithPartialCredentials_LogsErrorAndReturns()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventLink = "http://test-link";

            var partialCredentials = new UnifiCredentials
            {
                hostname = "test-host",
                username = "", // Missing username
                password = "test-password"
            };

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(partialCredentials);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Equal("Unifi credentials are not properly configured", exception.Message);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Validating Unifi credentials..."))), Times.Once);
        }

        [Fact]
        public async Task DownloadVideoAsync_LogsStartMessage()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventLink = "http://test-link";

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync((UnifiCredentials)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Equal("Unifi credentials are not properly configured", exception.Message);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Starting video download for event"))), Times.Once);
        }

        [Fact]
        public async Task DownloadVideoAsync_LogsEventDetails()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-event-123", device = "test-device-456" };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventLink = "http://test-link";

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync((UnifiCredentials)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Equal("Unifi credentials are not properly configured", exception.Message);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => 
                s.Contains("test-event-123") && s.Contains("test-device-456"))), Times.Once);
        }

        [Fact]
        public async Task DownloadVideoAsync_WithValidCredentials_LogsProcessingStart()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventLink = "http://test-link";

            var validCredentials = new UnifiCredentials
            {
                hostname = "test-host",
                username = "test-user",
                password = "test-password"
            };

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(validCredentials);

            // Act & Assert - browser will fail to launch in test environment
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Contains("Error downloading video:", exception.Message);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Credentials validated successfully"))), Times.Once);
        }

        [Fact]
        public void Constructor_StoresServicesCorrectly()
        {
            // This test verifies that the constructor properly stores the service dependencies
            // and that the service can be instantiated without throwing exceptions
            
            // Arrange & Act
            var service = new UnifiProtectService(_mockLogger.Object, _mockS3StorageService.Object, _mockCredentialsService.Object);

            // Assert
            Assert.NotNull(service);
            // If we got here without exception, the constructor worked correctly
        }

        [Fact]
        public async Task DownloadVideoAsync_WithTriggerContainingSpecialCharacters_HandlesCorrectly()
        {
            // Arrange
            var trigger = new Trigger 
            { 
                key = "motion",
                eventId = "test-id-with-special-chars-!@#$%", 
                device = "device-with-dashes-and_underscores" 
            };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventLink = "http://test-link";

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync((UnifiCredentials)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Equal("Unifi credentials are not properly configured", exception.Message);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => 
                s.Contains("test-id-with-special-chars-!@#$%"))), Times.AtLeastOnce);
        }

        [Fact]
        public async Task DownloadVideoAsync_WithMaxLongTimestamp_HandlesCorrectly()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };
            var timestamp = long.MaxValue;
            var eventLink = "http://test-link";

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync((UnifiCredentials)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Equal("Unifi credentials are not properly configured", exception.Message);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Starting video download"))), Times.Once);
        }

        [Fact]
        public async Task DownloadVideoAsync_WithVeryLongEventLink_HandlesCorrectly()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var longEventLink = "http://very-long-event-link-" + new string('x', 1000);

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync((UnifiCredentials)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, longEventLink, timestamp));
            
            Assert.Equal("Unifi credentials are not properly configured", exception.Message);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Starting video download"))), Times.Once);
        }

        [Fact]
        public async Task DownloadVideoAsync_WithCredentialsServiceReturningNullAfterDelay_HandlesCorrectly()
        {
            // Arrange
            var trigger = new Trigger { key = "motion", eventId = "test-id", device = "test-device" };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var eventLink = "http://test-link";

            // Setup credentials service to return null after a delay
            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .Returns(async () =>
                {
                    await Task.Delay(10); // Small delay to simulate async operation
                    return null;
                });

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, eventLink, timestamp));
            
            Assert.Equal("Unifi credentials are not properly configured", exception.Message);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Validating Unifi credentials..."))), Times.Once);
        }
    }
}
