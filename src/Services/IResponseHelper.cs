/************************
 * Unifi Webhook Event Receiver
 * IResponseHelper.cs
 * 
 * Interface for HTTP response generation operations.
 * Defines the contract for creating standardized API Gateway responses.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using System.Net;
using Amazon.Lambda.APIGatewayEvents;

namespace UnifiWebhookEventReceiver.Services
{
    /// <summary>
    /// Interface for generating standardized HTTP responses.
    /// </summary>
    public interface IResponseHelper
    {
        /// <summary>
        /// Creates a standard error response.
        /// </summary>
        /// <param name="statusCode">HTTP status code</param>
        /// <param name="message">Error message</param>
        /// <returns>API Gateway error response</returns>
        APIGatewayProxyResponse CreateErrorResponse(HttpStatusCode statusCode, string message);

        /// <summary>
        /// Creates a standard success response.
        /// </summary>
        /// <param name="body">Response body object</param>
        /// <returns>API Gateway success response</returns>
        APIGatewayProxyResponse CreateSuccessResponse(object body);

        /// <summary>
        /// Creates a success response for successfully processed alarm.
        /// </summary>
        /// <param name="trigger">The processed trigger information</param>
        /// <param name="timestamp">The event timestamp</param>
        /// <returns>Success API Gateway response</returns>
        APIGatewayProxyResponse CreateSuccessResponse(Trigger trigger, long timestamp);

        /// <summary>
        /// Gets standard CORS headers for responses.
        /// </summary>
        /// <returns>Dictionary of standard headers</returns>
        Dictionary<string, string> GetStandardHeaders();
    }
}
