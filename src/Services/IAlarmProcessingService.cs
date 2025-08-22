/************************
 * Unifi Webhook Event Receiver
 * IAlarmProcessingService.cs
 * 
 * Interface for alarm event processing operations.
 * Defines the contract for processing Unifi Protect alarm events.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using Amazon.Lambda.APIGatewayEvents;

namespace UnifiWebhookEventReceiver.Services
{
    /// <summary>
    /// Interface for processing Unifi Protect alarm events.
    /// </summary>
    public interface IAlarmProcessingService
    {
        /// <summary>
        /// Processes a Unifi Protect alarm event and stores it in S3.
        /// </summary>
        /// <param name="alarm">The alarm event to process</param>
        /// <returns>API Gateway response indicating success or failure</returns>
        Task<APIGatewayProxyResponse> ProcessAlarmAsync(Alarm alarm);

        /// <summary>
        /// Processes a Unifi Protect alarm event for SQS context.
        /// Throws exceptions instead of returning HTTP responses to allow SQS error handling.
        /// </summary>
        /// <param name="alarm">The alarm event to process</param>
        /// <returns>Task that completes when processing is done</returns>
        /// <exception cref="InvalidOperationException">Thrown when video download fails with "NoVideoFilesDownloaded"</exception>
        Task ProcessAlarmForSqsAsync(Alarm alarm);
    }
}
