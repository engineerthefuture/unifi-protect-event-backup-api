/************************
 * Unifi Webhook Event Receiver
 * Event.cs
 * 
 * Data models for Unifi Protect alarm webhook events.
 * These classes represent the JSON structure of webhook payloads
 * sent by Unifi Dream Machine when alarm events are triggered.
 * 
 * Author: Brent Foster
 * Created: 12-23-2024
 * Updated: 01-11-2025
 ***********************/

namespace UnifiWebhookEventReceiver
{
    /// <summary>
    /// Represents a complete Unifi Protect alarm event received via webhook.
    /// 
    /// This is the root object that contains all information about an alarm event,
    /// including the devices involved, conditions that triggered the alarm,
    /// and the specific triggers that activated the event.
    /// 
    /// The alarm data is stored in S3 with additional metadata like timestamp
    /// and processed device information for later retrieval and analysis.
    /// </summary>
    public class Alarm
    {
        /// <summary>Human-readable name of the alarm rule</summary>
        public string? name { get; set; }

        /// <summary>List of source devices involved in the alarm event</summary>
        public List<Source>? sources { get; set; }

        /// <summary>List of conditions that must be met for the alarm to trigger</summary>
        public List<Condition>? conditions { get; set; }

        /// <summary>List of specific triggers that activated this alarm event</summary>
        public required List<Trigger> triggers { get; set; }

        /// <summary>Unix timestamp (milliseconds) when the alarm event occurred</summary>
        public required long timestamp { get; set; }

        /// <summary>Local file path to the associated event data on the Unifi system</summary>
        public string? eventPath { get; set; }

        /// <summary>Local network link to access the event data directly from the Unifi system</summary>
        public string? eventLocalLink { get; set; }
    }

    /// <summary>
    /// Represents a source device that can participate in alarm events.
    /// 
    /// Sources identify the physical devices (cameras, sensors, etc.) that
    /// are configured to trigger alarm conditions in the Unifi Protect system.
    /// </summary>
    public class Source
    {
        /// <summary>Unique device identifier (typically MAC address)</summary>
        public required string device { get; set; }

        /// <summary>Type of device (camera, sensor, etc.)</summary>
        public required string type { get; set; }
    }

    /// <summary>
    /// Represents a condition that must be satisfied for an alarm to trigger.
    /// 
    /// Conditions define the rules and criteria that determine when an alarm
    /// should activate based on device states, motion detection, or other events.
    /// </summary>
    public class Condition
    {
        /// <summary>Type of condition (motion, intrusion, etc.)</summary>
        public string? type { get; set; }

        /// <summary>Source device or rule that this condition applies to</summary>
        public string? source { get; set; }
    }

    /// <summary>
    /// Represents a specific trigger instance that activated an alarm.
    /// 
    /// Triggers contain the detailed information about what actually happened
    /// to cause the alarm, including device identification, event details,
    /// and additional metadata added during processing.
    /// </summary>
    public class Trigger
    {
        /// <summary>Type/key identifier for the trigger event (motion, intrusion, etc.)</summary>
        public required string key { get; set; }

        /// <summary>Device identifier (MAC address) that generated the trigger</summary>
        public required string device { get; set; }

        /// <summary>Unique event identifier from the Unifi Protect system</summary>
        public required string eventId { get; set; }

        /// <summary>Human-readable device name (populated during processing from environment variables)</summary>
        public string? deviceName { get; set; }

        /// <summary>Formatted date string of when the event occurred (populated during processing)</summary>
        public string? date { get; set; }

        /// <summary>S3 storage key for the processed event data (populated during processing)</summary>
        public string? eventKey { get; set; }

        /// <summary>S3 storage key for associated video file (for future video upload functionality)</summary>
        public string? videoKey { get; set; }

        /// <summary>Original filename of the downloaded video file from browser</summary>
        public string? originalFileName { get; set; }
    }
}
