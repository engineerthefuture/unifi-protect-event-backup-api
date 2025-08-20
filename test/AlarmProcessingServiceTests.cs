/************************
 * Unifi Webhook Event Receiver
 * AlarmProcessingServiceTests.cs
 * 
 * Comprehensive unit tests for AlarmProcessingService.
 * Tests alarm validation, processing, S3 storage, and video download orchestration.
 * 
 * Author: Brent Foster
 * Created: 08-20-2025
 ***********************/

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Moq;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    public class AlarmProcessingServiceTests
    {
        private readonly Mock<IS3StorageService> _mockS3StorageService;
        private readonly Mock<IUnifiProtectService> _mockUnifiProtectService;
        private readonly Mock<ICredentialsService> _mockCredentialsService;
        private readonly Mock<IResponseHelper> _mockResponseHelper;
        private readonly Mock<ILambdaLogger> _mockLogger;
        private readonly AlarmProcessingService _alarmProcessingService;

        public AlarmProcessingServiceTests()
        {
            _mockS3StorageService = new Mock<IS3StorageService>();
            _mockUnifiProtectService = new Mock<IUnifiProtectService>();
            _mockCredentialsService = new Mock<ICredentialsService>();
            _mockResponseHelper = new Mock<IResponseHelper>();
            _mockLogger = new Mock<ILambdaLogger>();

            _alarmProcessingService = new AlarmProcessingService(
                _mockS3StorageService.Object,
                _mockUnifiProtectService.Object,
                _mockCredentialsService.Object,
                _mockResponseHelper.Object,
                _mockLogger.Object);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullS3StorageService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AlarmProcessingService(null, _mockUnifiProtectService.Object, _mockCredentialsService.Object, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullUnifiProtectService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AlarmProcessingService(_mockS3StorageService.Object, null, _mockCredentialsService.Object, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullCredentialsService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AlarmProcessingService(_mockS3StorageService.Object, _mockUnifiProtectService.Object, null, _mockResponseHelper.Object, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullResponseHelper_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AlarmProcessingService(_mockS3StorageService.Object, _mockUnifiProtectService.Object, _mockCredentialsService.Object, null, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AlarmProcessingService(_mockS3StorageService.Object, _mockUnifiProtectService.Object, _mockCredentialsService.Object, _mockResponseHelper.Object, null));
        }

        [Fact]
        public void Constructor_WithValidParameters_CreatesInstance()
        {
            // Act
            var service = new AlarmProcessingService(_mockS3StorageService.Object, _mockUnifiProtectService.Object, _mockCredentialsService.Object, _mockResponseHelper.Object, _mockLogger.Object);

            // Assert
            Assert.NotNull(service);
        }

        #endregion

        #region ProcessAlarmAsync Tests

        [Fact]
        public async Task ProcessAlarmAsync_WithNullAlarm_ReturnsBadRequest()
        {
            // Arrange
            var expectedResponse = new APIGatewayProxyResponse { StatusCode = 400 };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()))
                .Returns(expectedResponse);

            // Act
            var result = await _alarmProcessingService.ProcessAlarmAsync(null);

            // Assert
            Assert.Equal(expectedResponse, result);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Error:"))), Times.Once);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAlarmAsync_WithNullTriggers_ReturnsBadRequest()
        {
            // Arrange
            var alarm = new Alarm
            {
                name = "Test Alarm",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                triggers = null
            };

            var expectedResponse = new APIGatewayProxyResponse { StatusCode = 400 };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()))
                .Returns(expectedResponse);

            // Act
            var result = await _alarmProcessingService.ProcessAlarmAsync(alarm);

            // Assert
            Assert.Equal(expectedResponse, result);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Error:"))), Times.Once);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAlarmAsync_WithEmptyTriggers_ReturnsBadRequest()
        {
            // Arrange
            var alarm = new Alarm
            {
                name = "Test Alarm",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                triggers = new List<Trigger>()
            };

            var expectedResponse = new APIGatewayProxyResponse { StatusCode = 400 };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()))
                .Returns(expectedResponse);

            // Act
            var result = await _alarmProcessingService.ProcessAlarmAsync(alarm);

            // Assert
            Assert.Equal(expectedResponse, result);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Error:"))), Times.Once);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.BadRequest, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAlarmAsync_WithCredentialsException_ReturnsInternalServerError()
        {
            // Arrange
            SetValidAlarmBucketEnvironment();
            var alarm = CreateValidAlarm();

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ThrowsAsync(new InvalidOperationException("Credentials error"));

            var expectedResponse = new APIGatewayProxyResponse { StatusCode = 500 };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, It.IsAny<string>()))
                .Returns(expectedResponse);

            // Act
            var result = await _alarmProcessingService.ProcessAlarmAsync(alarm);

            // Assert
            Assert.Equal(expectedResponse, result);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ProcessAlarmAsync_WithMissingStorageBucket_ReturnsInternalServerError()
        {
            // Arrange
            // Clear bucket environment variable
            Environment.SetEnvironmentVariable("StorageBucket", null);

            var alarm = CreateValidAlarm();
            var credentials = CreateValidCredentials();

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(credentials);

            var expectedResponse = new APIGatewayProxyResponse { StatusCode = 500 };
            _mockResponseHelper.Setup(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured"))
                .Returns(expectedResponse);

            // Act
            var result = await _alarmProcessingService.ProcessAlarmAsync(alarm);

            // Assert
            Assert.Equal(expectedResponse, result);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("StorageBucket environment variable is not configured"))), Times.Once);
            _mockResponseHelper.Verify(x => x.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured"), Times.Once);
        }

        [Fact]
        public async Task ProcessAlarmAsync_WithValidAlarmWithoutEventPath_ReturnsSuccessWithoutVideoDownload()
        {
            // Arrange
            SetValidAlarmBucketEnvironment();
            var alarm = CreateValidAlarm();
            alarm.eventPath = null; // No event path

            var credentials = CreateValidCredentials();
            var eventKey = "alarm-events/2025/01/01/alarm-evt_123.json";
            var trigger = alarm.triggers[0];

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(credentials);

            _mockS3StorageService.Setup(x => x.GenerateS3Keys(It.IsAny<Trigger>(), It.IsAny<long>()))
                .Returns((eventKey, "videos/2025/01/01/video-evt_123.mp4"));

            _mockS3StorageService.Setup(x => x.StoreAlarmEventAsync(It.IsAny<Alarm>(), It.IsAny<Trigger>()))
                .ReturnsAsync(eventKey);

            var expectedResponse = new APIGatewayProxyResponse { StatusCode = 200 };
            _mockResponseHelper.Setup(x => x.CreateSuccessResponse(It.IsAny<Trigger>(), It.IsAny<long>()))
                .Returns(expectedResponse);

            // Act
            var result = await _alarmProcessingService.ProcessAlarmAsync(alarm);

            // Assert
            Assert.Equal(expectedResponse, result);
            _mockS3StorageService.Verify(x => x.StoreAlarmEventAsync(alarm, It.IsAny<Trigger>()), Times.Once);
            _mockUnifiProtectService.Verify(x => x.DownloadVideoAsync(It.IsAny<Trigger>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("No event path provided, skipping video download"))), Times.Once);
        }

        [Fact]
        public async Task ProcessAlarmAsync_WithValidAlarmWithEventPath_ReturnsSuccessWithVideoDownload()
        {
            // Arrange
            SetValidAlarmBucketEnvironment();
            var alarm = CreateValidAlarm();
            alarm.eventPath = "/protect/api/events/test-event-123/video";

            var credentials = CreateValidCredentials();
            var eventKey = "alarm-events/2025/01/01/alarm-evt_123.json";
            var videoKey = "videos/2025/01/01/video-evt_123.mp4";
            var tempVideoPath = "/tmp/test-video.mp4";

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(credentials);

            _mockS3StorageService.Setup(x => x.GenerateS3Keys(It.IsAny<Trigger>(), It.IsAny<long>()))
                .Returns((eventKey, videoKey));

            _mockS3StorageService.Setup(x => x.StoreAlarmEventAsync(It.IsAny<Alarm>(), It.IsAny<Trigger>()))
                .ReturnsAsync(eventKey);

            _mockUnifiProtectService.Setup(x => x.DownloadVideoAsync(It.IsAny<Trigger>(), It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(tempVideoPath);

            var expectedResponse = new APIGatewayProxyResponse { StatusCode = 200 };
            _mockResponseHelper.Setup(x => x.CreateSuccessResponse(It.IsAny<Trigger>(), It.IsAny<long>()))
                .Returns(expectedResponse);

            // Act
            var result = await _alarmProcessingService.ProcessAlarmAsync(alarm);

            // Assert
            Assert.Equal(expectedResponse, result);
            _mockS3StorageService.Verify(x => x.StoreAlarmEventAsync(alarm, It.IsAny<Trigger>()), Times.Once);
            _mockUnifiProtectService.Verify(x => x.DownloadVideoAsync(It.IsAny<Trigger>(), It.IsAny<string>(), It.IsAny<long>()), Times.Once);
            _mockS3StorageService.Verify(x => x.StoreVideoFileAsync(tempVideoPath, videoKey), Times.Once);
            _mockUnifiProtectService.Verify(x => x.CleanupTempFile(tempVideoPath), Times.Once);
        }

        [Fact]
        public async Task ProcessAlarmAsync_WithVideoDownloadException_ContinuesWithoutFailingAlarmProcessing()
        {
            // Arrange
            SetValidAlarmBucketEnvironment();
            var alarm = CreateValidAlarm();
            alarm.eventPath = "/protect/api/events/test-event-123/video";

            var credentials = CreateValidCredentials();
            var eventKey = "alarm-events/2025/01/01/alarm-evt_123.json";

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(credentials);

            _mockS3StorageService.Setup(x => x.GenerateS3Keys(It.IsAny<Trigger>(), It.IsAny<long>()))
                .Returns((eventKey, "videos/2025/01/01/video-evt_123.mp4"));

            _mockS3StorageService.Setup(x => x.StoreAlarmEventAsync(It.IsAny<Alarm>(), It.IsAny<Trigger>()))
                .ReturnsAsync(eventKey);

            _mockUnifiProtectService.Setup(x => x.DownloadVideoAsync(It.IsAny<Trigger>(), It.IsAny<string>(), It.IsAny<long>()))
                .ThrowsAsync(new InvalidOperationException("Video download failed"));

            var expectedResponse = new APIGatewayProxyResponse { StatusCode = 200 };
            _mockResponseHelper.Setup(x => x.CreateSuccessResponse(It.IsAny<Trigger>(), It.IsAny<long>()))
                .Returns(expectedResponse);

            // Act
            var result = await _alarmProcessingService.ProcessAlarmAsync(alarm);

            // Assert
            Assert.Equal(expectedResponse, result);
            _mockS3StorageService.Verify(x => x.StoreAlarmEventAsync(alarm, It.IsAny<Trigger>()), Times.Once);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("Error downloading or storing video"))), Times.Once);
        }

        [Fact]
        public async Task ProcessAlarmAsync_WithS3UploadVideoException_LogsErrorButContinues()
        {
            // Arrange
            SetValidAlarmBucketEnvironment();
            var alarm = CreateValidAlarm();
            alarm.eventPath = "/protect/api/events/test-event-123/video";

            var credentials = CreateValidCredentials();
            var eventKey = "alarm-events/2025/01/01/alarm-evt_123.json";
            var videoKey = "videos/2025/01/01/video-evt_123.mp4";
            var tempVideoPath = "/tmp/test-video.mp4";

            _mockCredentialsService.Setup(x => x.GetUnifiCredentialsAsync())
                .ReturnsAsync(credentials);

            _mockS3StorageService.Setup(x => x.GenerateS3Keys(It.IsAny<Trigger>(), It.IsAny<long>()))
                .Returns((eventKey, videoKey));

            _mockS3StorageService.Setup(x => x.StoreAlarmEventAsync(It.IsAny<Alarm>(), It.IsAny<Trigger>()))
                .ReturnsAsync(eventKey);

            _mockUnifiProtectService.Setup(x => x.DownloadVideoAsync(It.IsAny<Trigger>(), It.IsAny<string>(), It.IsAny<long>()))
                .ReturnsAsync(tempVideoPath);

            _mockS3StorageService.Setup(x => x.StoreVideoFileAsync(tempVideoPath, videoKey))
                .ThrowsAsync(new InvalidOperationException("S3 upload failed"));

            var expectedResponse = new APIGatewayProxyResponse { StatusCode = 200 };
            _mockResponseHelper.Setup(x => x.CreateSuccessResponse(It.IsAny<Trigger>(), It.IsAny<long>()))
                .Returns(expectedResponse);

            // Act
            var result = await _alarmProcessingService.ProcessAlarmAsync(alarm);

            // Assert
            Assert.Equal(expectedResponse, result);
            _mockUnifiProtectService.Verify(x => x.CleanupTempFile(tempVideoPath), Times.Once);
            _mockLogger.Verify(x => x.LogLine(It.Is<string>(s => s.Contains("ERROR uploading video to S3"))), Times.Once);
        }

        #endregion

        #region Helper Methods

        private static void SetValidAlarmBucketEnvironment()
        {
            Environment.SetEnvironmentVariable("StorageBucket", "test-alarm-bucket");
        }

        private static Alarm CreateValidAlarm()
        {
            return new Alarm
            {
                name = "Test Motion Alarm",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "test-device-mac",
                        eventId = "evt_test_123"
                    }
                },
                sources = new List<Source>
                {
                    new Source
                    {
                        device = "test-camera-mac",
                        type = "camera"
                    }
                }
            };
        }

        private static UnifiCredentials CreateValidCredentials()
        {
            return new UnifiCredentials
            {
                hostname = "https://192.168.1.1",
                username = "testuser",
                password = "testpass"
            };
        }

        #endregion
    }
}
