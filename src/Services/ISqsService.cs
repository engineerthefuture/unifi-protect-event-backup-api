/************************
 * Unifi Webhook Event Receiver
 * ISqsService.cs
 * 
 * Interface for SQS message processing operations.
 * Defines the contract for handling SQS events and queue operations.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.SQSEvents;

namespace UnifiWebhookEventReceiver.Services
{
    /// <summary>
    /// Interface for SQS message processing operations.
    /// </summary>
    public interface ISqsService
    {
        /// <summary>
        /// Processes SQS events containing delayed alarm processing requests.
        /// </summary>
        /// <param name="requestBody">JSON string containing the SQS event</param>
        /// <returns>API Gateway response indicating processing status</returns>
        Task<APIGatewayProxyResponse> ProcessSqsEventAsync(string requestBody);

    /// <summary>
    /// Gets the number of messages currently in the DLQ.
    /// </summary>
    /// <returns>Approximate number of messages in the DLQ</returns>
    Task<int> GetDlqMessageCountAsync();

        /// <summary>
        /// Determines if the request body represents an SQS event.
        /// </summary>
        /// <param name="requestBody">The request body to check</param>
        /// <returns>True if this is an SQS event, false otherwise</returns>
        bool IsSqsEvent(string requestBody);

        /// <summary>
        /// Sends an alarm event to the SQS queue for delayed processing.
        /// </summary>
        /// <param name="alarm">The alarm event to queue</param>
        /// <returns>SQS message ID</returns>
        Task<string> SendAlarmToQueueAsync(Alarm alarm);

        /// <summary>
        /// Queues an alarm event for delayed processing and returns an appropriate response.
        /// </summary>
        /// <param name="alarm">The alarm event to queue</param>
        /// <returns>API Gateway response indicating the event has been queued</returns>
        Task<APIGatewayProxyResponse> QueueAlarmForProcessingAsync(Alarm alarm);

        /// <summary>
        /// Sends an alarm event to the dead letter queue for failed processing scenarios.
        /// </summary>
        /// <param name="alarm">The alarm event to send to DLQ</param>
        /// <param name="reason">The reason for sending to DLQ</param>
        /// <returns>SQS message ID</returns>
        Task<string> SendAlarmToDlqAsync(Alarm alarm, string reason);
    }
}
