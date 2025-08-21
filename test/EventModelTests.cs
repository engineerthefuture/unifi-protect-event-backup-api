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
    }
}
