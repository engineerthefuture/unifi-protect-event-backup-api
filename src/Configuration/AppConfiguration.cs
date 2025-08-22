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

namespace UnifiWebhookEventReceiver.Configuration
{
    /// <summary>
    /// Centralized configuration management class that provides strongly-typed access
    /// to environment variables and AWS configuration settings.
    /// </summary>
    public static class AppConfiguration
    {
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

        /// <summary>Event source identifier for AWS scheduled events</summary>
        public const string SOURCE_EVENT_TRIGGER = "aws.events";

        #endregion

        #region Environment Variables

        /// <summary>S3 bucket name for storing alarm event data</summary>
        public static string? AlarmBucketName => Environment.GetEnvironmentVariable("StorageBucket");

        /// <summary>Prefix for environment variables containing device MAC to name mappings</summary>
        public static string? DevicePrefix => Environment.GetEnvironmentVariable("DevicePrefix");

        /// <summary>Lambda function name for logging and identification</summary>
        public static string? FunctionName => Environment.GetEnvironmentVariable("FunctionName");

        /// <summary>AWS Secrets Manager ARN containing Unifi Protect credentials</summary>
        public static string? UnifiCredentialsSecretArn => Environment.GetEnvironmentVariable("UnifiCredentialsSecretArn");

        /// <summary>Download directory for temporary video files. Defaults to /tmp for Lambda compatibility.</summary>
        public static string DownloadDirectory => Environment.GetEnvironmentVariable("DownloadDirectory") ?? "/tmp";

        /// <summary>X coordinate for archive button click. Defaults to 1274.</summary>
        public static int ArchiveButtonX => int.TryParse(Environment.GetEnvironmentVariable("ArchiveButtonX"), out var archiveX) ? archiveX : 1274;

        /// <summary>Y coordinate for archive button click. Defaults to 257.</summary>
        public static int ArchiveButtonY => int.TryParse(Environment.GetEnvironmentVariable("ArchiveButtonY"), out var archiveY) ? archiveY : 257;

        /// <summary>X coordinate for download button click. Defaults to 1095.</summary>
        public static int DownloadButtonX => int.TryParse(Environment.GetEnvironmentVariable("DownloadButtonX"), out var downloadX) ? downloadX : 1095;

        /// <summary>Y coordinate for download button click. Defaults to 275.</summary>
        public static int DownloadButtonY => int.TryParse(Environment.GetEnvironmentVariable("DownloadButtonY"), out var downloadY) ? downloadY : 275;

        /// <summary>SQS queue URL for delayed alarm processing</summary>
        public static string? AlarmProcessingQueueUrl => Environment.GetEnvironmentVariable("AlarmProcessingQueueUrl");

        /// <summary>SQS dead letter queue URL for failed alarm processing</summary>
        public static string? AlarmProcessingDlqUrl => Environment.GetEnvironmentVariable("AlarmProcessingDlqUrl");

        /// <summary>Delay in seconds before processing alarm events (defaults to 2 minutes)</summary>
        public static int ProcessingDelaySeconds => int.TryParse(Environment.GetEnvironmentVariable("ProcessingDelaySeconds"), out var delay) ? delay : 120;

        #endregion

        #region AWS Configuration

        /// <summary>AWS region for S3 operations</summary>
        public static RegionEndpoint AwsRegion => RegionEndpoint.USEast1;

        #endregion

        #region Device Name Mapping

        /// <summary>
        /// Gets the human-readable device name from environment variables using device MAC address.
        /// </summary>
        /// <param name="deviceMac">MAC address of the device</param>
        /// <returns>Human-readable device name or original MAC if not found</returns>
        public static string GetDeviceName(string deviceMac)
        {
            if (string.IsNullOrEmpty(DevicePrefix) || string.IsNullOrEmpty(deviceMac))
            {
                return deviceMac;
            }

            var deviceName = Environment.GetEnvironmentVariable($"{DevicePrefix}{deviceMac}");
            return string.IsNullOrEmpty(deviceName) ? deviceMac : deviceName;
        }

        #endregion
    }
}
