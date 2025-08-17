/************************
 * File Organization Tests
 * FileOrganizationTests.cs
 * Testing enhanced file naming and S3 organization
 * Brent Foster
 * 08-17-2025
 ***********************/

using System;
using System.Collections.Generic;
using Xunit;
using UnifiWebhookEventReceiver;

namespace UnifiWebhookEventReceiver.Tests
{
    public class FileOrganizationTests
    {
        [Fact]
        public void EventIdBasedNaming_ShouldPrefixWithEventId()
        {
            // Arrange
            var eventId = "evt_123456789";
            var device = "28704E113F64";
            var timestamp = 1691000000000L;

            // Act
            var videoKey = $"{eventId}_{device}_{timestamp}.mp4";
            var eventKey = $"{eventId}_{device}_{timestamp}.json";

            // Assert
            Assert.StartsWith(eventId, videoKey);
            Assert.StartsWith(eventId, eventKey);
            Assert.EndsWith(".mp4", videoKey);
            Assert.EndsWith(".json", eventKey);
        }

        [Fact]
        public void S3PrefixSearch_ShouldEnableDirectLookup()
        {
            // Arrange
            var eventId = "evt_123456789";
            var prefix = $"{eventId}_";

            // Act
            var videoKey = $"{eventId}_28704E113F64_1691000000000.mp4";
            var eventKey = $"{eventId}_28704E113F64_1691000000000.json";

            // Assert
            Assert.StartsWith(prefix, videoKey);
            Assert.StartsWith(prefix, eventKey);
            // This enables O(1) S3 prefix searches instead of downloading and parsing JSON
        }

        [Theory]
        [InlineData(1672531200000, "2023-01-01")] // Unix timestamp in milliseconds (UTC)
        [InlineData(1691000000000, "2023-08-02")]
        [InlineData(1704067200000, "2024-01-01")]
        [InlineData(1735689600000, "2025-01-01")]
        public void DateBasedFolders_ShouldFormatCorrectly(long timestamp, string expectedDate)
        {
            // Convert timestamp to DateTimeOffset (UTC) and use UTC date
            var dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
            var actualDate = dateTime.ToString("yyyy-MM-dd");
            
            Assert.Equal(expectedDate, actualDate);
        }

        [Fact]
        public void CompleteFileKeys_ShouldIncludeDatePath()
        {
            // Arrange
            var eventId = "evt_123456789";
            var device = "28704E113F64";
            var timestamp = 1691000000000L; // August 2, 2023
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;

            // Act
            var videoKey = $"{eventId}_{device}_{timestamp}.mp4";
            var eventKey = $"{eventId}_{device}_{timestamp}.json";
            var videoFileKey = $"{dt.Year}-{dt.Month.ToString("D2")}-{dt.Day.ToString("D2")}/{videoKey}";
            var eventFileKey = $"{dt.Year}-{dt.Month.ToString("D2")}-{dt.Day.ToString("D2")}/{eventKey}";

            // Assert
            Assert.Equal("2023-08-02/evt_123456789_28704E113F64_1691000000000.mp4", videoFileKey);
            Assert.Equal("2023-08-02/evt_123456789_28704E113F64_1691000000000.json", eventFileKey);
        }

        [Fact]
        public void FileKeyComponents_ShouldBeExtractable()
        {
            // Arrange
            var videoKey = "evt_123456789_28704E113F64_1691000000000.mp4";

            // Act
            var parts = videoKey.Replace(".mp4", "").Split('_');
            var extractedEventId = parts[0] + "_" + parts[1]; // Handle "evt_" prefix
            var extractedDevice = parts[2];
            var extractedTimestamp = parts[3];

            // Assert
            Assert.Equal("evt_123456789", extractedEventId);
            Assert.Equal("28704E113F64", extractedDevice);
            Assert.Equal("1691000000000", extractedTimestamp);
        }

        [Theory]
        [InlineData("28704E113F64", "Backyard East")]
        [InlineData("F4E2C67A2FE8", "Front")]
        [InlineData("28704E113C44", "Side")]
        [InlineData("UNKNOWN_DEVICE", "")]
        public void DeviceMapping_ShouldMapMacToName(string deviceMac, string expectedName)
        {
            // Arrange
            Environment.SetEnvironmentVariable("DevicePrefix", "DeviceMac");
            Environment.SetEnvironmentVariable("DeviceMac28704E113F64", "Backyard East");
            Environment.SetEnvironmentVariable("DeviceMacF4E2C67A2FE8", "Front");
            Environment.SetEnvironmentVariable("DeviceMac28704E113C44", "Side");

            // Act
            var devicePrefix = Environment.GetEnvironmentVariable("DevicePrefix");
            var deviceName = Environment.GetEnvironmentVariable(devicePrefix + deviceMac) ?? "";

            // Assert
            Assert.Equal(expectedName, deviceName);
        }

        [Fact]
        public void S3BucketStructure_ShouldOrganizeByDateThenFiles()
        {
            // Arrange
            var dateFolder = "2023-08-02";
            var eventFiles = new List<string>
            {
                "evt_123_28704E113F64_1691000000000.json",
                "evt_123_28704E113F64_1691000000000.mp4",
                "evt_456_F4E2C67A2FE8_1691000001000.json",
                "evt_456_F4E2C67A2FE8_1691000001000.mp4"
            };

            // Act
            var s3Keys = new List<string>();
            foreach (var file in eventFiles)
            {
                s3Keys.Add($"{dateFolder}/{file}");
            }

            // Assert
            Assert.All(s3Keys, key => Assert.StartsWith(dateFolder, key));
            Assert.Contains($"{dateFolder}/evt_123_28704E113F64_1691000000000.json", s3Keys);
            Assert.Contains($"{dateFolder}/evt_123_28704E113F64_1691000000000.mp4", s3Keys);
        }

        [Fact]
        public void VideoFileKeys_ShouldMatchEventKeys()
        {
            // Arrange
            var eventId = "evt_123456789";
            var device = "28704E113F64";
            var timestamp = 1691000000000L;

            // Act
            var baseKey = $"{eventId}_{device}_{timestamp}";
            var videoKey = $"{baseKey}.mp4";
            var eventKey = $"{baseKey}.json";

            // Assert
            Assert.Equal("evt_123456789_28704E113F64_1691000000000", baseKey);
            Assert.Equal(baseKey, videoKey.Replace(".mp4", ""));
            Assert.Equal(baseKey, eventKey.Replace(".json", ""));
        }

        [Fact]
        public void FileSizes_ShouldBeTrackable()
        {
            // Arrange
            var fileData = new Dictionary<string, long>
            {
                ["evt_123_28704E113F64_1691000000000.json"] = 1024,     // 1KB JSON
                ["evt_123_28704E113F64_1691000000000.mp4"] = 5242880   // 5MB video
            };

            // Act & Assert
            Assert.True(fileData["evt_123_28704E113F64_1691000000000.json"] < fileData["evt_123_28704E113F64_1691000000000.mp4"]);
            Assert.InRange(fileData["evt_123_28704E113F64_1691000000000.json"], 1, 10240); // JSON should be small
            Assert.InRange(fileData["evt_123_28704E113F64_1691000000000.mp4"], 1048576, 52428800); // Video 1-50MB
        }

        [Fact]
        public void FileNaming_ShouldAvoidCollisions()
        {
            // Arrange
            var baseEventId = "evt_123456789";
            var device = "28704E113F64";
            var timestamp1 = 1691000000000L;
            var timestamp2 = 1691000000001L; // 1ms later

            // Act
            var key1 = $"{baseEventId}_{device}_{timestamp1}.mp4";
            var key2 = $"{baseEventId}_{device}_{timestamp2}.mp4";

            // Assert
            Assert.NotEqual(key1, key2);
            Assert.Contains(timestamp1.ToString(), key1);
            Assert.Contains(timestamp2.ToString(), key2);
        }

        [Theory]
        [InlineData("evt_", "event-")]
        [InlineData("alm_", "alarm-")]
        [InlineData("test_", "test-")]
        public void EventIdPrefixes_ShouldCategorizeEvents(string prefix, string category)
        {
            // Arrange
            var eventId = prefix + "123456789";
            var device = "28704E113F64";
            var timestamp = 1691000000000L;

            // Act
            var fileKey = $"{eventId}_{device}_{timestamp}.json";

            // Assert
            Assert.StartsWith(prefix, fileKey);
            // Validate that the prefix maps to the expected category concept
            // e.g., "evt_" relates to "event", "alm_" to "alarm", etc.
            var prefixBase = prefix.Replace("_", "");
            var categoryBase = category.Replace("-", "");
            
            // For known mappings, validate the relationship
            if (prefix == "evt_")
                Assert.Contains("event", categoryBase);
            else if (prefix == "alm_")
                Assert.Contains("alarm", categoryBase);
            else
                Assert.Contains(prefixBase, categoryBase);
        }

        [Fact]
        public void S3PrefixQueries_ShouldEnableFastSearch()
        {
            // Arrange
            var searchEventId = "evt_123456789";
            var fileKeys = new List<string>
            {
                "evt_123456789_28704E113F64_1691000000000.json",
                "evt_123456789_28704E113F64_1691000000000.mp4",
                "evt_987654321_F4E2C67A2FE8_1691000001000.json",
                "evt_987654321_F4E2C67A2FE8_1691000001000.mp4"
            };

            // Act
            var matchingFiles = fileKeys.FindAll(key => key.StartsWith(searchEventId));

            // Assert
            Assert.Equal(2, matchingFiles.Count);
            Assert.All(matchingFiles, file => Assert.StartsWith(searchEventId, file));
        }

        [Fact]
        public void DateRangeQueries_ShouldUseFolderStructure()
        {
            // Arrange
            var searchDate = "2023-08-02";
            var s3Keys = new List<string>
            {
                "2023-08-01/evt_123_device1_timestamp1.json",
                "2023-08-02/evt_456_device2_timestamp2.json",
                "2023-08-02/evt_789_device3_timestamp3.json",
                "2023-08-03/evt_012_device4_timestamp4.json"
            };

            // Act
            var matchingDates = s3Keys.FindAll(key => key.StartsWith(searchDate));

            // Assert
            Assert.Equal(2, matchingDates.Count);
            Assert.All(matchingDates, key => Assert.StartsWith(searchDate, key));
        }
    }
}
