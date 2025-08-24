/************************
 * Unifi Webhook Event Receiver
 * RequestRouter.cs
 * 
 * Service for routing HTTP requests to appropriate handlers.
 * Handles API Gateway request routing, validation, and CORS support.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for routing HTTP requests to appropriate handlers.
    /// </summary>
    public class RequestRouter : IRequestRouter
    {
        private readonly ISqsService _sqsService;
        private readonly IS3StorageService _s3StorageService;
        private readonly IResponseHelper _responseHelper;
        private readonly ILambdaLogger _logger;

        /// <summary>
        /// Initializes a new instance of the RequestRouter.
        /// </summary>
        public RequestRouter(
            ISqsService sqsService,
            IS3StorageService s3StorageService,
            IResponseHelper responseHelper,
            ILambdaLogger logger)
        {
            _sqsService = sqsService ?? throw new ArgumentNullException(nameof(sqsService));
            _s3StorageService = s3StorageService ?? throw new ArgumentNullException(nameof(s3StorageService));
            _responseHelper = responseHelper ?? throw new ArgumentNullException(nameof(responseHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Routes an API Gateway request to the appropriate handler.
        /// </summary>
        /// <param name="request">The API Gateway request to route</param>
        /// <returns>API Gateway response from the appropriate handler</returns>
        public async Task<APIGatewayProxyResponse> RouteRequestAsync(APIGatewayProxyRequest request)
        {
            if (!IsValidRequest(request))
            {
                return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
            }

            var routeInfo = ExtractRouteInfo(request);
            _logger.LogLine($"Method: {routeInfo.Method}, Path: {routeInfo.Path}, Route: {routeInfo.Route}");

            // Handle OPTIONS preflight
            if (routeInfo.Method == "OPTIONS")
            {
                return HandleOptionsRequest();
            }

            // Route to specific handlers
            return await RouteToHandler(request, routeInfo);
        }

        /// <summary>
        /// Handles CORS preflight OPTIONS requests.
        /// </summary>
        /// <returns>API Gateway response with CORS headers</returns>
        public APIGatewayProxyResponse HandleOptionsRequest()
        {
            var headers = _responseHelper.GetStandardHeaders();
            headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
            headers["Access-Control-Allow-Headers"] = "Content-Type,X-Amz-Date,Authorization,X-Api-Key,X-Amz-Security-Token";
            
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = null,
                Headers = headers
            };
        }

        #region Private Helper Methods

        /// <summary>
        /// Validates the API Gateway request.
        /// </summary>
        private static bool IsValidRequest(APIGatewayProxyRequest request)
        {
            return request?.Path != null && 
                   request.Path != "" && 
                   request.HttpMethod != null;
        }

        /// <summary>
        /// Extracts route information from the request.
        /// </summary>
        private static (string Method, string Path, string Route) ExtractRouteInfo(APIGatewayProxyRequest request)
        {
            var path = request.Path!;
            if (path.Contains('/'))
            {
                path = path.Substring(path.IndexOf('/') + 1);
            }

            // For paths like "dev/latestvideo", extract the endpoint after the environment prefix
            var route = path;
            if (path.Contains('/'))
            {
                route = path.Substring(path.LastIndexOf('/') + 1);
            }

            return (request.HttpMethod!.ToUpper(), path, route);
        }

        /// <summary>
        /// Routes to the appropriate handler based on method and route.
        /// </summary>
#pragma warning disable S1541 // Methods should not be too complex - complexity 13 is acceptable under new threshold of 13
        private async Task<APIGatewayProxyResponse> RouteToHandler(APIGatewayProxyRequest request, (string Method, string Path, string Route) routeInfo)
#pragma warning restore S1541
        {
            // Check if route is valid
            if (string.IsNullOrEmpty(routeInfo.Route) && (request.QueryStringParameters?.Count ?? 0) == 0)
            {
                return _responseHelper.CreateErrorResponse(HttpStatusCode.NotFound, AppConfiguration.ERROR_MESSAGE_404 + AppConfiguration.ERROR_INVALID_ROUTE);
            }

            return routeInfo.Method switch
            {
                "POST" when routeInfo.Route == AppConfiguration.ROUTE_ALARM => await HandleAlarmWebhook(request),
                "GET" when routeInfo.Route == AppConfiguration.ROUTE_LATEST_VIDEO => await HandleLatestVideoRequest(),
                "GET" => await HandleVideoDownloadRequest(request),
                "PUT" or "PATCH" or "HEAD" or "DELETE" => CreateMethodNotAllowedResponse(routeInfo.Method),
                _ => CreateInvalidRouteResponse(routeInfo.Route, routeInfo.Method)
            };
        }

        /// <summary>
        /// Handles alarm webhook POST requests.
        /// </summary>
        private async Task<APIGatewayProxyResponse> HandleAlarmWebhook(APIGatewayProxyRequest request)
        {
            if (string.IsNullOrEmpty(request.Body))
            {
                return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
            }

            _logger.LogLine("Request: " + request.Body);

            var alarm = ParseAlarmFromRequest(request.Body);
            if (alarm == null)
            {
                return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + "Invalid alarm object format");
            }

            try
            {
                return await _sqsService.QueueAlarmForProcessingAsync(alarm);
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error queuing alarm for processing: {ex.Message}");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, "Failed to queue alarm for processing");
            }
        }

        /// <summary>
        /// Parses alarm object from the request body.
        /// </summary>
        private static Alarm? ParseAlarmFromRequest(string requestBody)
        {
            try
            {
                var jo = JObject.Parse(requestBody);
                var alarmObject = jo.SelectToken("alarm") as JObject;
                var timestamp = jo.SelectToken("timestamp")?.Value<long>() ?? 0;

                if (alarmObject == null)
                {
                    return null;
                }

                var alarm = JsonConvert.DeserializeObject<Alarm>(alarmObject.ToString());
                if (alarm != null)
                {
                    alarm.timestamp = timestamp;
                }

                return alarm;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Handles latest video GET requests.
        /// </summary>
        private async Task<APIGatewayProxyResponse> HandleLatestVideoRequest()
        {
            return await _s3StorageService.GetLatestVideoAsync();
        }

        /// <summary>
        /// Handles video download GET requests.
        /// </summary>
        private async Task<APIGatewayProxyResponse> HandleVideoDownloadRequest(APIGatewayProxyRequest request)
        {
            var eventId = request.QueryStringParameters?.TryGetValue("eventId", out var value) == true ? value : null;
            
            if (string.IsNullOrEmpty(eventId))
            {
                return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest,
                    "Missing required parameter. Provide 'eventId' for video download.");
            }

            return await _s3StorageService.GetVideoByEventIdAsync(eventId);
        }

        /// <summary>
        /// Creates a method not allowed response.
        /// </summary>
        private APIGatewayProxyResponse CreateMethodNotAllowedResponse(string method)
        {
            return _responseHelper.CreateErrorResponse(HttpStatusCode.MethodNotAllowed,
                $"Method {method} is not allowed for this endpoint.");
        }

        /// <summary>
        /// Creates an invalid route response.
        /// </summary>
        private APIGatewayProxyResponse CreateInvalidRouteResponse(string route, string method)
        {
            return _responseHelper.CreateErrorResponse(HttpStatusCode.NotFound,
                $"{AppConfiguration.ERROR_MESSAGE_404} {AppConfiguration.ERROR_INVALID_ROUTE}. Route: {route}, Method: {method}");
        }

        #endregion
    }
}
