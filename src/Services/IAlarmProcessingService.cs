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
    }
}
