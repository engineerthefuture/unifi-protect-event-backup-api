/************************
 * AppConfiguration Tests
 * AppConfigurationTests.cs
 * Testing configuration management and environment variable access
 * Brent Foster
 * 08-20-2025
 ***********************/

using System;
using Amazon;
using Xunit;
using UnifiWebhookEventReceiver.Configuration;

namespace UnifiWebhookEventReceiverTests
{
    public class AppConfigurationTests
    {
        public AppConfigurationTests()
        {
            // Clean up environment variables before each test
            Environment.SetEnvironmentVariable("StorageBucket", null);
            Environment.SetEnvironmentVariable("DevicePrefix", null);
            Environment.SetEnvironmentVariable("FunctionName", null);
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", null);
            Environment.SetEnvironmentVariable("DownloadDirectory", null);
            Environment.SetEnvironmentVariable("ArchiveButtonX", null);
            Environment.SetEnvironmentVariable("ArchiveButtonY", null);
            Environment.SetEnvironmentVariable("DownloadButtonX", null);
            Environment.SetEnvironmentVariable("DownloadButtonY", null);
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", null);
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", null);
            Environment.SetEnvironmentVariable("AWS_REGION", null);
        }

        [Fact]
        public void Constants_HaveExpectedValues()
        {
            // Assert
            Assert.Equal("An internal server error has occured: ", AppConfiguration.ERROR_MESSAGE_500);
            Assert.Equal("Your request is malformed or invalid: ", AppConfiguration.ERROR_MESSAGE_400);
            Assert.Equal("Route not found: ", AppConfiguration.ERROR_MESSAGE_404);
            Assert.Equal("No action taken on request.", AppConfiguration.MESSAGE_202);
            Assert.Equal("you must have a valid body object in your request", AppConfiguration.ERROR_GENERAL);
            Assert.Equal("you must have triggers in your payload", AppConfiguration.ERROR_TRIGGERS);
            Assert.Equal("please provide a valid route", AppConfiguration.ERROR_INVALID_ROUTE);
            Assert.Equal("alarmevent", AppConfiguration.ROUTE_ALARM);
            Assert.Equal("latestvideo", AppConfiguration.ROUTE_LATEST_VIDEO);
        }

        [Fact]
        public void AlarmBucketName_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");

            // Act
            var result = AppConfiguration.AlarmBucketName;

            // Assert
            Assert.Equal("test-bucket", result);
        }

        [Fact]
        public void AlarmBucketName_WithoutEnvironmentVariable_ReturnsNull()
        {
            // Arrange - Ensure no environment variable is set
            var originalValue = Environment.GetEnvironmentVariable("AlarmBucketName");
            Environment.SetEnvironmentVariable("AlarmBucketName", null);

            try
            {
                // Act
                var result = AppConfiguration.AlarmBucketName;

                // Assert
                Assert.Null(result);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable("AlarmBucketName", originalValue);
            }
        }

        [Fact]
        public void DevicePrefix_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DevicePrefix", "DeviceMac");

            // Act
            var result = AppConfiguration.DevicePrefix;

            // Assert
            Assert.Equal("DeviceMac", result);
        }

        [Fact]
        public void DevicePrefix_WithoutEnvironmentVariable_ReturnsNull()
        {
            // Act
            var result = AppConfiguration.DevicePrefix;

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void FunctionName_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("FunctionName", "TestFunction");

            // Act
            var result = AppConfiguration.FunctionName;

            // Assert
            Assert.Equal("TestFunction", result);
        }

        [Fact]
        public void FunctionName_WithoutEnvironmentVariable_ReturnsNull()
        {
            // Act
            var result = AppConfiguration.FunctionName;

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void UnifiCredentialsSecretArn_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            var testArn = "arn:aws:secretsmanager:us-east-1:123456789012:secret:test-secret";
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", testArn);

            // Act
            var result = AppConfiguration.UnifiCredentialsSecretArn;

            // Assert
            Assert.Equal(testArn, result);
        }

        [Fact]
        public void UnifiCredentialsSecretArn_WithoutEnvironmentVariable_ReturnsNull()
        {
            // Act
            var result = AppConfiguration.UnifiCredentialsSecretArn;

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void DownloadDirectory_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DownloadDirectory", "/custom/path");

            // Act
            var result = AppConfiguration.DownloadDirectory;

            // Assert
            Assert.Equal("/custom/path", result);
        }

        [Fact]
        public void DownloadDirectory_WithoutEnvironmentVariable_ReturnsDefaultTmp()
        {
            // Act
            var result = AppConfiguration.DownloadDirectory;

            // Assert
            Assert.Equal("/tmp", result);
        }

        [Fact]
        public void ArchiveButtonX_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("ArchiveButtonX", "1500");

            // Act
            var result = AppConfiguration.ArchiveButtonX;

            // Assert
            Assert.Equal(1500, result);
        }

        [Fact]
        public void ArchiveButtonX_WithInvalidEnvironmentVariable_ReturnsDefault()
        {
            // Arrange
            Environment.SetEnvironmentVariable("ArchiveButtonX", "invalid");

            // Act
            var result = AppConfiguration.ArchiveButtonX;

            // Assert
            Assert.Equal(1274, result); // Default value
        }

        [Fact]
        public void ArchiveButtonX_WithoutEnvironmentVariable_ReturnsDefault()
        {
            // Act
            var result = AppConfiguration.ArchiveButtonX;

            // Assert
            Assert.Equal(1274, result);
        }

        [Fact]
        public void ArchiveButtonY_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("ArchiveButtonY", "300");

            // Act
            var result = AppConfiguration.ArchiveButtonY;

            // Assert
            Assert.Equal(300, result);
        }

        [Fact]
        public void ArchiveButtonY_WithInvalidEnvironmentVariable_ReturnsDefault()
        {
            // Arrange
            Environment.SetEnvironmentVariable("ArchiveButtonY", "not-a-number");

            // Act
            var result = AppConfiguration.ArchiveButtonY;

            // Assert
            Assert.Equal(257, result); // Default value
        }

        [Fact]
        public void ArchiveButtonY_WithoutEnvironmentVariable_ReturnsDefault()
        {
            // Act
            var result = AppConfiguration.ArchiveButtonY;

            // Assert
            Assert.Equal(257, result);
        }

        [Fact]
        public void DownloadButtonX_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DownloadButtonX", "1200");

            // Act
            var result = AppConfiguration.DownloadButtonX;

            // Assert
            Assert.Equal(1200, result);
        }

        [Fact]
        public void DownloadButtonX_WithInvalidEnvironmentVariable_ReturnsDefault()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DownloadButtonX", "xyz");

            // Act
            var result = AppConfiguration.DownloadButtonX;

            // Assert
            Assert.Equal(1095, result); // Default value
        }

        [Fact]
        public void DownloadButtonY_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DownloadButtonY", "350");

            // Act
            var result = AppConfiguration.DownloadButtonY;

            // Assert
            Assert.Equal(350, result);
        }

        [Fact]
        public void DownloadButtonY_WithInvalidEnvironmentVariable_ReturnsDefault()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DownloadButtonY", "abc");

            // Act
            var result = AppConfiguration.DownloadButtonY;

            // Assert
            Assert.Equal(275, result); // Default value
        }

        [Fact]
        public void ProcessingDelaySeconds_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", "300");

            // Act
            var result = AppConfiguration.ProcessingDelaySeconds;

            // Assert
            Assert.Equal(300, result);
        }

        [Fact]
        public void ProcessingDelaySeconds_WithInvalidEnvironmentVariable_ReturnsDefault()
        {
            // Arrange
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", "invalid");

            // Act
            var result = AppConfiguration.ProcessingDelaySeconds;

            // Assert
            Assert.Equal(120, result); // Default value
        }

        [Fact]
        public void ProcessingDelaySeconds_WithoutEnvironmentVariable_ReturnsDefault()
        {
            // Act
            var result = AppConfiguration.ProcessingDelaySeconds;

            // Assert
            Assert.Equal(120, result);
        }

        [Fact]
        public void AlarmProcessingQueueUrl_WithEnvironmentVariable_ReturnsValue()
        {
            // Arrange
            var testUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue";
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", testUrl);

            // Act
            var result = AppConfiguration.AlarmProcessingQueueUrl;

            // Assert
            Assert.Equal(testUrl, result);
        }

        [Fact]
        public void AlarmProcessingQueueUrl_WithoutEnvironmentVariable_ReturnsNull()
        {
            // Act
            var result = AppConfiguration.AlarmProcessingQueueUrl;

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void AwsRegion_WithEnvironmentVariable_ReturnsRegionEndpoint()
        {
            // Arrange
            var originalValue = Environment.GetEnvironmentVariable("AWS_REGION");
            Environment.SetEnvironmentVariable("AWS_REGION", "us-west-2");

            try
            {
                // Act
                var result = AppConfiguration.AwsRegion;

                // Assert - AppConfiguration.AwsRegion is hardcoded to USEast1
                Assert.Equal(RegionEndpoint.USEast1, result);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable("AWS_REGION", originalValue);
            }
        }

        [Fact]
        public void AwsRegion_WithInvalidEnvironmentVariable_ReturnsUSEast1()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AWS_REGION", "invalid-region");

            // Act
            var result = AppConfiguration.AwsRegion;

            // Assert
            Assert.Equal(RegionEndpoint.USEast1, result); // Default fallback
        }

        [Fact]
        public void AwsRegion_WithoutEnvironmentVariable_ReturnsUSEast1()
        {
            // Act
            var result = AppConfiguration.AwsRegion;

            // Assert
            Assert.Equal(RegionEndpoint.USEast1, result);
        }

        [Fact]
        public void GetDeviceName_WithValidMapping_ReturnsDeviceName()
        {
            // Arrange
            var macAddress = "28704E113F64";
            var deviceName = "Front Door Camera";
            var originalPrefix = Environment.GetEnvironmentVariable("DevicePrefix");
            var originalDevice = Environment.GetEnvironmentVariable($"DeviceMac{macAddress}");
            
            try
            {
                Environment.SetEnvironmentVariable("DevicePrefix", "DeviceMac");
                Environment.SetEnvironmentVariable($"DeviceMac{macAddress}", deviceName);

                // Act
                var result = AppConfiguration.GetDeviceName(macAddress);

                // Assert
                Assert.Equal(deviceName, result);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable("DevicePrefix", originalPrefix);
                Environment.SetEnvironmentVariable($"DeviceMac{macAddress}", originalDevice);
            }
        }

        [Fact]
        public void GetDeviceName_WithoutMapping_ReturnsMacAddress()
        {
            // Arrange
            var macAddress = "UNKNOWN123456";
            Environment.SetEnvironmentVariable("DevicePrefix", "DeviceMac");

            // Act
            var result = AppConfiguration.GetDeviceName(macAddress);

            // Assert
            Assert.Equal(macAddress, result);
        }

        [Fact]
        public void GetDeviceName_WithoutDevicePrefix_ReturnsMacAddress()
        {
            // Arrange
            var macAddress = "28704E113F64";

            // Act
            var result = AppConfiguration.GetDeviceName(macAddress);

            // Assert
            Assert.Equal(macAddress, result);
        }

        [Fact]
        public void GetDeviceName_WithEmptyMacAddress_ReturnsEmpty()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DevicePrefix", "DeviceMac");

            // Act
            var result = AppConfiguration.GetDeviceName("");

            // Assert
            Assert.Equal("", result);
        }

        [Fact]
        public void GetDeviceName_WithNullMacAddress_ReturnsNull()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DevicePrefix", "DeviceMac");

            // Act
            var result = AppConfiguration.GetDeviceName(null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Configuration_PropertyAccessMultipleTimes_ReturnsSameValue()
        {
            // Arrange
            Environment.SetEnvironmentVariable("FunctionName", "TestFunction");

            // Act
            var result1 = AppConfiguration.FunctionName;
            var result2 = AppConfiguration.FunctionName;

            // Assert
            Assert.Equal(result1, result2);
            Assert.Equal("TestFunction", result1);
        }

        [Fact]
        public void Configuration_WithExtremelyLongEnvironmentVariable_HandlesCorrectly()
        {
            // Arrange
            var longValue = new string('x', 10000);
            Environment.SetEnvironmentVariable("FunctionName", longValue);

            // Act
            var result = AppConfiguration.FunctionName;

            // Assert
            Assert.Equal(longValue, result);
        }

        [Fact]
        public void Configuration_WithSpecialCharactersInEnvironmentVariable_HandlesCorrectly()
        {
            // Arrange
            var specialValue = "test-function_name.with@special#chars$";
            Environment.SetEnvironmentVariable("FunctionName", specialValue);

            // Act
            var result = AppConfiguration.FunctionName;

            // Assert
            Assert.Equal(specialValue, result);
        }
    }
}
