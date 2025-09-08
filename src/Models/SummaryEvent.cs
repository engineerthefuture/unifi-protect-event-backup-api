using System;
using System.Collections.Generic;

namespace UnifiWebhookEventReceiver.Models
{
    /// <summary>
    /// Represents a summary event to be queued after alarm processing.
    /// </summary>
    public class SummaryEvent
    {
    public string? EventId { get; set; }
    public string? Device { get; set; }
    public long Timestamp { get; set; }
    public string? AlarmS3Key { get; set; }
    public string? VideoS3Key { get; set; }
    public string? PresignedVideoUrl { get; set; }
    public string? AlarmName { get; set; }
    public string? DeviceName { get; set; }
    public string? EventType { get; set; }
    public string? EventPath { get; set; }
    public string? EventLocalLink { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    }
}
