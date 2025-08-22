/************************
 * Unifi Webhook Event Receiver
 * IS3StorageService.cs
 * 
 * Interface for S3 storage operations.
 * Defines the contract for storing and retrieving files from S3.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using Amazon.Lambda.APIGatewayEvents;
using Amazon.S3.Model;

namespace UnifiWebhookEventReceiver.Services
{
    /// <summary>
    /// Interface for S3 storage operations.
    /// </summary>
    public interface IS3StorageService
    {
        /// <summary>
        /// Stores alarm event data in S3 as JSON.
        /// </summary>
        /// <param name="alarm">The alarm data to store</param>
        /// <param name="trigger">The specific trigger information</param>
        /// <returns>The S3 key where the data was stored</returns>
        Task<string> StoreAlarmEventAsync(Alarm alarm, Trigger trigger);

        /// <summary>
        /// Stores a video file in S3.
        /// </summary>
        /// <param name="videoFilePath">Path to the video file to upload</param>
        /// <param name="s3Key">S3 key for the file</param>
        /// <returns>Task representing the upload operation</returns>
        Task StoreVideoFileAsync(string videoFilePath, string s3Key);

        /// <summary>
        /// Retrieves the latest video file from S3.
        /// </summary>
        /// <returns>API Gateway response with the video file</returns>
        Task<APIGatewayProxyResponse> GetLatestVideoAsync();

        /// <summary>
        /// Retrieves a video file by event ID from S3.
        /// </summary>
        /// <param name="eventId">The event ID to search for</param>
        /// <returns>API Gateway response with the video file</returns>
        Task<APIGatewayProxyResponse> GetVideoByEventIdAsync(string eventId);

        /// <summary>
        /// Generates S3 file keys for event data and video files.
        /// </summary>
        /// <param name="trigger">The trigger information</param>
        /// <param name="timestamp">The event timestamp</param>
        /// <returns>Tuple containing event key and video key</returns>
        (string eventKey, string videoKey) GenerateS3Keys(Trigger trigger, long timestamp);

        /// <summary>
        /// Uploads a screenshot file to S3 with appropriate content type.
        /// </summary>
        /// <param name="screenshotFilePath">Path to the local screenshot file</param>
        /// <param name="s3Key">S3 key for the screenshot</param>
        Task StoreScreenshotFileAsync(string screenshotFilePath, string s3Key);

        /// <summary>
        /// Retrieves a binary file from S3.
        /// </summary>
        /// <param name="s3Key">The S3 key of the file to retrieve</param>
        /// <returns>The file data as byte array, or null if not found</returns>
        Task<byte[]?> GetFileAsync(string s3Key);
    }
}
