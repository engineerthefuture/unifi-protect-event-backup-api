/************************
 * Unifi Webhook Event Receiver
 * ResponseHelper.cs
 * 
 * Service for generating standardized HTTP responses.
 * Provides consistent response formatting across all API endpoints.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for generating standardized HTTP responses.
    /// </summary>
    public class ResponseHelper : IResponseHelper
    {
        /// <summary>
        /// Creates a standard error response.
        /// </summary>
        /// <param name="statusCode">HTTP status code</param>
        /// <param name="message">Error message</param>
        /// <returns>API Gateway error response</returns>
        public APIGatewayProxyResponse CreateErrorResponse(HttpStatusCode statusCode, string message)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)statusCode,
                Body = JsonConvert.SerializeObject(new { msg = message }),
                Headers = GetStandardHeaders()
            };
        }

        /// <summary>
        /// Creates a standard success response.
        /// </summary>
        /// <param name="body">Response body object</param>
        /// <returns>API Gateway success response</returns>
        public APIGatewayProxyResponse CreateSuccessResponse(object body)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(body),
                Headers = GetStandardHeaders()
            };
        }

        /// <summary>
        /// Creates a success response for successfully processed alarm.
        /// </summary>
        /// <param name="trigger">The processed trigger information</param>
        /// <param name="timestamp">The event timestamp</param>
        /// <returns>Success API Gateway response</returns>
        public APIGatewayProxyResponse CreateSuccessResponse(Trigger trigger, long timestamp)
        {
            DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            string date = string.Format("{0:s}", dt);

            string bodyContent = AppConfiguration.FunctionName + "has successfully processed the Unifi alarm event webhook with key " + trigger.eventKey +
                " for " + trigger.deviceName + " that occurred at " + date + ".";

            return CreateSuccessResponse(new { msg = bodyContent });
        }

        /// <summary>
        /// Gets standard CORS headers for responses.
        /// </summary>
        /// <returns>Dictionary of standard headers</returns>
        public Dictionary<string, string> GetStandardHeaders()
        {
            return new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Access-Control-Allow-Origin", "*" }
            };
        }
    }
}
