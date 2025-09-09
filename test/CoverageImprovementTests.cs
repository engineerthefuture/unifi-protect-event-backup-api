using System;
using System.Collections.Generic;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Models;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    /// <summary>
    /// Tests for coverage improvement focusing on model validation and edge cases.
    /// </summary>
    public class CoverageImprovementTests
    {
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
    }
}
