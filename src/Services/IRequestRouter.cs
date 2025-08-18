/************************
 * Unifi Webhook Event Receiver
 * IRequestRouter.cs
 * 
 * Interface for HTTP request routing operations.
 * Defines the contract for routing API Gateway requests to appropriate handlers.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using Amazon.Lambda.APIGatewayEvents;

namespace UnifiWebhookEventReceiver.Services
{
    /// <summary>
    /// Interface for routing HTTP requests to appropriate handlers.
    /// </summary>
    public interface IRequestRouter
    {
        /// <summary>
        /// Routes an API Gateway request to the appropriate handler.
        /// </summary>
        /// <param name="request">The API Gateway request to route</param>
        /// <returns>API Gateway response from the appropriate handler</returns>
        Task<APIGatewayProxyResponse> RouteRequestAsync(APIGatewayProxyRequest request);

        /// <summary>
        /// Handles CORS preflight OPTIONS requests.
        /// </summary>
        /// <returns>API Gateway response with CORS headers</returns>
        APIGatewayProxyResponse HandleOptionsRequest();
    }
}
