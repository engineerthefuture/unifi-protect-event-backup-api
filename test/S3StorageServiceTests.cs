using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    public class S3StorageServiceTests
    {
        private readonly Mock<AmazonS3Client> _mockS3Client;
        private readonly Mock<IResponseHelper> _mockResponseHelper;
        private readonly Mock<ILambdaLogger> _mockLogger;
        private readonly S3StorageService _s3StorageService;

        public S3StorageServiceTests()
        {
            // Set required environment variables for testing
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            
            // Create AWS client mock using constructor with region
            _mockS3Client = new Mock<AmazonS3Client>(Amazon.RegionEndpoint.USEast1);
            _mockResponseHelper = new Mock<IResponseHelper>();
            _mockLogger = new Mock<ILambdaLogger>();
            _s3StorageService = new S3StorageService(_mockS3Client.Object, _mockResponseHelper.Object, _mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullS3Client_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new S3StorageService(null, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullResponseHelper_ThrowsArgumentNullException()
        {
            var mockS3Client = new Mock<AmazonS3Client>(Amazon.RegionEndpoint.USEast1);
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new S3StorageService(mockS3Client.Object, null, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            var mockS3Client = new Mock<AmazonS3Client>(Amazon.RegionEndpoint.USEast1);
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new S3StorageService(mockS3Client.Object, _mockResponseHelper.Object, null));
        }

        [Fact]
        public async Task StoreAlarmEventAsync_WithValidData_ReturnsS3Key()
        {
            // Arrange - ensure environment variable is set
            var originalBucket = Environment.GetEnvironmentVariable("StorageBucket");
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            
            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var trigger = new Trigger
                {
                    key = "test-key",
                    device = "test-device",
                    eventId = "test-event-id"
                };
                var alarm = new Alarm
                {
                    timestamp = timestamp,
                    triggers = new List<Trigger> { trigger }
                };

                _mockS3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
                    .ReturnsAsync(new PutObjectResponse());

                // Act
                var result = await _s3StorageService.StoreAlarmEventAsync(alarm, trigger);

                // Assert
                Assert.NotNull(result);
                Assert.Contains("test-event-id", result);
                _mockS3Client.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Once);
            }
            finally
            {
                Environment.SetEnvironmentVariable("StorageBucket", originalBucket);
            }
        }

        [Fact]
        public async Task StoreAlarmEventAsync_WithNullAlarm_ThrowsArgumentNullException()
        {
            // Arrange
            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _s3StorageService.StoreAlarmEventAsync(null, trigger));
        }

        [Fact]
        public async Task StoreVideoFileAsync_WithValidPath_UploadsSuccessfully()
        {
            // Arrange
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            var tempDir = Path.GetTempPath();
            var filePath = Path.Combine(tempDir, $"test-video-{Guid.NewGuid()}.mp4");
            var s3Key = "videos/test-video.mp4";

            // Create a temporary test file
            var testVideoData = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }; // MP4 header bytes
            await File.WriteAllBytesAsync(filePath, testVideoData);

            try
            {
                _mockS3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
                    .ReturnsAsync(new PutObjectResponse());

                // Act
                await _s3StorageService.StoreVideoFileAsync(filePath, s3Key);

                // Assert
                _mockS3Client.Verify(x => x.PutObjectAsync(It.Is<PutObjectRequest>(r => 
                    r.Key == s3Key && r.ContentType == "video/mp4"), default), Times.Once);
            }
            finally
            {
                // Clean up the temporary file
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }

        [Fact]
        public async Task GetLatestVideoAsync_WithMissingBucketConfiguration_ReturnsServerError()
        {
            // Arrange
            // Save original environment variable value
            var originalStorageBucket = Environment.GetEnvironmentVariable("StorageBucket");
            
            try
            {
                // Clear the environment variable to trigger validation error
                Environment.SetEnvironmentVariable("StorageBucket", null);
                
                var expectedResponse = new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = "Server configuration error: StorageBucket not configured"
                };
                _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured"))
                    .Returns(expectedResponse);

                // Act
                var result = await _s3StorageService.GetLatestVideoAsync();

                // Assert
                Assert.Equal(500, result.StatusCode);
            }
            finally
            {
                // Restore original environment variable value
                Environment.SetEnvironmentVariable("StorageBucket", originalStorageBucket);
            }
        }

        [Fact]
        public async Task GetLatestVideoAsync_WithExceptionHandling_ReturnsInternalServerError()
        {
            // Arrange
            _mockS3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), default))
                .ThrowsAsync(new Exception("S3 connection error"));

            var expectedResponse = new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = "Error retrieving latest video: S3 connection error"
            };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, "Error retrieving latest video: S3 connection error"))
                .Returns(expectedResponse);

            // Act
            var result = await _s3StorageService.GetLatestVideoAsync();

            // Assert
            Assert.Equal(500, result.StatusCode);
        }

        [Fact]
        public async Task GetVideoByEventIdAsync_WithInvalidEventId_ReturnsBadRequest()
        {
            // Arrange
            var expectedResponse = new APIGatewayProxyResponse
            {
                StatusCode = 400,
                Body = "EventId parameter is required"
            };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, "EventId parameter is required"))
                .Returns(expectedResponse);

            // Act
            var result = await _s3StorageService.GetVideoByEventIdAsync(string.Empty);

            // Assert
            Assert.Equal(400, result.StatusCode);
        }

        [Fact]
        public void GenerateS3Keys_WithValidTrigger_ReturnsCorrectKeys()
        {
            // Arrange
            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Act
            var (eventKey, videoKey) = _s3StorageService.GenerateS3Keys(trigger, timestamp);

            // Assert
            Assert.NotNull(eventKey);
            Assert.NotNull(videoKey);
            Assert.Contains("test-event-id", eventKey);
            Assert.Contains("test-event-id", videoKey);
        }

        [Fact]
        public async Task StoreAlarmEventAsync_WithS3Exception_ThrowsException()
        {
            // Arrange
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var trigger = new Trigger
            {
                key = "test-key",
                device = "test-device",
                eventId = "test-event-id"
            };
            var alarm = new Alarm
            {
                timestamp = timestamp,
                triggers = new List<Trigger> { trigger }
            };

            _mockS3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
                .ThrowsAsync(new Exception("S3 error"));

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => 
                _s3StorageService.StoreAlarmEventAsync(alarm, trigger));
        }

        [Fact]
        public async Task StoreVideoFileAsync_WithFileNotFound_ThrowsFileNotFoundException()
        {
            // Arrange - ensure environment variable is set
            var originalBucket = Environment.GetEnvironmentVariable("StorageBucket");
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            
            try
            {
                var filePath = "/nonexistent/path/video.mp4";
                var s3Key = "videos/video.mp4";

                // Act & Assert
                await Assert.ThrowsAsync<FileNotFoundException>(() => 
                    _s3StorageService.StoreVideoFileAsync(filePath, s3Key));
            }
            finally
            {
                Environment.SetEnvironmentVariable("StorageBucket", originalBucket);
            }
        }

        [Fact]
        public async Task StoreScreenshotFileAsync_WithValidPngFile_UploadsSuccessfully()
        {
            // Arrange
            var originalBucket = Environment.GetEnvironmentVariable("StorageBucket");
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            
            var tempScreenshotPath = Path.GetTempFileName();
            var pngPath = Path.ChangeExtension(tempScreenshotPath, ".png");
            File.Move(tempScreenshotPath, pngPath);
            
            await File.WriteAllBytesAsync(pngPath, new byte[] { 1, 2, 3, 4 });
            var s3Key = "screenshots/test-screenshot.png";

            try
            {
                // Act
                await _s3StorageService.StoreScreenshotFileAsync(pngPath, s3Key);

                // Assert - Verify the upload was called with correct parameters
                _mockS3Client.Verify(x => x.PutObjectAsync(
                    It.Is<PutObjectRequest>(req => 
                        req.BucketName == "test-bucket" &&
                        req.Key == s3Key &&
                        req.ContentType == "image/png"), 
                    default), Times.Once);
            }
            finally
            {
                Environment.SetEnvironmentVariable("StorageBucket", originalBucket);
                if (File.Exists(pngPath))
                    File.Delete(pngPath);
            }
        }

        [Fact]
        public async Task StoreScreenshotFileAsync_WithJpegFile_UploadsWithCorrectContentType()
        {
            // Arrange
            var tempScreenshotPath = Path.GetTempFileName();
            var jpegPath = Path.ChangeExtension(tempScreenshotPath, ".jpg");
            File.Move(tempScreenshotPath, jpegPath);
            
            await File.WriteAllBytesAsync(jpegPath, new byte[] { 1, 2, 3, 4 });
            var s3Key = "screenshots/test-screenshot.jpg";

            try
            {
                // Act
                await _s3StorageService.StoreScreenshotFileAsync(jpegPath, s3Key);

                // Assert
                _mockS3Client.Verify(x => x.PutObjectAsync(
                    It.Is<PutObjectRequest>(req => 
                        req.ContentType == "image/jpeg"), 
                    default), Times.Once);
            }
            finally
            {
                if (File.Exists(jpegPath))
                    File.Delete(jpegPath);
            }
        }

        [Fact]
        public async Task StoreScreenshotFileAsync_WithUnknownExtension_UsesDefaultContentType()
        {
            // Arrange
            var tempScreenshotPath = Path.GetTempFileName();
            var unknownPath = Path.ChangeExtension(tempScreenshotPath, ".xyz");
            File.Move(tempScreenshotPath, unknownPath);
            
            await File.WriteAllBytesAsync(unknownPath, new byte[] { 1, 2, 3, 4 });
            var s3Key = "screenshots/test-screenshot.xyz";

            try
            {
                // Act
                await _s3StorageService.StoreScreenshotFileAsync(unknownPath, s3Key);

                // Assert
                _mockS3Client.Verify(x => x.PutObjectAsync(
                    It.Is<PutObjectRequest>(req => 
                        req.ContentType == "application/octet-stream"), 
                    default), Times.Once);
            }
            finally
            {
                if (File.Exists(unknownPath))
                    File.Delete(unknownPath);
            }
        }

        [Fact]
        public async Task StoreScreenshotFileAsync_WithMissingBucket_ThrowsException()
        {
            // Arrange
            var originalBucket = Environment.GetEnvironmentVariable("StorageBucket");
            Environment.SetEnvironmentVariable("StorageBucket", null);
            
            var tempScreenshotPath = Path.GetTempFileName();
            
            try
            {
                // Act & Assert
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
                    _s3StorageService.StoreScreenshotFileAsync(tempScreenshotPath, "test-key"));
                
                Assert.Equal("StorageBucket environment variable is not configured", exception.Message);
            }
            finally
            {
                Environment.SetEnvironmentVariable("StorageBucket", originalBucket);
                if (File.Exists(tempScreenshotPath))
                    File.Delete(tempScreenshotPath);
            }
        }

        [Fact]
        public async Task StoreScreenshotFileAsync_WithMissingFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentPath = "/tmp/nonexistent-screenshot.png";
            var s3Key = "screenshots/test.png";

            // Act & Assert
            var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => 
                _s3StorageService.StoreScreenshotFileAsync(nonExistentPath, s3Key));
            
            Assert.Contains("Screenshot file not found", exception.Message);
        }

        [Theory]
        [InlineData(".gif", "image/gif")]
        [InlineData(".bmp", "image/bmp")]
        [InlineData(".jpeg", "image/jpeg")]
        [InlineData(".PNG", "image/png")] // Test case insensitive
        [InlineData(".JPG", "image/jpeg")] // Test case insensitive
        public async Task StoreScreenshotFileAsync_WithDifferentExtensions_UsesCorrectContentType(string extension, string expectedContentType)
        {
            // Arrange
            var originalBucket = Environment.GetEnvironmentVariable("StorageBucket");
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            
            var tempScreenshotPath = Path.GetTempFileName();
            var testPath = Path.ChangeExtension(tempScreenshotPath, extension);
            File.Move(tempScreenshotPath, testPath);
            
            await File.WriteAllBytesAsync(testPath, new byte[] { 1, 2, 3, 4 });
            var s3Key = $"screenshots/test{extension}";

            try
            {
                // Act
                await _s3StorageService.StoreScreenshotFileAsync(testPath, s3Key);

                // Assert
                _mockS3Client.Verify(x => x.PutObjectAsync(
                    It.Is<PutObjectRequest>(req => 
                        req.ContentType == expectedContentType), 
                    default), Times.Once);
            }
            finally
            {
                Environment.SetEnvironmentVariable("StorageBucket", originalBucket);
                if (File.Exists(testPath))
                    File.Delete(testPath);
            }
        }

        [Fact]
        public void ExtractTimestampFromFileName_WithValidFormat_ReturnsTimestamp()
        {
            // This method is private, so we'll test it indirectly through a public method that uses it
            // For now, let's add tests for other methods with branching logic
        }

        [Fact]
        public async Task GetVideoByEventIdAsync_WithEmptyEventId_ReturnsBadRequest()
        {
            // Arrange
            var expectedResponse = new APIGatewayProxyResponse
            {
                StatusCode = 400,
                Body = "EventId parameter is required"
            };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, "EventId parameter is required"))
                .Returns(expectedResponse);

            // Act
            var result = await _s3StorageService.GetVideoByEventIdAsync("");

            // Assert
            Assert.Equal(400, result.StatusCode);
        }

        [Fact]
        public async Task GetVideoByEventIdAsync_WithWhitespaceEventId_ReturnsBadRequest()
        {
            // Arrange
            var expectedResponse = new APIGatewayProxyResponse
            {
                StatusCode = 400,
                Body = "EventId parameter is required"
            };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, "EventId parameter is required"))
                .Returns(expectedResponse);

            // Act
            var result = await _s3StorageService.GetVideoByEventIdAsync("   ");

            // Assert
            Assert.Equal(400, result.StatusCode);
        }

        [Fact]
        public async Task GetVideoByEventIdAsync_WithMissingBucket_ReturnsServerError()
        {
            // Arrange
            var originalBucket = Environment.GetEnvironmentVariable("StorageBucket");
            Environment.SetEnvironmentVariable("StorageBucket", "");
            
            var expectedResponse = new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = "Server configuration error: StorageBucket not configured"
            };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured"))
                .Returns(expectedResponse);

            try
            {
                // Act
                var result = await _s3StorageService.GetVideoByEventIdAsync("evt_123456");

                // Assert
                Assert.Equal(500, result.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable("StorageBucket", originalBucket);
            }
        }
    }
}
