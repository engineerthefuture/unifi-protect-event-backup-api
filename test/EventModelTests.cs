/************************
 * Unifi Webhook Event Receiver
 * EventModelTests.cs
 * 
 * Unit tests for Event model classes (Alarm, Source, Condition, Trigger).
 * Tests property getters/setters and basic model functionality.
 * 
 * Author: GitHub Copilot
 * Created: 01-14-2025
 ***********************/

using Xunit;
using System.Collections.Generic;
using UnifiWebhookEventReceiver.Models;

namespace UnifiWebhookEventReceiver.Tests
{
    /// <summary>
    /// Tests for Event model classes to ensure proper property functionality.
    /// These tests cover basic property get/set operations for simple model classes.
    /// </summary>
    public class EventModelTests
    {
        [Fact]
        public void Alarm_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var alarm = new Alarm
            {
                name = "Test Alarm",
                timestamp = 1642077600000,
                eventPath = "/test/path",
                eventLocalLink = "http://test.local/event",
                triggers = new List<Trigger>
                {
                    new Trigger { key = "motion", device = "test-device", eventId = "test-event-id" }
                },
                sources = new List<Source>
                {
                    new Source { device = "test-device", type = "camera" }
                },
                conditions = new List<Condition>
                {
                    new Condition { type = "motion", source = "test-source" }
                }
            };

            // Assert
            Assert.Equal("Test Alarm", alarm.name);
            Assert.Equal(1642077600000, alarm.timestamp);
            Assert.Equal("/test/path", alarm.eventPath);
            Assert.Equal("http://test.local/event", alarm.eventLocalLink);
            Assert.NotNull(alarm.triggers);
            Assert.Single(alarm.triggers);
            Assert.NotNull(alarm.sources);
            Assert.Single(alarm.sources);
            Assert.NotNull(alarm.conditions);
            Assert.Single(alarm.conditions);
        }

        [Fact]
        public void Source_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var source = new Source
            {
                device = "test-device-123",
                type = "camera"
            };

            // Assert
            Assert.Equal("test-device-123", source.device);
            Assert.Equal("camera", source.type);
        }

        [Fact]
        public void Condition_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var condition = new Condition
            {
                type = "motion",
                source = "test-source-456"
            };

            // Assert
            Assert.Equal("motion", condition.type);
            Assert.Equal("test-source-456", condition.source);
        }

        [Fact]
        public void Trigger_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var trigger = new Trigger
            {
                key = "motion",
                device = "test-device-789",
                eventId = "test-event-123",
                deviceName = "Front Door Camera",
                date = "2025-01-14",
                eventKey = "events/test-event-123.json",
                videoKey = "videos/test-event-123.mp4"
            };

            // Assert
            Assert.Equal("motion", trigger.key);
            Assert.Equal("test-device-789", trigger.device);
            Assert.Equal("test-event-123", trigger.eventId);
            Assert.Equal("Front Door Camera", trigger.deviceName);
            Assert.Equal("2025-01-14", trigger.date);
            Assert.Equal("events/test-event-123.json", trigger.eventKey);
            Assert.Equal("videos/test-event-123.mp4", trigger.videoKey);
        }

        [Fact]
        public void Alarm_WithNullOptionalProperties_AllowsNullValues()
        {
            // Arrange & Act
            var alarm = new Alarm
            {
                timestamp = 1642077600000,
                triggers = new List<Trigger>
                {
                    new Trigger { key = "motion", device = "test-device", eventId = "test-event-id" }
                },
                name = null,
                eventPath = null,
                eventLocalLink = null,
                sources = null,
                conditions = null
            };

            // Assert
            Assert.Null(alarm.name);
            Assert.Null(alarm.eventPath);
            Assert.Null(alarm.eventLocalLink);
            Assert.Null(alarm.sources);
            Assert.Null(alarm.conditions);
            Assert.Equal(1642077600000, alarm.timestamp);
            Assert.NotNull(alarm.triggers);
        }

        [Fact]
        public void Condition_WithNullOptionalProperties_AllowsNullValues()
        {
            // Arrange & Act
            var condition = new Condition
            {
                type = null,
                source = null
            };

            // Assert
            Assert.Null(condition.type);
            Assert.Null(condition.source);
        }

        [Fact]
        public void Trigger_WithNullOptionalProperties_AllowsNullValues()
        {
            // Arrange & Act
            var trigger = new Trigger
            {
                key = "motion",
                device = "test-device",
                eventId = "test-event-id",
                deviceName = null,
                date = null,
                eventKey = null,
                videoKey = null
            };

            // Assert
            Assert.Equal("motion", trigger.key);
            Assert.Equal("test-device", trigger.device);
            Assert.Equal("test-event-id", trigger.eventId);
            Assert.Null(trigger.deviceName);
            Assert.Null(trigger.date);
            Assert.Null(trigger.eventKey);
            Assert.Null(trigger.videoKey);
        }

        [Fact]
        public void UnifiCredentials_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var credentials = new UnifiCredentials
            {
                hostname = "unifi.example.com",
                username = "admin",
                password = "secretpassword"
            };

            // Assert
            Assert.Equal("unifi.example.com", credentials.hostname);
            Assert.Equal("admin", credentials.username);
            Assert.Equal("secretpassword", credentials.password);
        }

        [Fact]
        public void UnifiCredentials_DefaultConstructor_InitializesWithEmptyStrings()
        {
            // Arrange & Act
            var credentials = new UnifiCredentials();

            // Assert
            Assert.Equal(string.Empty, credentials.hostname);
            Assert.Equal(string.Empty, credentials.username);
            Assert.Equal(string.Empty, credentials.password);
        }

        [Fact]
        public void UnifiCredentials_Properties_CanBeSetToEmptyStrings()
        {
            // Arrange & Act
            var credentials = new UnifiCredentials
            {
                hostname = "",
                username = "",
                password = ""
            };

            // Assert
            Assert.Equal("", credentials.hostname);
            Assert.Equal("", credentials.username);
            Assert.Equal("", credentials.password);
        }

        [Fact]
        public void Alarm_CanCreateWithRequiredFieldsOnly()
        {
            // Arrange & Act
            var alarm = new Alarm
            {
                timestamp = 1642077600000,
                triggers = new List<Trigger>()
            };

            // Assert
            Assert.Equal(1642077600000, alarm.timestamp);
            Assert.NotNull(alarm.triggers);
            Assert.Empty(alarm.triggers);
            Assert.Null(alarm.name);
            Assert.Null(alarm.sources);
            Assert.Null(alarm.conditions);
            Assert.Null(alarm.eventPath);
            Assert.Null(alarm.eventLocalLink);
        }

        [Fact]
        public void Source_CanCreateWithRequiredFields()
        {
            // Arrange & Act
            var source = new Source
            {
                device = "cam-001",
                type = "camera"
            };

            // Assert
            Assert.Equal("cam-001", source.device);
            Assert.Equal("camera", source.type);
        }

        [Fact]
        public void Trigger_CanCreateWithRequiredFields()
        {
            // Arrange & Act
            var trigger = new Trigger
            {
                key = "alarm",
                device = "sensor-001", 
                eventId = "evt-123"
            };

            // Assert
            Assert.Equal("alarm", trigger.key);
            Assert.Equal("sensor-001", trigger.device);
            Assert.Equal("evt-123", trigger.eventId);
            Assert.Null(trigger.deviceName);
            Assert.Null(trigger.date);
            Assert.Null(trigger.eventKey);
            Assert.Null(trigger.videoKey);
        }

        #region CameraSummary Model Tests

        [Fact]
        public void CameraEventSummary_Properties_CanBeSetAndRetrieved()
        {
            // Arrange
            var alarm = new Alarm
            {
                timestamp = 1642077600000,
                triggers = new List<Trigger>()
            };

            // Act
            var cameraEvent = new CameraEventSummary
            {
                eventData = alarm,
                videoUrl = "https://example.com/video.mp4",
                originalFileName = "front_door_20250101_120000.mp4"
            };

            // Assert
            Assert.Equal(alarm, cameraEvent.eventData);
            Assert.Equal("https://example.com/video.mp4", cameraEvent.videoUrl);
            Assert.Equal("front_door_20250101_120000.mp4", cameraEvent.originalFileName);
        }

        [Fact]
        public void CameraEventSummary_DefaultConstructor_InitializesWithNullValues()
        {
            // Arrange & Act
            var cameraEvent = new CameraEventSummary();

            // Assert
            Assert.Null(cameraEvent.eventData);
            Assert.Null(cameraEvent.videoUrl);
            Assert.Null(cameraEvent.originalFileName);
        }

        [Fact]
        public void CameraSummaryMulti_Properties_CanBeSetAndRetrieved()
        {
            // Arrange
            var events = new List<CameraEventSummary>
            {
                new CameraEventSummary { originalFileName = "event1.mp4" },
                new CameraEventSummary { originalFileName = "event2.mp4" }
            };

            // Act
            var cameraSummary = new CameraSummaryMulti
            {
                cameraId = "cam-123",
                cameraName = "Front Door Camera",
                events = events,
                count24h = 5
            };

            // Assert
            Assert.Equal("cam-123", cameraSummary.cameraId);
            Assert.Equal("Front Door Camera", cameraSummary.cameraName);
            Assert.Equal(events, cameraSummary.events);
            Assert.Equal(5, cameraSummary.count24h);
        }

        [Fact]
        public void CameraSummaryMulti_DefaultConstructor_InitializesWithDefaults()
        {
            // Arrange & Act
            var cameraSummary = new CameraSummaryMulti();

            // Assert
            Assert.Equal(string.Empty, cameraSummary.cameraId);
            Assert.Equal(string.Empty, cameraSummary.cameraName);
            Assert.NotNull(cameraSummary.events);
            Assert.Empty(cameraSummary.events);
            Assert.Equal(0, cameraSummary.count24h);
        }

        #endregion

        #region DailySummary Model Tests

        [Fact]
        public void SummaryMetadata_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var metadata = new SummaryMetadata
            {
                date = "2025-01-14",
                dateFormatted = "2025-01-14",
                lastUpdated = "2025-01-14T12:00:00Z",
                totalEvents = 10,
                missingVideoCount = 2,
                dlqMessageCount = 0
            };

            // Assert
            Assert.Equal("2025-01-14", metadata.date);
            Assert.Equal("2025-01-14", metadata.dateFormatted);
            Assert.Equal("2025-01-14T12:00:00Z", metadata.lastUpdated);
            Assert.Equal(10, metadata.totalEvents);
            Assert.Equal(2, metadata.missingVideoCount);
            Assert.Equal(0, metadata.dlqMessageCount);
        }

        [Fact]
        public void SummaryMetadata_DefaultConstructor_InitializesWithDefaults()
        {
            // Arrange & Act
            var metadata = new SummaryMetadata();

            // Assert
            Assert.Equal(string.Empty, metadata.date);
            Assert.Equal(string.Empty, metadata.dateFormatted);
            Assert.Equal(string.Empty, metadata.lastUpdated);
            Assert.Equal(0, metadata.totalEvents);
            Assert.Equal(0, metadata.missingVideoCount);
            Assert.Equal(0, metadata.dlqMessageCount);
        }

        [Fact]
        public void DailySummaryEvent_Properties_CanBeSetAndRetrieved()
        {
            // Arrange
            var metadata = new Dictionary<string, object>
            {
                { "thumbnail", "base64imagedata" },
                { "originalFileName", "test.mp4" }
            };

            // Act
            var summaryEvent = new DailySummaryEvent
            {
                EventId = "evt-123",
                Device = "cam-001",
                Timestamp = 1642077600000,
                AlarmS3Key = "events/evt-123.json",
                VideoS3Key = "videos/evt-123.mp4",
                PresignedVideoUrl = "https://s3.amazonaws.com/presigned-url",
                AlarmName = "Motion Detected",
                DeviceName = "Front Door",
                EventType = "motion",
                EventPath = "/path/to/event",
                EventLocalLink = "https://unifi.local/event",
                Metadata = metadata
            };

            // Assert
            Assert.Equal("evt-123", summaryEvent.EventId);
            Assert.Equal("cam-001", summaryEvent.Device);
            Assert.Equal(1642077600000, summaryEvent.Timestamp);
            Assert.Equal("events/evt-123.json", summaryEvent.AlarmS3Key);
            Assert.Equal("videos/evt-123.mp4", summaryEvent.VideoS3Key);
            Assert.Equal("https://s3.amazonaws.com/presigned-url", summaryEvent.PresignedVideoUrl);
            Assert.Equal("Motion Detected", summaryEvent.AlarmName);
            Assert.Equal("Front Door", summaryEvent.DeviceName);
            Assert.Equal("motion", summaryEvent.EventType);
            Assert.Equal("/path/to/event", summaryEvent.EventPath);
            Assert.Equal("https://unifi.local/event", summaryEvent.EventLocalLink);
            Assert.Equal(metadata, summaryEvent.Metadata);
        }

        [Fact]
        public void DailySummaryEvent_DefaultConstructor_InitializesWithDefaults()
        {
            // Arrange & Act
            var summaryEvent = new DailySummaryEvent();

            // Assert
            Assert.Equal(string.Empty, summaryEvent.EventId);
            Assert.Equal(string.Empty, summaryEvent.Device);
            Assert.Equal(0, summaryEvent.Timestamp);
            Assert.Equal(string.Empty, summaryEvent.AlarmS3Key);
            Assert.Equal(string.Empty, summaryEvent.VideoS3Key);
            Assert.Equal(string.Empty, summaryEvent.PresignedVideoUrl);
            Assert.Equal(string.Empty, summaryEvent.AlarmName);
            Assert.Equal(string.Empty, summaryEvent.DeviceName);
            Assert.Equal(string.Empty, summaryEvent.EventType);
            Assert.Equal(string.Empty, summaryEvent.EventPath);
            Assert.Equal(string.Empty, summaryEvent.EventLocalLink);
            Assert.NotNull(summaryEvent.Metadata);
            Assert.Empty(summaryEvent.Metadata);
        }

        [Fact]
        public void MissingVideoEvent_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var missingEvent = new MissingVideoEvent
            {
                eventId = "evt-456",
                jsonFile = "events/evt-456.json",
                lastModified = "2025-01-14T12:00:00Z",
                size = 1024
            };

            // Assert
            Assert.Equal("evt-456", missingEvent.eventId);
            Assert.Equal("events/evt-456.json", missingEvent.jsonFile);
            Assert.Equal("2025-01-14T12:00:00Z", missingEvent.lastModified);
            Assert.Equal(1024, missingEvent.size);
        }

        [Fact]
        public void MissingVideoEvent_DefaultConstructor_InitializesWithDefaults()
        {
            // Arrange & Act
            var missingEvent = new MissingVideoEvent();

            // Assert
            Assert.Equal(string.Empty, missingEvent.eventId);
            Assert.Equal(string.Empty, missingEvent.jsonFile);
            Assert.Equal(string.Empty, missingEvent.lastModified);
            Assert.Equal(0, missingEvent.size);
        }

        [Fact]
        public void DailySummary_Properties_CanBeSetAndRetrieved()
        {
            // Arrange
            var metadata = new SummaryMetadata { totalEvents = 5 };
            var eventCounts = new Dictionary<string, int> { { "motion", 3 }, { "person", 2 } };
            var deviceCounts = new Dictionary<string, int> { { "cam-001", 3 }, { "cam-002", 2 } };
            var events = new List<DailySummaryEvent> { new DailySummaryEvent { EventId = "evt-1" } };
            var missingEvents = new List<MissingVideoEvent> { new MissingVideoEvent { eventId = "evt-2" } };
            var dlqCounts = new Dictionary<string, int> { { "alarm-dlq", 0 }, { "summary-dlq", 1 } };

            // Act
            var dailySummary = new DailySummary
            {
                metadata = metadata,
                eventCounts = eventCounts,
                deviceCounts = deviceCounts,
                hourlyCounts = new Dictionary<string, int> { { "12", 3 }, { "13", 2 } },
                events = events,
                missingVideoEvents = missingEvents,
                dlqCounts = dlqCounts
            };

            // Assert
            Assert.Equal(metadata, dailySummary.metadata);
            Assert.Equal(eventCounts, dailySummary.eventCounts);
            Assert.Equal(deviceCounts, dailySummary.deviceCounts);
            Assert.NotNull(dailySummary.hourlyCounts);
            Assert.Equal(2, dailySummary.hourlyCounts.Count);
            Assert.Equal(events, dailySummary.events);
            Assert.Equal(missingEvents, dailySummary.missingVideoEvents);
            Assert.Equal(dlqCounts, dailySummary.dlqCounts);
        }

        [Fact]
        public void DailySummary_DefaultConstructor_InitializesWithDefaults()
        {
            // Arrange & Act
            var dailySummary = new DailySummary();

            // Assert
            Assert.NotNull(dailySummary.metadata);
            Assert.NotNull(dailySummary.eventCounts);
            Assert.Empty(dailySummary.eventCounts);
            Assert.NotNull(dailySummary.deviceCounts);
            Assert.Empty(dailySummary.deviceCounts);
            Assert.NotNull(dailySummary.hourlyCounts);
            Assert.Empty(dailySummary.hourlyCounts);
            Assert.NotNull(dailySummary.events);
            Assert.Empty(dailySummary.events);
            Assert.NotNull(dailySummary.missingVideoEvents);
            Assert.Empty(dailySummary.missingVideoEvents);
            Assert.NotNull(dailySummary.dlqCounts);
            Assert.Empty(dailySummary.dlqCounts);
        }

        #endregion

        #region DeviceMetadata Model Tests

        [Fact]
        public void DeviceMetadata_Properties_CanBeSetAndRetrieved()
        {
            // Arrange & Act
            var deviceMetadata = new DeviceMetadata
            {
                DeviceName = "Front Door Camera",
                DeviceMac = "AA:BB:CC:DD:EE:FF",
                ArchiveButtonX = 100,
                ArchiveButtonY = 200
            };

            // Assert
            Assert.Equal("Front Door Camera", deviceMetadata.DeviceName);
            Assert.Equal("AA:BB:CC:DD:EE:FF", deviceMetadata.DeviceMac);
            Assert.Equal(100, deviceMetadata.ArchiveButtonX);
            Assert.Equal(200, deviceMetadata.ArchiveButtonY);
        }

        [Fact]
        public void DeviceMetadata_WithDifferentValues_StoresCorrectly()
        {
            // Arrange & Act
            var deviceMetadata = new DeviceMetadata
            {
                DeviceName = "Back Yard Camera",
                DeviceMac = "11:22:33:44:55:66",
                ArchiveButtonX = 0,
                ArchiveButtonY = 0
            };

            // Assert
            Assert.Equal("Back Yard Camera", deviceMetadata.DeviceName);
            Assert.Equal("11:22:33:44:55:66", deviceMetadata.DeviceMac);
            Assert.Equal(0, deviceMetadata.ArchiveButtonX);
            Assert.Equal(0, deviceMetadata.ArchiveButtonY);
        }

        [Fact]
        public void DeviceMetadata_WithNegativeCoordinates_HandlesCorrectly()
        {
            // Arrange & Act
            var deviceMetadata = new DeviceMetadata
            {
                DeviceName = "Test Camera",
                DeviceMac = "FF:EE:DD:CC:BB:AA",
                ArchiveButtonX = -50,
                ArchiveButtonY = -100
            };

            // Assert
            Assert.Equal("Test Camera", deviceMetadata.DeviceName);
            Assert.Equal("FF:EE:DD:CC:BB:AA", deviceMetadata.DeviceMac);
            Assert.Equal(-50, deviceMetadata.ArchiveButtonX);
            Assert.Equal(-100, deviceMetadata.ArchiveButtonY);
        }

        [Fact]
        public void DeviceMetadata_WithLargeCoordinates_HandlesCorrectly()
        {
            // Arrange & Act
            var deviceMetadata = new DeviceMetadata
            {
                DeviceName = "High Resolution Camera",
                DeviceMac = "12:34:56:78:90:AB",
                ArchiveButtonX = 1920,
                ArchiveButtonY = 1080
            };

            // Assert
            Assert.Equal("High Resolution Camera", deviceMetadata.DeviceName);
            Assert.Equal("12:34:56:78:90:AB", deviceMetadata.DeviceMac);
            Assert.Equal(1920, deviceMetadata.ArchiveButtonX);
            Assert.Equal(1080, deviceMetadata.ArchiveButtonY);
        }

        [Fact]
        public void DeviceMetadataCollection_Properties_CanBeSetAndRetrieved()
        {
            // Arrange
            var device1 = new DeviceMetadata
            {
                DeviceName = "Camera 1",
                DeviceMac = "AA:BB:CC:DD:EE:FF",
                ArchiveButtonX = 100,
                ArchiveButtonY = 200
            };

            var device2 = new DeviceMetadata
            {
                DeviceName = "Camera 2", 
                DeviceMac = "11:22:33:44:55:66",
                ArchiveButtonX = 300,
                ArchiveButtonY = 400
            };

            var devices = new List<DeviceMetadata> { device1, device2 };

            // Act
            var collection = new DeviceMetadataCollection
            {
                Devices = devices
            };

            // Assert
            Assert.Equal(devices, collection.Devices);
            Assert.Equal(2, collection.Devices.Count);
            Assert.Equal("Camera 1", collection.Devices[0].DeviceName);
            Assert.Equal("Camera 2", collection.Devices[1].DeviceName);
        }

        [Fact]
        public void DeviceMetadataCollection_DefaultConstructor_InitializesWithEmptyList()
        {
            // Arrange & Act
            var collection = new DeviceMetadataCollection();

            // Assert
            Assert.NotNull(collection.Devices);
            Assert.Empty(collection.Devices);
        }

        [Fact]
        public void DeviceMetadataCollection_WithEmptyDevicesList_HandlesCorrectly()
        {
            // Arrange & Act
            var collection = new DeviceMetadataCollection
            {
                Devices = new List<DeviceMetadata>()
            };

            // Assert
            Assert.NotNull(collection.Devices);
            Assert.Empty(collection.Devices);
        }

        [Fact]
        public void DeviceMetadataCollection_WithSingleDevice_HandlesCorrectly()
        {
            // Arrange
            var device = new DeviceMetadata
            {
                DeviceName = "Single Camera",
                DeviceMac = "AB:CD:EF:12:34:56",
                ArchiveButtonX = 500,
                ArchiveButtonY = 600
            };

            // Act
            var collection = new DeviceMetadataCollection
            {
                Devices = new List<DeviceMetadata> { device }
            };

            // Assert
            Assert.NotNull(collection.Devices);
            Assert.Single(collection.Devices);
            Assert.Equal("Single Camera", collection.Devices[0].DeviceName);
            Assert.Equal("AB:CD:EF:12:34:56", collection.Devices[0].DeviceMac);
            Assert.Equal(500, collection.Devices[0].ArchiveButtonX);
            Assert.Equal(600, collection.Devices[0].ArchiveButtonY);
        }

        #endregion
    }
}
