/************************
 * Unifi Webhook Event Receiver
 * UnifiWebhookEventHandler.cs
 * 
 * Main handler class for AWS Lambda function that orchestrates all services.
 * This is the new, decomposed main entry point that replaces the monolithic class.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using System.Net;
using System.Reflection;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SQS;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;

// Assembly attributes
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace UnifiWebhookEventReceiver
{
    /// <summary>
    /// Main AWS Lambda function handler for processing Unifi Protect webhook events.
    /// 
    /// This class provides the main entry point for handling HTTP requests from Unifi Dream Machine
    /// alarm webhooks. It orchestrates various services to process alarm events, store them in S3,
    /// and provides RESTful API endpoints for event retrieval.
    /// 
    /// The handler uses dependency injection pattern with service composition to separate concerns
    /// and improve maintainability compared to the original monolithic approach.
    /// </summary>
    public class UnifiWebhookEventHandler
    {
        #region Service Dependencies

        private readonly IRequestRouter _requestRouter;
        private readonly ISqsService _sqsService;
        private readonly IResponseHelper _responseHelper;
        private readonly ILambdaLogger _logger;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance with dependency injection.
        /// Used for testing and when services are externally managed.
        /// </summary>
        public UnifiWebhookEventHandler(
            IRequestRouter requestRouter,
            ISqsService sqsService,
            IResponseHelper responseHelper,
            ILambdaLogger logger)
        {
            _requestRouter = requestRouter ?? throw new ArgumentNullException(nameof(requestRouter));
            _sqsService = sqsService ?? throw new ArgumentNullException(nameof(sqsService));
            _responseHelper = responseHelper ?? throw new ArgumentNullException(nameof(responseHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Default constructor that initializes services with default AWS clients.
        /// Used by Lambda runtime when no external dependency injection is configured.
        /// </summary>
        public UnifiWebhookEventHandler() : this(CreateDefaultServices())
        {
        }

        /// <summary>
        /// Constructor that accepts a tuple of services (used by default constructor).
        /// </summary>
        private UnifiWebhookEventHandler((IRequestRouter, ISqsService, IResponseHelper, ILambdaLogger) services)
            : this(services.Item1, services.Item2, services.Item3, services.Item4)
        {
        }

        #endregion

        #region Lambda Handler

        /// <summary>
        /// Main AWS Lambda function handler for processing both HTTP requests and SQS events.
        /// 
        /// This method serves as the entry point for all incoming events to the Lambda function.
        /// It handles various types of events including:
        /// - API Gateway HTTP requests (POST /alarmevent, GET /*, OPTIONS)
        /// - SQS events for delayed alarm processing
        /// - AWS scheduled events for keep-alive functionality
        /// 
        /// The function detects the event type from the input stream and routes to the appropriate
        /// handler, returning properly formatted responses for each event type.
        /// </summary>
        /// <param name="input">Raw request stream containing the event data</param>
        /// <param name="context">Lambda execution context providing logging and runtime information</param>
        /// <returns>API Gateway proxy response for HTTP events, or void response for SQS events</returns>
        public async Task<APIGatewayProxyResponse> FunctionHandler(Stream input, ILambdaContext context)
        {
            // Initialize with context logger if available
            var logger = context?.Logger ?? _logger;

            try
            {
                if (input == null)
                {
                    logger.LogLine("Input stream was null.");
                    return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
                }

                var functionName = AppConfiguration.FunctionName ?? "UnknownFunction";
                logger.LogLine("C# trigger function processed a request for " + functionName + ".");

                // Read the input stream
                string requestBody = await ReadInputStreamAsync(input);
                logger.LogLine("Request body: " + (requestBody?.Length > 0 ? "Present" : "Empty"));
                
                // Log the event source for debugging
                if (!string.IsNullOrEmpty(requestBody))
                {
                    if (requestBody.Contains("\"eventSource\""))
                    {
                        logger.LogLine("Event contains eventSource field");
                    }
                    if (requestBody.Contains("\"Records\""))
                    {
                        logger.LogLine("Event contains Records field");
                    }
                    if (requestBody.Contains("httpMethod") || requestBody.Contains("HttpMethod"))
                    {
                        logger.LogLine("Event appears to be API Gateway request");
                    }
                }

                // Manual SQS event check with proper logger
                logger.LogLine("=== Manual SQS Event Check START ===");
                logger.LogLine($"Request body null/empty: {string.IsNullOrEmpty(requestBody)}");
                
                if (!string.IsNullOrEmpty(requestBody))
                {
                    // Log a snippet of the request body for debugging (first 500 chars)
                    var snippet = requestBody.Length > 500 ? string.Concat(requestBody.AsSpan(0, 500), "...") : requestBody;
                    logger.LogLine($"Request body snippet: {snippet}");
                    
                    bool hasRecords = requestBody.Contains("\"Records\"");
                    bool hasEventSource = requestBody.Contains("\"eventSource\":\"aws:sqs\"");
                    
                    logger.LogLine($"Has 'Records': {hasRecords}");
                    logger.LogLine($"Has 'eventSource:aws:sqs': {hasEventSource}");
                    
                    bool isSqsEvent = hasRecords && hasEventSource;
                    logger.LogLine($"Is SQS event: {isSqsEvent}");
                    
                    if (isSqsEvent)
                    {
                        logger.LogLine("Detected SQS event, processing delayed alarm");
                        return await _sqsService.ProcessSqsEventAsync(requestBody);
                    }
                }
                
                logger.LogLine("=== Manual SQS Event Check END ===");

                // Otherwise, process as API Gateway event
                return await ProcessAPIGatewayEvent(requestBody ?? "");
            }
            catch (Exception e)
            {
                logger.LogLine($"Error in main handler: {e}");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, AppConfiguration.ERROR_MESSAGE_500 + e.Message);
            }
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Creates default service instances with standard AWS clients.
        /// Used when no external dependency injection container is available.
        /// </summary>
        private static (IRequestRouter, ISqsService, IResponseHelper, ILambdaLogger) CreateDefaultServices()
        {
            // Create a null logger for initialization (will be replaced with context logger in FunctionHandler)
            ILambdaLogger logger = new NullLogger();

            // Create AWS clients
            var s3Client = new AmazonS3Client(AppConfiguration.AwsRegion);
            var sqsClient = new AmazonSQSClient(AppConfiguration.AwsRegion);
            var secretsClient = new AmazonSecretsManagerClient(AppConfiguration.AwsRegion);

            // Create service instances
            var responseHelper = new ResponseHelper();
            var credentialsService = new CredentialsService(secretsClient, logger);
            var s3StorageService = new S3StorageService(s3Client, responseHelper, logger);
            var unifiProtectService = new UnifiProtectService(logger, s3StorageService);
            
            // Create services with resolved dependencies
            var alarmProcessingService = new AlarmProcessingService(s3StorageService, unifiProtectService, credentialsService, responseHelper, logger);
            var sqsService = new SqsService(sqsClient, alarmProcessingService, responseHelper, logger);
            var requestRouter = new RequestRouter(sqsService, s3StorageService, responseHelper, logger);

            return (requestRouter, sqsService, responseHelper, logger);
        }

        /// <summary>
        /// Reads the input stream asynchronously.
        /// </summary>
        /// <param name="input">The input stream to read</param>
        /// <returns>The request body as a string</returns>
        private static async Task<string> ReadInputStreamAsync(Stream input)
        {
            using (var reader = new StreamReader(input))
            {
                return await reader.ReadToEndAsync();
            }
        }

        /// <summary>
        /// Processes API Gateway HTTP requests.
        /// 
        /// This method handles the original HTTP request processing logic including
        /// webhook receipt, event retrieval, and CORS support.
        /// </summary>
        /// <param name="requestBody">JSON string containing the API Gateway request</param>
        /// <returns>API Gateway proxy response</returns>
        private async Task<APIGatewayProxyResponse> ProcessAPIGatewayEvent(string requestBody)
        {
            _logger.LogLine("Processing API Gateway event");
            
            try
            {
                // Handle scheduled events
                if (!string.IsNullOrEmpty(requestBody) && requestBody.Contains(AppConfiguration.SOURCE_EVENT_TRIGGER))
                {
                    _logger.LogLine("Detected scheduled event trigger");
                    return HandleScheduledEvent();
                }

                // Handle empty request body as bad request
                if (string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogLine(AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
                    return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
                }

                _logger.LogLine("Parsing API Gateway request from request body");

                // Parse the request
                var request = ParseApiGatewayRequest(requestBody);
                if (request == null)
                {
                    _logger.LogLine(AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
                    return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
                }

                _logger.LogLine("API Gateway request parsed successfully, routing to handler");

                // Route the request
                return await _requestRouter.RouteRequestAsync(request);
            }
            catch (Exception e)
            {
                _logger.LogLine(e.ToString());
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, AppConfiguration.ERROR_MESSAGE_500 + e.Message);
            }
        }

        /// <summary>
        /// Handles scheduled event triggers.
        /// </summary>
        private APIGatewayProxyResponse HandleScheduledEvent()
        {
            _logger.LogLine("Scheduled event trigger received.");
            return _responseHelper.CreateSuccessResponse(new { msg = AppConfiguration.MESSAGE_202 });
        }

        /// <summary>
        /// Parses the API Gateway request from JSON.
        /// </summary>
        private APIGatewayProxyRequest? ParseApiGatewayRequest(string requestBody)
        {
            try
            {
                return JsonConvert.DeserializeObject<APIGatewayProxyRequest>(requestBody);
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                _logger.LogLine("Failed to deserialize API Gateway request from: " + requestBody + ". Error: " + e.Message);
                throw;
            }
            catch (Exception e)
            {
                _logger.LogLine("Failed to deserialize API Gateway request from: " + requestBody + ". Error: " + e.Message);
                throw;
            }
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// Null object pattern implementation for ILambdaLogger to prevent null reference exceptions
        /// when logger is not available during testing or initialization.
        /// </summary>
        private sealed class NullLogger : ILambdaLogger
        {
            public void Log(string message) { }
            public void LogLine(string message) { }
        }

        #endregion
    }
}
