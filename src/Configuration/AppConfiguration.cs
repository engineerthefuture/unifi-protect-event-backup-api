

/************************
 * Unifi Webhook Event Receiver
 * AppConfiguration.cs
 * 
 * Centralized configuration management for environment variables and AWS settings.
 * Provides strongly-typed access to all application configuration values.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using Amazon;
using System.Text.Json;
using UnifiWebhookEventReceiver.Models;

namespace UnifiWebhookEventReceiver.Configuration
{
    /// <summary>
    /// Centralized configuration management class that provides strongly-typed access
    /// to environment variables and AWS configuration settings.
    /// </summary>
    public static class AppConfiguration
    {
        /// <summary>Default X coordinate for archive button click.</summary>
        public const int DEFAULT_ARCHIVE_BUTTON_X = 1205;

        /// <summary>Default Y coordinate for archive button click.</summary>
        public const int DEFAULT_ARCHIVE_BUTTON_Y = 240;
        /// <summary>Maximum retention period (in days) for event data, matching S3 lifecycle rule.</summary>
        public static int MaxRetentionDays => int.TryParse(Environment.GetEnvironmentVariable("MaxRetentionDays"), out var days) ? days : 30;
        #region Constants

        /// <summary>Error message template for 500 Internal Server Error responses</summary>
        public const string ERROR_MESSAGE_500 = "An internal server error has occured: ";

        /// <summary>Error message template for 400 Bad Request responses</summary>
        public const string ERROR_MESSAGE_400 = "Your request is malformed or invalid: ";

        /// <summary>Error message template for 404 Not Found responses</summary>
        public const string ERROR_MESSAGE_404 = "Route not found: ";

        /// <summary>Success message for requests that don't require action</summary>
        public const string MESSAGE_202 = "No action taken on request.";

        /// <summary>Error message for requests missing required body content</summary>
        public const string ERROR_GENERAL = "you must have a valid body object in your request";

        /// <summary>Error message for alarm events missing triggers</summary>
        public const string ERROR_TRIGGERS = "you must have triggers in your payload";

        /// <summary>Error message for invalid API routes</summary>
        public const string ERROR_INVALID_ROUTE = "please provide a valid route";

        /// <summary>API route for alarm event webhook processing</summary>
        public const string ROUTE_ALARM = "alarmevent";

        /// <summary>API route for latest video download</summary>
        public const string ROUTE_LATEST_VIDEO = "latestvideo";

    /// <summary>API route for event summary</summary>
    public const string ROUTE_SUMMARY = "summary";

        /// <summary>Event source identifier for AWS scheduled events</summary>
        public const string SOURCE_EVENT_TRIGGER = "aws.events";

        #endregion

        #region Environment Variables

        /// <summary>S3 bucket name for storing alarm event data</summary>
        public static string? AlarmBucketName => Environment.GetEnvironmentVariable("StorageBucket");

        /// <summary>Lambda function name for logging and identification</summary>
        public static string? FunctionName => Environment.GetEnvironmentVariable("FunctionName");

        /// <summary>AWS Secrets Manager ARN containing Unifi Protect credentials</summary>
        public static string? UnifiCredentialsSecretArn => Environment.GetEnvironmentVariable("UnifiCredentialsSecretArn");

        /// <summary>Download directory for temporary video files. Defaults to /tmp for Lambda compatibility.</summary>
        public static string DownloadDirectory => Environment.GetEnvironmentVariable("DownloadDirectory") ?? "/tmp";


        /// <summary>SQS queue URL for delayed alarm processing</summary>
        public static string? AlarmProcessingQueueUrl => Environment.GetEnvironmentVariable("AlarmProcessingQueueUrl");

        /// <summary>SQS dead letter queue URL for failed alarm processing</summary>
        public static string? AlarmProcessingDlqUrl => Environment.GetEnvironmentVariable("AlarmProcessingDlqUrl");

    /// <summary>SQS queue URL for summary event processing</summary>
    public static string? SummaryEventQueueUrl => Environment.GetEnvironmentVariable("SummaryEventQueueUrl");

        /// <summary>Delay in seconds before processing alarm events (defaults to 2 minutes)</summary>
        public static int ProcessingDelaySeconds => int.TryParse(Environment.GetEnvironmentVariable("ProcessingDelaySeconds"), out var delay) ? delay : 120;

        /// <summary>Support email address for failure notifications</summary>
        public static string? SupportEmail => Environment.GetEnvironmentVariable("SupportEmail");

        /// <summary>Deployed environment prefix (dev, prod, staging)</summary>
        public static string? DeployedEnv => Environment.GetEnvironmentVariable("DeployedEnv");

        /// <summary>Git commit SHA for the current build</summary>
        public static string? BuildSha => Environment.GetEnvironmentVariable("BuildSha");

        /// <summary>Timestamp when the current build was created</summary>
        public static string? BuildTimestamp => Environment.GetEnvironmentVariable("BuildTimestamp");

        #endregion

        #region AWS Configuration

        /// <summary>AWS region for S3 operations</summary>
        public static RegionEndpoint AwsRegion => RegionEndpoint.USEast1;

        #endregion

        #region Device Metadata

        private static DeviceMetadataCollection? _deviceMetadata;
        private static readonly object _deviceMetadataLock = new object();
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Gets the parsed device metadata from the DeviceMetadata environment variable.
        /// </summary>
        public static DeviceMetadataCollection DeviceMetadata
        {
            get
            {
                if (_deviceMetadata == null)
                {
                    lock (_deviceMetadataLock)
                    {
                        if (_deviceMetadata == null)
                        {
                            _deviceMetadata = LoadDeviceMetadata();
                        }
                    }
                }
                return _deviceMetadata;
            }
        }

        /// <summary>
        /// Loads and parses device metadata from environment variable.
        /// </summary>
        /// <returns>Device metadata collection or empty collection if parsing fails</returns>
        private static DeviceMetadataCollection LoadDeviceMetadata()
        {
            try
            {
                var deviceMetadataJson = Environment.GetEnvironmentVariable("DeviceMetadata");
                if (string.IsNullOrEmpty(deviceMetadataJson))
                {
                    return new DeviceMetadataCollection();
                }

                var metadata = JsonSerializer.Deserialize<DeviceMetadataCollection>(deviceMetadataJson, _jsonOptions);

                return metadata ?? new DeviceMetadataCollection();
            }
            catch
            {
                // Return empty collection if JSON parsing fails
                return new DeviceMetadataCollection();
            }
        }

        /// <summary>
        /// Gets human-readable device name from MAC address using DeviceMetadata configuration.
        /// Falls back to legacy environment variables if DeviceMetadata is not available.
        /// </summary>
        /// <param name="deviceMac">MAC address of the device</param>
        /// <returns>Human-readable device name or original MAC if not found</returns>
        public static string GetDeviceName(string deviceMac)
        {
            if (string.IsNullOrEmpty(deviceMac))
            {
                return deviceMac;
            }

            // First, try to get device name from DeviceMetadata JSON
            if (DeviceMetadata?.Devices != null)
            {
                var device = DeviceMetadata.Devices.Find(d => 
                    string.Equals(d.DeviceMac, deviceMac, StringComparison.OrdinalIgnoreCase));

                if (device != null)
                {
                    return device.DeviceName;
                }
            }

            // Fallback to legacy environment variables for backward compatibility
            string prefix = Environment.GetEnvironmentVariable("DevicePrefix") ?? string.Empty;
            if (!string.IsNullOrEmpty(prefix))
            {
                string? deviceName = Environment.GetEnvironmentVariable($"{prefix}{deviceMac}");
                if (!string.IsNullOrEmpty(deviceName))
                {
                    return deviceName;
                }
            }

            // Return MAC address if no mapping found
            return deviceMac;
        }

        /// <summary>
        /// Gets the MAC address for a given device name from the DeviceMetadata configuration.
        /// </summary>
        /// <param name="deviceName">The device name to find the MAC address for</param>
        /// <returns>The MAC address of the device, or null if not found</returns>
        public static string? GetDeviceMac(string deviceName)
        {
            if (DeviceMetadata?.Devices == null || string.IsNullOrEmpty(deviceName))
                return null;

            var device = DeviceMetadata.Devices.Find(d => 
                string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

            return device?.DeviceMac;
        }

        /// <summary>
        /// Gets device-specific coordinates for archive and download buttons.
        /// </summary>
        /// <param name="deviceMac">The MAC address of the device</param>
        /// <returns>Tuple containing archive and download button coordinates</returns>
    public static (int x, int y) GetDeviceCoordinates(string deviceMac)
        {
            if (string.IsNullOrEmpty(deviceMac))
            {
                // Return default coordinates
                return (DEFAULT_ARCHIVE_BUTTON_X, DEFAULT_ARCHIVE_BUTTON_Y);
            }

            var device = DeviceMetadata.Devices.Find(d => 
                string.Equals(d.DeviceMac, deviceMac, StringComparison.OrdinalIgnoreCase));

            if (device != null)
            {
                // Only return archive button coordinates
                return (device.ArchiveButtonX, device.ArchiveButtonY);
            }

            // Return default coordinates if device not found
            return (DEFAULT_ARCHIVE_BUTTON_X, DEFAULT_ARCHIVE_BUTTON_Y);
        }

        #endregion

    // Deprecated members removed. Use DeviceMetadata and GetDeviceCoordinates instead.
}
}
