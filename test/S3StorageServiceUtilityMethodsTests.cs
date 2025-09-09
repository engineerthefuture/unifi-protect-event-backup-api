using System;
using System.Reflection;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    /// <summary>
    /// Tests for S3StorageService utility methods to improve coverage.
    /// Tests filename parsing and other utility functions.
    /// </summary>
    public class S3StorageServiceUtilityMethodsTests
    {
        [Fact]
        public void ExtractTimestampFromFileName_WithValidTimestamp_ReturnsTimestamp()
        {
            // Arrange
            string fileName = "video_1672531200000.mp4";
            
            // Act
            long result = InvokeExtractTimestampFromFileName(fileName);
            
            // Assert
            Assert.Equal(1672531200000, result);
        }

        [Fact]
        public void ExtractTimestampFromFileName_WithValidS3Key_ReturnsTimestamp()
        {
            // Arrange
            string s3Key = "2023-01-01/video_1672531200000.mp4";
            
            // Act
            long result = InvokeExtractTimestampFromFileName(s3Key);
            
            // Assert
            Assert.Equal(1672531200000, result);
        }

        [Fact]
        public void ExtractTimestampFromFileName_WithInvalidFormat_ReturnsZero()
        {
            // Arrange
            string fileName = "invalid_filename.mp4";
            
            // Act
            long result = InvokeExtractTimestampFromFileName(fileName);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void ExtractTimestampFromFileName_WithNoUnderscore_ReturnsZero()
        {
            // Arrange
            string fileName = "filename.mp4";
            
            // Act
            long result = InvokeExtractTimestampFromFileName(fileName);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void ExtractTimestampFromFileName_WithNoDot_ReturnsZero()
        {
            // Arrange
            string fileName = "video_1672531200000";
            
            // Act
            long result = InvokeExtractTimestampFromFileName(fileName);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void ExtractTimestampFromFileName_WithNonNumericTimestamp_ReturnsZero()
        {
            // Arrange
            string fileName = "video_abc123def.mp4";
            
            // Act
            long result = InvokeExtractTimestampFromFileName(fileName);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void ExtractTimestampFromFileName_WithNegativeTimestamp_ReturnsZero()
        {
            // Arrange
            string fileName = "video_-1672531200000.mp4";
            
            // Act
            long result = InvokeExtractTimestampFromFileName(fileName);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void ExtractTimestampFromEventFileName_WithValidEventFile_ReturnsTimestamp()
        {
            // Arrange
            string fileName = "alarm_event123_1672531200000.json";
            
            // Act
            long result = InvokeExtractTimestampFromEventFileName(fileName);
            
            // Assert
            Assert.Equal(1672531200000, result);
        }

        [Fact]
        public void ExtractTimestampFromEventFileName_WithValidS3Key_ReturnsTimestamp()
        {
            // Arrange
            string s3Key = "2023-01-01/alarm_event123_1672531200000.json";
            
            // Act
            long result = InvokeExtractTimestampFromEventFileName(s3Key);
            
            // Assert
            Assert.Equal(1672531200000, result);
        }

        [Fact]
        public void ExtractTimestampFromEventFileName_WithInvalidFormat_ReturnsZero()
        {
            // Arrange
            string fileName = "invalid_format.json";
            
            // Act
            long result = InvokeExtractTimestampFromEventFileName(fileName);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void ExtractTimestampFromEventFileName_WithFewerThanThreeParts_ReturnsZero()
        {
            // Arrange
            string fileName = "alarm_event123.json"; // Only 2 parts
            
            // Act
            long result = InvokeExtractTimestampFromEventFileName(fileName);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void ExtractTimestampFromEventFileName_WithNonNumericTimestamp_ReturnsZero()
        {
            // Arrange
            string fileName = "alarm_event123_timestamp.json";
            
            // Act
            long result = InvokeExtractTimestampFromEventFileName(fileName);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void ExtractTimestampFromEventFileName_WithNegativeTimestamp_ReturnsZero()
        {
            // Arrange
            string fileName = "alarm_event123_-1672531200000.json";
            
            // Act
            long result = InvokeExtractTimestampFromEventFileName(fileName);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void CloneAlarmWithoutSourcesAndConditions_WithValidAlarm_ReturnsClone()
        {
            // Arrange
            var originalAlarm = new UnifiWebhookEventReceiver.Alarm
            {
                name = "Test Alarm",
                timestamp = 1672531200000,
                eventPath = "/test/path",
                eventLocalLink = "local://link",
                thumbnail = "thumb_data",
                sources = new System.Collections.Generic.List<UnifiWebhookEventReceiver.Source>
                {
                    new UnifiWebhookEventReceiver.Source 
                    { 
                        device = "AA:BB:CC:DD:EE:FF",
                        type = "camera"
                    }
                },
                conditions = new System.Collections.Generic.List<UnifiWebhookEventReceiver.Condition>
                {
                    new UnifiWebhookEventReceiver.Condition { }
                },
                triggers = new System.Collections.Generic.List<UnifiWebhookEventReceiver.Trigger>
                {
                    new UnifiWebhookEventReceiver.Trigger 
                    { 
                        eventId = "trigger1",
                        key = "motion",
                        device = "AA:BB:CC:DD:EE:FF"
                    }
                }
            };

            // Act
            var result = InvokeCloneAlarmWithoutSourcesAndConditions(originalAlarm);

            // Assert
            Assert.NotNull(result);
            Assert.NotSame(originalAlarm, result);
            Assert.Equal(originalAlarm.name, result.name);
            Assert.Equal(originalAlarm.timestamp, result.timestamp);
            Assert.Equal(originalAlarm.eventPath, result.eventPath);
            Assert.Equal(originalAlarm.eventLocalLink, result.eventLocalLink);
            Assert.Equal(originalAlarm.thumbnail, result.thumbnail);
            Assert.Null(result.sources);
            Assert.Null(result.conditions);
            Assert.NotNull(result.triggers);
            Assert.Equal(originalAlarm.triggers.Count, result.triggers.Count);
        }

        [Fact]
        public void CloneAlarmWithoutSourcesAndConditions_WithNullAlarm_ReturnsNull()
        {
            // Arrange
            UnifiWebhookEventReceiver.Alarm originalAlarm = null;

            // Act
            var result = InvokeCloneAlarmWithoutSourcesAndConditions(originalAlarm);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void CloneAlarmWithoutSourcesAndConditions_WithMinimalAlarm_ReturnsClone()
        {
            // Arrange
            var originalAlarm = new UnifiWebhookEventReceiver.Alarm
            {
                name = "Minimal Alarm",
                timestamp = 1672531200000,
                triggers = new System.Collections.Generic.List<UnifiWebhookEventReceiver.Trigger>()
                // Most properties null/empty
            };

            // Act
            var result = InvokeCloneAlarmWithoutSourcesAndConditions(originalAlarm);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(originalAlarm.name, result.name);
            Assert.Equal(originalAlarm.timestamp, result.timestamp);
            Assert.Null(result.sources);
            Assert.Null(result.conditions);
        }

        // Helper methods to invoke private static methods via reflection
        private static long InvokeExtractTimestampFromFileName(string s3Key)
        {
            var method = typeof(S3StorageService).GetMethod("ExtractTimestampFromFileName", 
                BindingFlags.NonPublic | BindingFlags.Static);
            
            return (long)method?.Invoke(null, new object[] { s3Key });
        }

        private static long InvokeExtractTimestampFromEventFileName(string s3Key)
        {
            var method = typeof(S3StorageService).GetMethod("ExtractTimestampFromEventFileName", 
                BindingFlags.NonPublic | BindingFlags.Static);
            
            return (long)method?.Invoke(null, new object[] { s3Key });
        }

        private static UnifiWebhookEventReceiver.Alarm InvokeCloneAlarmWithoutSourcesAndConditions(
            UnifiWebhookEventReceiver.Alarm alarm)
        {
            var method = typeof(S3StorageService).GetMethod("CloneAlarmWithoutSourcesAndConditions", 
                BindingFlags.NonPublic | BindingFlags.Static);
            
            return (UnifiWebhookEventReceiver.Alarm)method?.Invoke(null, new object[] { alarm });
        }
    }
}
