/************************
 * Unifi Webhook Event Receiver
 * CameraSummaryMulti.cs
 *
 * Models for camera event summary and per-camera aggregation used in the /summary API response.
 * Includes event data, presigned video links, and per-camera statistics.
 *
 * Author: Brent Foster
 * Created: 08-27-2025
 ***********************/

namespace UnifiWebhookEventReceiver.Models
{
    public class CameraEventSummary
    {
        public object? eventData { get; set; }
        public string? videoUrl { get; set; }
        public string? originalFileName { get; set; }
    }

    public class CameraSummaryMulti
    {
        public string cameraId { get; set; } = string.Empty;
        public string cameraName { get; set; } = string.Empty;
        public List<CameraEventSummary> events { get; set; } = new();
        public int count24h { get; set; }
    }
}
