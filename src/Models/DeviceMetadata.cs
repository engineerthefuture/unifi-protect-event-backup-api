/************************
 * Unifi Webhook Event Receiver
 * DeviceMetadata.cs
 * 
 * Model for device metadata configuration including device mapping and UI coordinates.
 * Represents device-specific settings loaded from environment variables.
 * 
 * Author: Brent Foster
 * Created: 08-25-2025
 ***********************/

using System.Text.Json.Serialization;

namespace UnifiWebhookEventReceiver.Models
{
    /// <summary>
    /// Represents metadata for a Unifi Protect device including name mapping and UI coordinates.
    /// </summary>
    public class DeviceMetadata
    {
        /// <summary>Human-readable device name</summary>
        [JsonPropertyName("deviceName")]
        public required string DeviceName { get; set; }

        /// <summary>Device MAC address identifier</summary>
        [JsonPropertyName("deviceMac")]
        public required string DeviceMac { get; set; }

        /// <summary>X coordinate for archive button in Unifi Protect UI</summary>
        [JsonPropertyName("archiveButtonX")]
        public required int ArchiveButtonX { get; set; }

        /// <summary>Y coordinate for archive button in Unifi Protect UI</summary>
        [JsonPropertyName("archiveButtonY")]
        public required int ArchiveButtonY { get; set; }
    }

    /// <summary>
    /// Collection of device metadata entries loaded from environment configuration.
    /// </summary>
    public class DeviceMetadataCollection
    {
        /// <summary>List of device metadata entries</summary>
        [JsonPropertyName("devices")]
        public List<DeviceMetadata> Devices { get; set; } = new List<DeviceMetadata>();
    }
}
