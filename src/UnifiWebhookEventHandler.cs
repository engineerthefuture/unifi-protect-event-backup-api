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
            // Validate that the services are provided even though we don't store them as fields
            _ = requestRouter ?? throw new ArgumentNullException(nameof(requestRouter));
            _ = sqsService ?? throw new ArgumentNullException(nameof(sqsService));
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

                // Create services with proper logger for this execution
                logger.LogLine("Creating services with context logger");
                var servicesWithLogger = CreateServicesWithLogger(logger);

                // Check if this is an SQS event and process accordingly
                if (!string.IsNullOrEmpty(requestBody) && requestBody.Contains("\"eventSource\"") && requestBody.Contains("\"Records\""))
                {
                    return await ProcessSQSEventWithLogger(requestBody, servicesWithLogger.sqsService, logger);
                }
                else
                {
                    // Process as an API Gateway event
                    logger.LogLine("Processing as an API Gateway event");
                    return await ProcessAPIGatewayEventWithServices(requestBody ?? "", servicesWithLogger.requestRouter, logger);
                }  
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
            var unifiProtectService = new UnifiProtectService(logger, s3StorageService, credentialsService);
            
            // Create services with resolved dependencies
            var alarmProcessingService = new AlarmProcessingService(s3StorageService, unifiProtectService, credentialsService, responseHelper, logger);
            var sqsService = new SqsService(sqsClient, alarmProcessingService, responseHelper, logger);
            var requestRouter = new RequestRouter(sqsService, s3StorageService, responseHelper, logger);

            return (requestRouter, sqsService, responseHelper, logger);
        }

        /// <summary>
        /// Creates service instances with the provided logger (used for proper logging).
        /// </summary>
        private static (IRequestRouter requestRouter, ISqsService sqsService, IResponseHelper responseHelper) CreateServicesWithLogger(ILambdaLogger logger)
        {
            // Create AWS clients
            var s3Client = new AmazonS3Client(AppConfiguration.AwsRegion);
            var sqsClient = new AmazonSQSClient(AppConfiguration.AwsRegion);
            var secretsClient = new AmazonSecretsManagerClient(AppConfiguration.AwsRegion);

            // Create service instances with proper logger
            var responseHelper = new ResponseHelper();
            var credentialsService = new CredentialsService(secretsClient, logger);
            var s3StorageService = new S3StorageService(s3Client, responseHelper, logger);
            var unifiProtectService = new UnifiProtectService(logger, s3StorageService, credentialsService);
            
            // Create services with resolved dependencies
            var alarmProcessingService = new AlarmProcessingService(s3StorageService, unifiProtectService, credentialsService, responseHelper, logger);
            var sqsService = new SqsService(sqsClient, alarmProcessingService, responseHelper, logger);
            var requestRouter = new RequestRouter(sqsService, s3StorageService, responseHelper, logger);

            return (requestRouter, sqsService, responseHelper);
        }

        /// <summary>
        /// Processes the request as an SQS event.
        /// </summary>
        private static async Task<APIGatewayProxyResponse> ProcessSQSEventWithLogger(string requestBody, ISqsService sqsService, ILambdaLogger logger)
        {
            logger.LogLine($"Request body null/empty: {string.IsNullOrEmpty(requestBody)}");
            
            if (string.IsNullOrEmpty(requestBody))
            {
                logger.LogLine("Request body is null/empty for SQS event");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Bad Request: Empty request body for SQS event",
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }
            
            logger.LogLine("About to call sqsService.IsSqsEvent()");
            bool isSqsEvent = sqsService.IsSqsEvent(requestBody);
            logger.LogLine($"sqsService.IsSqsEvent() returned: {isSqsEvent}");
            
            if (!isSqsEvent)
            {
                logger.LogLine("Event is not a valid SQS event format");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Bad Request: Invalid SQS event format",
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
                };
            }

            return await sqsService.ProcessSqsEventAsync(requestBody);
        }

        /// <summary>
        /// Processes API Gateway HTTP requests with provided services.
        /// </summary>
        private async Task<APIGatewayProxyResponse> ProcessAPIGatewayEventWithServices(string requestBody, IRequestRouter requestRouter, ILambdaLogger logger)
        {
            logger.LogLine("Processing API Gateway event");
            
            try
            {
                // Handle scheduled events
                if (!string.IsNullOrEmpty(requestBody) && requestBody.Contains(AppConfiguration.SOURCE_EVENT_TRIGGER))
                {
                    logger.LogLine("Detected scheduled event trigger");
                    return HandleScheduledEvent();
                }

                // Handle empty request body as bad request
                if (string.IsNullOrEmpty(requestBody))
                {
                    logger.LogLine(AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
                    return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
                }

                logger.LogLine("Parsing API Gateway request from request body");

                // Parse the request
                var request = ParseApiGatewayRequest(requestBody);
                if (request == null)
                {
                    logger.LogLine(AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
                    return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
                }

                logger.LogLine("API Gateway request parsed successfully, routing to handler");

                // Route the request with the properly configured service
                return await requestRouter.RouteRequestAsync(request);
            }
            catch (Exception e)
            {
                logger.LogLine(e.ToString());
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, AppConfiguration.ERROR_MESSAGE_500 + e.Message);
            }
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
        /// Handles scheduled event triggers.
        /// </summary>
        private APIGatewayProxyResponse HandleScheduledEvent()
        {
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
