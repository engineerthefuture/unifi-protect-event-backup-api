using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Moq;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Models;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    /// <summary>
    /// Tests for coverage improvement focusing on model validation and edge cases.
    /// </summary>
    public class CoverageImprovementTests : IDisposable
    {
        private readonly string? _originalStorageBucket;
        private readonly string? _originalAlarmBucket;

        public CoverageImprovementTests()
        {
            _originalStorageBucket = Environment.GetEnvironmentVariable("StorageBucket");
            _originalAlarmBucket = Environment.GetEnvironmentVariable("AlarmBucket");
        }

        public void Dispose()
        {
            if (_originalStorageBucket != null)
                Environment.SetEnvironmentVariable("StorageBucket", _originalStorageBucket);
            else
                Environment.SetEnvironmentVariable("StorageBucket", null);

            if (_originalAlarmBucket != null)
                Environment.SetEnvironmentVariable("AlarmBucket", _originalAlarmBucket);
            else
                Environment.SetEnvironmentVariable("AlarmBucket", null);
        }
        [Fact]
        public void CameraSummaryMulti_Properties_GetSet()
        {
            // This will improve coverage for the CameraSummaryMulti class properties
            var cameraEventSummary = new CameraEventSummary
            {
                eventData = new Alarm
                {
                    name = "Test Alarm",
                    timestamp = 1672531200000,
                    triggers = new List<Trigger>
                    {
                        new Trigger
                        {
                            eventId = "test123",
                            key = "motion",
                            device = "AA:BB:CC:DD:EE:FF",
                            deviceName = "Test Camera"
                        }
                    }
                },
                videoUrl = "https://example.com/video.mp4",
                originalFileName = "video.mp4"
            };

            Assert.NotNull(cameraEventSummary.eventData);
            Assert.Equal("https://example.com/video.mp4", cameraEventSummary.videoUrl);
            Assert.Equal("video.mp4", cameraEventSummary.originalFileName);
        }

        [Fact]
        public void DailySummaryEvent_AllProperties_SetCorrectly()
        {
            // Test all properties of DailySummaryEvent for coverage
            var dailySummaryEvent = new DailySummaryEvent
            {
                EventId = "event123",
                Device = "AA:BB:CC:DD:EE:FF",
                Timestamp = 1672531200000,
                AlarmS3Key = "alarm_event123.json",
                VideoS3Key = "video_event123.mp4",
                PresignedVideoUrl = "https://example.com/video.mp4",
                AlarmName = "Motion Alert",
                DeviceName = "Test Camera",
                EventType = "motion",
                EventPath = "/path/to/event",
                EventLocalLink = "local://link",
                Metadata = new Dictionary<string, object>
                {
                    { "thumbnail", "base64data" },
                    { "originalFileName", "original.mp4" }
                }
            };

            Assert.Equal("event123", dailySummaryEvent.EventId);
            Assert.Equal("AA:BB:CC:DD:EE:FF", dailySummaryEvent.Device);
            Assert.Equal(1672531200000, dailySummaryEvent.Timestamp);
            Assert.Equal("alarm_event123.json", dailySummaryEvent.AlarmS3Key);
            Assert.Equal("video_event123.mp4", dailySummaryEvent.VideoS3Key);
            Assert.Equal("https://example.com/video.mp4", dailySummaryEvent.PresignedVideoUrl);
            Assert.Equal("Motion Alert", dailySummaryEvent.AlarmName);
            Assert.Equal("Test Camera", dailySummaryEvent.DeviceName);
            Assert.Equal("motion", dailySummaryEvent.EventType);
            Assert.Equal("/path/to/event", dailySummaryEvent.EventPath);
            Assert.Equal("local://link", dailySummaryEvent.EventLocalLink);
            Assert.NotNull(dailySummaryEvent.Metadata);
            Assert.Equal(2, dailySummaryEvent.Metadata.Count);
        }

        [Fact]
        public void DailySummary_AllProperties_SetCorrectly()
        {
            // Test all properties of DailySummary for coverage
            var dailySummary = new DailySummary
            {
                metadata = new SummaryMetadata
                {
                    date = "2023-01-01",
                    dateFormatted = "2023-01-01",
                    lastUpdated = "2023-01-01T12:00:00Z",
                    totalEvents = 10,
                    missingVideoCount = 2,
                    dlqMessageCount = 0
                },
                eventCounts = new Dictionary<string, int>
                {
                    { "motion", 5 },
                    { "person", 3 }
                },
                deviceCounts = new Dictionary<string, int>
                {
                    { "Camera1", 5 },
                    { "Camera2", 3 }
                },
                hourlyCounts = new Dictionary<string, int>
                {
                    { "12", 4 },
                    { "13", 4 }
                },
                events = new List<DailySummaryEvent>
                {
                    new DailySummaryEvent
                    {
                        EventId = "event1",
                        Device = "device1",
                        Timestamp = 1672531200000
                    }
                },
                missingVideoEvents = new List<MissingVideoEvent>
                {
                    new MissingVideoEvent
                    {
                        eventId = "missing1",
                        jsonFile = "alarm_missing1.json",
                        lastModified = "2023-01-01T12:00:00Z",
                        size = 1024
                    }
                },
                dlqCounts = new Dictionary<string, int>
                {
                    { "AlarmProcessingDLQ", 0 }
                }
            };

            Assert.NotNull(dailySummary.metadata);
            Assert.Equal("2023-01-01", dailySummary.metadata.date);
            Assert.Equal(10, dailySummary.metadata.totalEvents);
            Assert.Equal(2, dailySummary.metadata.missingVideoCount);
            Assert.Equal(0, dailySummary.metadata.dlqMessageCount);
            Assert.NotNull(dailySummary.eventCounts);
            Assert.Equal(2, dailySummary.eventCounts.Count);
            Assert.NotNull(dailySummary.deviceCounts);
            Assert.Equal(2, dailySummary.deviceCounts.Count);
            Assert.NotNull(dailySummary.hourlyCounts);
            Assert.Equal(2, dailySummary.hourlyCounts.Count);
            Assert.NotNull(dailySummary.events);
            Assert.Single(dailySummary.events);
            Assert.NotNull(dailySummary.missingVideoEvents);
            Assert.Single(dailySummary.missingVideoEvents);
            Assert.NotNull(dailySummary.dlqCounts);
            Assert.Single(dailySummary.dlqCounts);
        }

        [Fact]
        public void SummaryEvent_AllProperties_SetCorrectly()
        {
            // Test all properties of SummaryEvent for coverage
            var summaryEvent = new SummaryEvent
            {
                EventId = "event123",
                Device = "AA:BB:CC:DD:EE:FF",
                Timestamp = 1672531200000,
                AlarmS3Key = "alarm_event123.json",
                VideoS3Key = "video_event123.mp4",
                PresignedVideoUrl = "https://example.com/video.mp4",
                AlarmName = "Motion Alert",
                DeviceName = "Test Camera",
                EventType = "motion",
                EventPath = "/path/to/event",
                EventLocalLink = "local://link",
                Metadata = new Dictionary<string, string>
                {
                    { "thumbnail", "base64data" },
                    { "originalFileName", "original.mp4" }
                }
            };

            Assert.Equal("event123", summaryEvent.EventId);
            Assert.Equal("AA:BB:CC:DD:EE:FF", summaryEvent.Device);
            Assert.Equal(1672531200000, summaryEvent.Timestamp);
            Assert.Equal("alarm_event123.json", summaryEvent.AlarmS3Key);
            Assert.Equal("video_event123.mp4", summaryEvent.VideoS3Key);
            Assert.Equal("https://example.com/video.mp4", summaryEvent.PresignedVideoUrl);
            Assert.Equal("Motion Alert", summaryEvent.AlarmName);
            Assert.Equal("Test Camera", summaryEvent.DeviceName);
            Assert.Equal("motion", summaryEvent.EventType);
            Assert.Equal("/path/to/event", summaryEvent.EventPath);
            Assert.Equal("local://link", summaryEvent.EventLocalLink);
            Assert.NotNull(summaryEvent.Metadata);
            Assert.Equal(2, summaryEvent.Metadata.Count);
        }

        [Fact]
        public void MissingVideoEvent_AllProperties_SetCorrectly()
        {
            // Test all properties of MissingVideoEvent for coverage
            var missingVideoEvent = new MissingVideoEvent
            {
                eventId = "missing123",
                jsonFile = "alarm_missing123.json",
                lastModified = "2023-01-01T12:00:00Z",
                size = 2048
            };

            Assert.Equal("missing123", missingVideoEvent.eventId);
            Assert.Equal("alarm_missing123.json", missingVideoEvent.jsonFile);
            Assert.Equal("2023-01-01T12:00:00Z", missingVideoEvent.lastModified);
            Assert.Equal(2048, missingVideoEvent.size);
        }

        [Fact]
        public void DeviceMetadata_AllProperties_SetCorrectly()
        {
            // Test DeviceMetadata for coverage
            var deviceMetadata = new DeviceMetadata
            {
                DeviceName = "Test Camera",
                DeviceMac = "AA:BB:CC:DD:EE:FF",
                ArchiveButtonX = 100,
                ArchiveButtonY = 200
            };

            Assert.Equal("Test Camera", deviceMetadata.DeviceName);
            Assert.Equal("AA:BB:CC:DD:EE:FF", deviceMetadata.DeviceMac);
            Assert.Equal(100, deviceMetadata.ArchiveButtonX);
            Assert.Equal(200, deviceMetadata.ArchiveButtonY);
        }

        [Fact]
        public void UnifiCredentials_AllProperties_SetCorrectly()
        {
            // Test UnifiCredentials for coverage
            var credentials = new UnifiCredentials
            {
                hostname = "https://unifi.local",
                username = "admin",
                password = "password123"
            };

            Assert.Equal("https://unifi.local", credentials.hostname);
            Assert.Equal("admin", credentials.username);
            Assert.Equal("password123", credentials.password);
        }

        [Fact]
        public void Alarm_WithAllPropertiesSet_ValidatesCorrectly()
        {
            // Test Alarm model with all properties for coverage
            var alarm = new Alarm
            {
                name = "Test Alert",
                timestamp = 1672531200000,
                eventPath = "/path/to/event",
                eventLocalLink = "local://link",
                thumbnail = "base64thumbnaildata",
                sources = new List<Source>
                {
                    new Source
                    {
                        device = "AA:BB:CC:DD:EE:FF",
                        type = "camera"
                    }
                },
                conditions = new List<Condition>
                {
                    new Condition()
                },
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        eventId = "trigger123",
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        deviceName = "Test Camera",
                        date = "2023-01-01 12:00:00",
                        eventKey = "event123.json",
                        videoKey = "video123.mp4",
                        originalFileName = "original.mp4"
                    }
                }
            };

            Assert.Equal("Test Alert", alarm.name);
            Assert.Equal(1672531200000, alarm.timestamp);
            Assert.Equal("/path/to/event", alarm.eventPath);
            Assert.Equal("local://link", alarm.eventLocalLink);
            Assert.Equal("base64thumbnaildata", alarm.thumbnail);
            Assert.NotNull(alarm.sources);
            Assert.Single(alarm.sources);
            Assert.NotNull(alarm.conditions);
            Assert.Single(alarm.conditions);
            Assert.NotNull(alarm.triggers);
            Assert.Single(alarm.triggers);
            
            var trigger = alarm.triggers[0];
            Assert.Equal("trigger123", trigger.eventId);
            Assert.Equal("motion", trigger.key);
            Assert.Equal("AA:BB:CC:DD:EE:FF", trigger.device);
            Assert.Equal("Test Camera", trigger.deviceName);
            Assert.Equal("2023-01-01 12:00:00", trigger.date);
            Assert.Equal("event123.json", trigger.eventKey);
            Assert.Equal("video123.mp4", trigger.videoKey);
            Assert.Equal("original.mp4", trigger.originalFileName);
        }

        [Fact]
        public async Task AlarmProcessingService_ProcessAlarm_WithException_Returns500()
        {
            // Setup - This tests the exception catch block in ProcessAlarmAsync
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            
            var mockS3 = new Mock<IS3StorageService>();
            var mockUnifi = new Mock<IUnifiProtectService>();
            var mockCreds = new Mock<ICredentialsService>();
            var mockResponse = new Mock<IResponseHelper>();
            var mockLogger = new Mock<ILambdaLogger>();
            var mockSummaryQueue = new Mock<ISummaryEventQueueService>();

            // Make GetUnifiCredentialsAsync throw an exception
            mockCreds.Setup(x => x.GetUnifiCredentialsAsync())
                .ThrowsAsync(new Exception("Test exception"));

            mockResponse.Setup(x => x.CreateErrorResponse(
                It.IsAny<HttpStatusCode>(), 
                It.IsAny<string>()))
                .Returns(new Amazon.Lambda.APIGatewayEvents.APIGatewayProxyResponse());

            var service = new AlarmProcessingService(
                mockS3.Object, 
                mockUnifi.Object, 
                mockCreds.Object, 
                mockResponse.Object, 
                mockLogger.Object, 
                mockSummaryQueue.Object);

            var alarm = new Alarm
            {
                name = "Test",
                timestamp = 1234567890,
                triggers = new List<Trigger> 
                { 
                    new Trigger 
                    { 
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        eventId = "evt1"
                    } 
                }
            };

            // Act
            var result = await service.ProcessAlarmAsync(alarm);

            // Assert - exception path is exercised
            mockResponse.Verify(x => x.CreateErrorResponse(
                HttpStatusCode.InternalServerError, 
                It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void AppConfiguration_MultiplePaths_Coverage()
        {
            // Tests for covering AppConfiguration edge cases without complex flows
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            
            // Access various configuration properties to increase coverage
            var alarmBucket = AppConfiguration.AlarmBucketName;
            var deployedEnv = AppConfiguration.DeployedEnv;
            var functionName = AppConfiguration.FunctionName;
            var downloadDir = AppConfiguration.DownloadDirectory;
            var maxRetention = AppConfiguration.MaxRetentionDays;
            var processingDelay = AppConfiguration.ProcessingDelaySeconds;
            var videoPageDelay = AppConfiguration.VideoPageLoadDelaySeconds;
            var throttle = AppConfiguration.EventProcessingThrottleSeconds;
            
            Assert.NotNull(downloadDir);
            Assert.True(maxRetention > 0);
        }

        [Fact]
        public void AppConfiguration_WithMissingEnvironmentVariables_HandlesGracefully()
        {
            // Test various AppConfiguration property accesses with missing env vars
            Environment.SetEnvironmentVariable("StorageBucket", null);
            Environment.SetEnvironmentVariable("DeployedEnv", null);
            Environment.SetEnvironmentVariable("FunctionName", null);

            // These should not throw, just return null or empty
            var alarmBucket = AppConfiguration.AlarmBucketName;
            var deployedEnv = AppConfiguration.DeployedEnv;
            var functionName = AppConfiguration.FunctionName;

            // Set them back for other tests
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
        }

        [Fact]
        public void Source_AllProperties_Coverage()
        {
            // Cover Source model properties
            var source = new Source
            {
                device = "AA:BB:CC:DD:EE:FF",
                type = "camera"
            };

            Assert.Equal("AA:BB:CC:DD:EE:FF", source.device);
            Assert.Equal("camera", source.type);
        }

        [Fact]
        public void Condition_InstantiationCoverage()
        {
            // Cover Condition model instantiation
            var condition = new Condition();
            Assert.NotNull(condition);
        }

        [Fact]
        public void CameraSummaryMulti_Instantiation()
        {
            // Cover CameraSummaryMulti instantiation
            var multi = new CameraSummaryMulti();
            Assert.NotNull(multi);
            
            // Test with CameraEventSummary
            var cameraEvent = new CameraEventSummary
            {
                eventData = new Alarm
                {
                    timestamp = 123,
                    triggers = new List<Trigger>
                    {
                        new Trigger
                        {
                            key = "motion",
                            device = "AA:BB:CC:DD:EE:FF",
                            eventId = "evt1"
                        }
                    }
                },
                videoUrl = "url",
                originalFileName = "file.mp4"
            };

            Assert.NotNull(cameraEvent);
        }

        [Fact]
        public void SummaryMetadata_AllProperties_Coverage()
        {
            var metadata = new SummaryMetadata
            {
                date = "2023-01-01",
                dateFormatted = "January 1, 2023",
                lastUpdated = "2023-01-01T12:00:00Z",
                totalEvents = 100,
                missingVideoCount = 5,
                dlqMessageCount = 2
            };

            Assert.Equal("2023-01-01", metadata.date);
            Assert.Equal("January 1, 2023", metadata.dateFormatted);
            Assert.Equal("2023-01-01T12:00:00Z", metadata.lastUpdated);
            Assert.Equal(100, metadata.totalEvents);
            Assert.Equal(5, metadata.missingVideoCount);
            Assert.Equal(2, metadata.dlqMessageCount);
        }

        [Fact]
        public void ResponseHelper_CreateSuccessAndErrorResponses_Coverage()
        {
            var helper = new ResponseHelper();

            var trigger = new Trigger
            {
                key = "motion",
                device = "AA:BB:CC:DD:EE:FF",
                eventId = "evt1"
            };

            // Test success response
            var successResponse = helper.CreateSuccessResponse(trigger, 1234567890);
            Assert.Equal((int)HttpStatusCode.OK, successResponse.StatusCode);

            // Test error response
            var errorResponse = helper.CreateErrorResponse(HttpStatusCode.BadRequest, "Error message");
            Assert.Equal((int)HttpStatusCode.BadRequest, errorResponse.StatusCode);
            Assert.Contains("Error message", errorResponse.Body);

            // Test different status codes for branch coverage
            var notFoundResponse = helper.CreateErrorResponse(HttpStatusCode.NotFound, "Not found");
            Assert.Equal((int)HttpStatusCode.NotFound, notFoundResponse.StatusCode);

            var serverErrorResponse = helper.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server error");
            Assert.Equal((int)HttpStatusCode.InternalServerError, serverErrorResponse.StatusCode);
        }

        [Fact]

        public void Alarm_EmptyCollections_Coverage()
        {
            // Test alarm with empty/null collections
            var alarm1 = new Alarm
            {
                name = "Test",
                timestamp = 123,
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        eventId = "evt1"
                    }
                },
                sources = new List<Source>(),
                conditions = new List<Condition>()
            };

            Assert.Single(alarm1.triggers);
            Assert.Empty(alarm1.sources);
            Assert.Empty(alarm1.conditions);

            var alarm2 = new Alarm
            {
                name = "Test2",
                timestamp = 456,
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        eventId = "evt2"
                    }
                },
                sources = null,
                conditions = null
            };

            Assert.Null(alarm2.sources);
            Assert.Null(alarm2.conditions);
        }

        [Fact]
        public void Trigger_AllProperties_Coverage()
        {
            // Comprehensive trigger property coverage
            var trigger = new Trigger
            {
                eventId = "evt123",
                key = "smartDetectZone",
                device = "AA:BB:CC:DD:EE:FF",
                deviceName = "Front Door",
                date = "2023-01-01",
                eventKey = "event.json",
                videoKey = "video.mp4",
                originalFileName = "original.mp4"
            };

            Assert.Equal("evt123", trigger.eventId);
            Assert.Equal("smartDetectZone", trigger.key);
            Assert.Equal("AA:BB:CC:DD:EE:FF", trigger.device);
            Assert.Equal("Front Door", trigger.deviceName);
            Assert.Equal("2023-01-01", trigger.date);
            Assert.Equal("event.json", trigger.eventKey);
            Assert.Equal("video.mp4", trigger.videoKey);
            Assert.Equal("original.mp4", trigger.originalFileName);
        }

        [Fact]
        public void SummaryEvent_EmptyMetadata_Coverage()
        {
            // Test SummaryEvent with various metadata states
            var event1 = new SummaryEvent
            {
                EventId = "e1",
                Metadata = new Dictionary<string, string>()
            };
            Assert.Empty(event1.Metadata);

            var event2 = new SummaryEvent
            {
                EventId = "e2",
                Metadata = new Dictionary<string, string>
                {
                    { "key1", "value1" }
                }
            };
            Assert.Single(event2.Metadata);
            Assert.True(event2.Metadata.ContainsKey("key1"));
        }

        [Fact]
        public void DailySummary_EmptyCollections_Coverage()
        {
            // Test DailySummary with empty collections
            var summary = new DailySummary
            {
                metadata = new SummaryMetadata { date = "2023-01-01" },
                events = new List<DailySummaryEvent>(),
                missingVideoEvents = new List<MissingVideoEvent>(),
                eventCounts = new Dictionary<string, int>(),
                deviceCounts = new Dictionary<string, int>(),
                hourlyCounts = new Dictionary<string, int>(),
                dlqCounts = new Dictionary<string, int>()
            };

            Assert.Empty(summary.events);
            Assert.Empty(summary.missingVideoEvents);
            Assert.Empty(summary.eventCounts);
            Assert.Empty(summary.deviceCounts);
            Assert.Empty(summary.hourlyCounts);
            Assert.Empty(summary.dlqCounts);
        }

        [Fact]
        public void DailySummaryEvent_NullableProperties_Coverage()
        {
            // Test with null values
            var evt = new DailySummaryEvent
            {
                EventId = null,
                Device = null,
                Timestamp = 0,
                AlarmS3Key = null,
                VideoS3Key = null,
                PresignedVideoUrl = string.Empty,
                AlarmName = null,
                DeviceName = null,
                EventType = null
            };

            Assert.Null(evt.EventId);
            Assert.Null(evt.Device);
            Assert.Null(evt.VideoS3Key);
        }

        [Fact]
        public void ResponseHelper_AllResponseTypes_Coverage()
        {
            var helper = new ResponseHelper();

            var trigger = new Trigger
            {
                key = "motion",
                device = "AA:BB:CC:DD:EE:FF",
                eventId = "evt1"
            };

            // Test CreateSuccessResponse with trigger
            var response1 = helper.CreateSuccessResponse(trigger, 123456789);
            Assert.Equal(200, response1.StatusCode);
            Assert.NotNull(response1.Body);
            Assert.NotNull(response1.Headers);

            // Test CreateSuccessResponse with object
            var response2 = helper.CreateSuccessResponse(new { data = "test" });
            Assert.Equal(200, response2.StatusCode);

            // Test CreateSuccessResponse with complex object
            var response3 = helper.CreateSuccessResponse(new { 
                status = "success", 
                message = "Operation completed",
                data = new { id = 123, name = "test" }
            });
            Assert.Equal(200, response3.StatusCode);
            Assert.NotNull(response3.Body);

            // Test error responses with various status codes
            var err400 = helper.CreateErrorResponse(HttpStatusCode.BadRequest, "Bad request");
            Assert.Equal(400, err400.StatusCode);
            
            var err401 = helper.CreateErrorResponse(HttpStatusCode.Unauthorized, "Unauthorized");
            Assert.Equal(401, err401.StatusCode);
            
            var err403 = helper.CreateErrorResponse(HttpStatusCode.Forbidden, "Forbidden");
            Assert.Equal(403, err403.StatusCode);
            
            var err404 = helper.CreateErrorResponse(HttpStatusCode.NotFound, "Not found");
            Assert.Equal(404, err404.StatusCode);
            
            var err500 = helper.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server error");
            Assert.Equal(500, err500.StatusCode);
            
            var err503 = helper.CreateErrorResponse(HttpStatusCode.ServiceUnavailable, "Service unavailable");
            Assert.Equal(503, err503.StatusCode);
        }

        [Theory]
        [InlineData("motion", "AA:BB:CC:DD:EE:FF", "evt1")]
        [InlineData("smartDetectZone", "11:22:33:44:55:66", "evt2")]
        [InlineData("ring", "99:88:77:66:55:44", "evt3")]
        public void Trigger_VariousEventTypes_Coverage(string key, string device, string eventId)
        {
            var trigger = new Trigger
            {
                key = key,
                device = device,
                eventId = eventId,
                deviceName = $"Camera-{device}",
                videoKey = $"video_{eventId}.mp4",
                eventKey = $"event_{eventId}.json"
            };

            Assert.Equal(key, trigger.key);
            Assert.Equal(device, trigger.device);
            Assert.Equal(eventId, trigger.eventId);
            Assert.NotNull(trigger.deviceName);
            Assert.NotNull(trigger.videoKey);
            Assert.NotNull(trigger.eventKey);
        }

        [Fact]
        public void Models_NullAndEmptyStrings_Coverage()
        {
            // Test models with various null/empty states
            var alarm = new Alarm
            {
                name = string.Empty,
                timestamp = 0,
                eventPath = null,
                eventLocalLink = null,
                thumbnail = null,
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF",
                        eventId = "evt1",
                        deviceName = null,
                        date = null,
                        videoKey = null,
                        eventKey = null,
                        originalFileName = null
                    }
                }
            };

            Assert.Empty(alarm.name);
            Assert.Null(alarm.eventPath);
            Assert.Null(alarm.thumbnail);
            Assert.Null(alarm.triggers[0].deviceName);

            var summary = new SummaryEvent
            {
                EventId = null,
                Device = string.Empty,
                AlarmName = null,
                DeviceName = string.Empty,
                EventType = null,
                VideoS3Key = null,
                PresignedVideoUrl = null
            };

            Assert.Null(summary.EventId);
            Assert.Empty(summary.Device);
            Assert.Null(summary.AlarmName);
        }

        [Fact]
        public void DeviceMetadata_VariousConfigurations_Coverage()
        {
            // Test with various device metadata configurations
            var metadata1 = new DeviceMetadata
            {
                DeviceName = "Front Door",
                DeviceMac = "AA:BB:CC:DD:EE:FF",
                ArchiveButtonX = 100,
                ArchiveButtonY = 200
            };

            var metadata2 = new DeviceMetadata
            {
                DeviceName = "Back Yard",
                DeviceMac = "11:22:33:44:55:66",
                ArchiveButtonX = 150,
                ArchiveButtonY = 250
            };

            Assert.NotEqual(metadata1.DeviceMac, metadata2.DeviceMac);
            Assert.NotEqual(metadata1.ArchiveButtonX, metadata2.ArchiveButtonX);
        }

        [Fact]
        public void Alarm_VariousPropertyCombinations_Coverage()
        {
            // Test alarm with only required fields
            var minimal = new Alarm
            {
                timestamp = 123,
                triggers = new List<Trigger>
                {
                    new Trigger { key = "motion", device = "AA:BB:CC:DD:EE:FF", eventId = "1" }
                }
            };
            Assert.Null(minimal.name);

            // Test alarm with all optional fields
            var full = new Alarm
            {
                name = "Full Alarm",
                timestamp = 456,
                eventPath = "/path",
                eventLocalLink = "link",
                thumbnail = "thumb",
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        key = "smartDetectZone",
                        device = "BB:CC:DD:EE:FF:AA",
                        eventId = "2",
                        deviceName = "Camera",
                        date = "2023-01-01",
                        videoKey = "video.mp4",
                        eventKey = "event.json",
                        originalFileName = "original.mp4"
                    }
                },
                sources = new List<Source>
                {
                    new Source { device = "BB:CC:DD:EE:FF:AA", type = "camera" }
                },
                conditions = new List<Condition> { new Condition() }
            };

            Assert.NotNull(full.name);
            Assert.NotNull(full.eventPath);
            Assert.NotNull(full.thumbnail);
            Assert.NotEmpty(full.sources);
            Assert.NotEmpty(full.conditions);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("valid-name")]
        public void UnifiCredentials_HostnameVariations_Coverage(string? hostname)
        {
            var creds = new UnifiCredentials
            {
                hostname = hostname,
                username = "user",
                password = "pass"
            };

            Assert.Equal(hostname, creds.hostname);
        }

        [Fact]
        public void DailySummary_VariousCollectionSizes_Coverage()
        {
            // Test with single items
            var summary1 = new DailySummary
            {
                metadata = new SummaryMetadata { date = "2023-01-01", totalEvents = 1 },
                events = new List<DailySummaryEvent>
                {
                    new DailySummaryEvent { EventId = "e1", Timestamp = 123 }
                },
                eventCounts = new Dictionary<string, int> { { "motion", 1 } },
                deviceCounts = new Dictionary<string, int> { { "cam1", 1 } },
                hourlyCounts = new Dictionary<string, int> { { "12", 1 } }
            };
            Assert.Single(summary1.events);

            // Test with multiple items
            var summary2 = new DailySummary
            {
                metadata = new SummaryMetadata { date = "2023-01-02", totalEvents = 5 },
                events = new List<DailySummaryEvent>
                {
                    new DailySummaryEvent { EventId = "e1", Timestamp = 123 },
                    new DailySummaryEvent { EventId = "e2", Timestamp = 456 },
                    new DailySummaryEvent { EventId = "e3", Timestamp = 789 }
                },
                eventCounts = new Dictionary<string, int>
                {
                    { "motion", 3 },
                    { "smartDetectZone", 2 }
                },
                deviceCounts = new Dictionary<string, int>
                {
                    { "cam1", 2 },
                    { "cam2", 3 }
                },
                hourlyCounts = new Dictionary<string, int>
                {
                    { "10", 1 },
                    { "11", 2 },
                    { "12", 2 }
                }
            };
            Assert.Equal(3, summary2.events.Count);
            Assert.Equal(2, summary2.eventCounts.Count);
            Assert.Equal(2, summary2.deviceCounts.Count);
            Assert.Equal(3, summary2.hourlyCounts.Count);
        }

        [Fact]
        public void Source_MultipleDevices_Coverage()
        {
            var sources = new List<Source>
            {
                new Source { device = "AA:BB:CC:DD:EE:FF", type = "camera" },
                new Source { device = "11:22:33:44:55:66", type = "sensor" },
                new Source { device = "99:88:77:66:55:44", type = "doorbell" }
            };

            Assert.Equal(3, sources.Count);
            Assert.All(sources, s => Assert.NotNull(s.device));
            Assert.Contains(sources, s => s.type == "camera");
            Assert.Contains(sources, s => s.type == "sensor");
            Assert.Contains(sources, s => s.type == "doorbell");
        }
    }
}
