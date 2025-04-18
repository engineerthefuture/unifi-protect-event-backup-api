/************************
 * Unifi Webhook Event Receiver
 * Alarm.cs
 * Brent Foster
 * 12-23-2024
 ***********************/

namespace UnifiWebhookEventReceiver
{
    public class Alarm
    {
        public string? name { get; set; }
        public List<Source>? sources { get; set; }
        public List<Condition>? conditions { get; set; }
        public required List<Trigger> triggers { get; set; }
        public required long timestamp { get; set; }
        public string? eventPath { get; set; }
        public string? eventLocalLink { get; set; }
    }

    public class Source
    {
        public string device { get; set; }
        public string type { get; set; }
    }
    
    public class Condition
    {
        public string? type { get; set; }
        public string? source { get; set; }
    }

    public class Trigger
    {
        public required string key { get; set; }
        public required string device { get; set; }
        public required string eventId { get; set; }
        public string? deviceName { get; set; }
        public string? date { get; set; }
        public string? eventKey { get; set; }
        public string? videoKey { get; set; }
        public string? presignedUrl { get; set; }
    }
}
