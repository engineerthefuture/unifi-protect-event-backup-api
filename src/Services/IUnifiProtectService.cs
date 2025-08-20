/************************
 * Unifi Webhook Event Receiver
 * IUnifiProtectService.cs
 * 
 * Interface for Unifi Protect system integration.
 * Defines the contract for downloading videos and interacting with Unifi Protect.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/



namespace UnifiWebhookEventReceiver.Services
{
    /// <summary>
    /// Interface for Unifi Protect system integration.
    /// </summary>
    public interface IUnifiProtectService
    {
        /// <summary>
        /// Downloads a video file from Unifi Protect for a specific event.
        /// </summary>
        /// <param name="trigger">The trigger containing event information</param>
        /// <param name="eventLocalLink">Direct URL to the event in Unifi Protect</param>
        /// <param name="timestamp">The event timestamp for consistent S3 key generation</param>
        /// <returns>Path to the downloaded video file</returns>
        Task<string> DownloadVideoAsync(Trigger trigger, string eventLocalLink, long timestamp);

        /// <summary>
        /// Cleans up temporary video files.
        /// </summary>
        /// <param name="filePath">Path to the file to clean up</param>
        void CleanupTempFile(string filePath);
    }
}
