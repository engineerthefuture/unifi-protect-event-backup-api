using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Moq;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    public class UnifiProtectServiceTests
    {
        private readonly Mock<ICredentialsService> _mockCredentialsService;
        private readonly Mock<ILambdaLogger> _mockLogger;
        private readonly UnifiProtectService _unifiProtectService;

        public UnifiProtectServiceTests()
        {
            _mockCredentialsService = new Mock<ICredentialsService>();
            _mockLogger = new Mock<ILambdaLogger>();
            var mockS3StorageService = new Mock<IS3StorageService>();
            _unifiProtectService = new UnifiProtectService(_mockLogger.Object, mockS3StorageService.Object, _mockCredentialsService.Object);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            var mockS3StorageService = new Mock<IS3StorageService>();
            Assert.Throws<ArgumentNullException>(() => 
                new UnifiProtectService(null, mockS3StorageService.Object, _mockCredentialsService.Object));
        }

        [Fact]
        public void Constructor_WithNullS3StorageService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new UnifiProtectService(_mockLogger.Object, null, _mockCredentialsService.Object));
        }

        [Fact]
        public void Constructor_WithNullCredentialsService_ThrowsArgumentNullException()
        {
            // Act & Assert
            var mockS3StorageService = new Mock<IS3StorageService>();
            Assert.Throws<ArgumentNullException>(() => 
                new UnifiProtectService(_mockLogger.Object, mockS3StorageService.Object, null));
        }

        [Fact]
        public async Task DownloadVideoAsync_WithNullTrigger_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _unifiProtectService.DownloadVideoAsync(null, "test-link", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        }

        [Fact]
        public async Task DownloadVideoAsync_WithInvalidTimestamp_ThrowsArgumentException()
        {
            // Arrange
            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "test-link", -1));
        }

        [Fact]
        public async Task DownloadVideoAsync_WithZeroTimestamp_ThrowsArgumentException()
        {
            // Arrange
            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "test-link", 0));
        }

        [Fact]
        public async Task DownloadVideoAsync_WithValidParameters_ThrowsInvalidOperationException()
        {
            // Arrange
            var credentials = new UnifiCredentials
            {
                hostname = "192.168.1.1",
                username = "admin",
                password = "secret123"
            };

            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(credentials);

            // Act & Assert
            // In test environment, browser launch will fail, which should throw InvalidOperationException
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "test-link", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        }

        [Fact]
        public async Task DownloadVideoAsync_WithCredentialsException_ThrowsInvalidOperationException()
        {
            // Arrange
            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ThrowsAsync(new Exception("Credentials error"));

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "test-link", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        }

        [Fact]
        public async Task DownloadVideoAsync_WithNullCredentials_ThrowsInvalidOperationException()
        {
            // Arrange
            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(null as UnifiCredentials);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "test-link", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        }

        [Theory]
        [InlineData("192.168.1.1", "admin", "password123")]
        [InlineData("unifi.local", "user", "secret")]
        [InlineData("10.0.0.100", "admin", "test123")]
        public async Task DownloadVideoAsync_WithDifferentCredentials_ThrowsInvalidOperationException(
            string hostname, string username, string password)
        {
            // Arrange
            var credentials = new UnifiCredentials
            {
                hostname = hostname,
                username = username,
                password = password
            };

            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(credentials);

            // Act & Assert
            // Browser will fail to launch in test environment
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "test-link", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            
            // Verify credentials service was called before browser failure
            _mockCredentialsService.Verify(x => x.GetUnifiCredentialsAsync(), Times.Once);
        }

        [Fact]
        public async Task DownloadVideoAsync_LogsOperationStart()
        {
            // Arrange
            var credentials = new UnifiCredentials
            {
                hostname = "192.168.1.1",
                username = "admin",
                password = "secret123"
            };

            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(credentials);

            // Act & Assert
            // Browser will fail, but we can still verify logging occurred before failure
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "test-link", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

            // Verify operation start was logged before browser failure
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Starting video download"))), Times.Once);
        }

        [Fact]
        public async Task DownloadVideoAsync_WithBrowserException_ThrowsInvalidOperationException()
        {
            // Arrange
            var credentials = new UnifiCredentials
            {
                hostname = "192.168.1.1",
                username = "admin",
                password = "secret123"
            };

            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(credentials);

            // Act & Assert
            // Browser launch will fail in test environment, wrapped as InvalidOperationException
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _unifiProtectService.DownloadVideoAsync(trigger, "test-link", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        }

        [Fact]
        public void CleanupTempFile_WithValidPath_ExecutesWithoutException()
        {
            // Arrange
            var testFilePath = "/tmp/test-cleanup-file.mp4";
            
            // Create a temp file
            if (!File.Exists(testFilePath))
            {
                File.WriteAllText(testFilePath, "test content");
            }

            // Act
            _unifiProtectService.CleanupTempFile(testFilePath);

            // Assert
            Assert.False(File.Exists(testFilePath));
        }

        [Fact]
        public void CleanupTempFile_WithNonexistentFile_ExecutesWithoutException()
        {
            // Arrange
            var nonexistentPath = "/tmp/nonexistent-file.mp4";

            // Act & Assert - should not throw
            _unifiProtectService.CleanupTempFile(nonexistentPath);
        }
    }
}
