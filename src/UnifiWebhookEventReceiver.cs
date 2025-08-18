/************************
 * Unifi Webhook Event Receiver
 * UnifiWebhookEventReceiver.cs
 * 
 * AWS Lambda function that receives and processes webhook events from Unifi Dream Machine.
 * Handles alarm events by storing them in S3 with organized folder structure by date.
 * Supports CORS for web client integration and provides GET API for event retrieval.
 * 
 * Author: Brent Foster
 * Created: 12-23-2024
 * Last Updated: 08-16-2025
 ***********************/

// System includes
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

// Third-party includes
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;
using HeadlessChromium.Puppeteer.Lambda.Dotnet;

// AWS includes
using Amazon;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

// Assembly attributes
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace UnifiWebhookEventReceiver
{
    /// <summary>
    /// AWS Lambda function handler for processing Unifi Protect webhook events.
    /// 
    /// This class provides the main entry point for handling HTTP requests from Unifi Dream Machine
    /// alarm webhooks. It processes alarm events, stores them in S3 with date-organized folder
    /// structure, and provides RESTful API endpoints for event retrieval.
    /// 
    /// Supported HTTP methods:
    /// - POST /alarmevent: Processes alarm webhook events from Unifi Protect
    /// - GET /?eventKey={key}: Retrieves stored alarm event data
    /// - GET /latestvideo: Downloads the most recent video file as MP4
    /// - OPTIONS: Handles CORS preflight requests for web client support
    /// 
    /// Environment Variables Required:
    /// - StorageBucket: S3 bucket name for storing alarm events
    /// - AlarmProcessingQueueUrl: SQS queue URL for delayed alarm processing
    /// - ProcessingDelaySeconds: Delay in seconds before processing (default: 120)
    /// - DevicePrefix: Prefix for environment variables containing device name mappings
    /// - DeployedEnv: Environment identifier (dev, prod, etc.)
    /// - FunctionName: Lambda function name for logging
    /// - UnifiCredentialsSecretArn: AWS Secrets Manager ARN containing Unifi Protect credentials
    /// - DownloadDirectory: Directory for temporary video files (defaults to /tmp)
    /// - ArchiveButtonX: X coordinate for archive button click (used for "Door" device, defaults to 1274)
    /// - ArchiveButtonY: Y coordinate for archive button click (used for "Door" device, defaults to 257)
    /// - DownloadButtonX: X coordinate for download button click (used for "Door" device, defaults to 1095)
    /// - DownloadButtonY: Y coordinate for download button click (used for "Door" device, defaults to 275)
    /// 
    /// AWS Secrets Manager Secret Format (JSON):
    /// {
    ///   "hostname": "unifi-host.local or IP address",
    ///   "username": "unifi-protect-username",
    ///   "password": "unifi-protect-password"
    /// }
    /// 
    /// Note: For devices other than "Door", coordinates are automatically adjusted to (1205, 241) for archive
    /// and (1026, 259) for download to accommodate different UI layouts in Unifi Protect.
    /// 
    /// Dependencies:
    /// - For local development, ensure HeadlessChromium can be initialized properly
    /// </summary>
    public class UnifiWebhookEventReceiver
    {
        #region Constants and Configuration

        /// <summary>Error message template for 500 Internal Server Error responses</summary>
        const string ERROR_MESSAGE_500 = "An internal server error has occured: ";

        /// <summary>Error message template for 400 Bad Request responses</summary>
        const string ERROR_MESSAGE_400 = "Your request is malformed or invalid: ";

        /// <summary>Error message template for 404 Not Found responses</summary>
        const string ERROR_MESSAGE_404 = "Route not found: ";

        /// <summary>Success message for requests that don't require action</summary>
        const string MESSAGE_202 = "No action taken on request.";

        /// <summary>Error message for requests missing required body content</summary>
        const string ERROR_GENERAL = "you must have a valid body object in your request";

        /// <summary>Error message for alarm events missing triggers</summary>
        const string ERROR_TRIGGERS = "you must have triggers in your payload";

        /// <summary>Error message for invalid API routes</summary>
        const string ERROR_INVALID_ROUTE = "please provide a valid route";

        /// <summary>API route for alarm event webhook processing</summary>
        const string ROUTE_ALARM = "alarmevent";

        /// <summary>API route for latest video download</summary>
        const string ROUTE_LATEST_VIDEO = "latestvideo";

        /// <summary>Event source identifier for AWS scheduled events</summary>
        const string SOURCE_EVENT_TRIGGER = "aws.events";

        #endregion

        #region Environment Variables and AWS Configuration

        /// <summary>S3 bucket name for storing alarm event data</summary>
        static string? ALARM_BUCKET_NAME = Environment.GetEnvironmentVariable("StorageBucket");

        /// <summary>Prefix for environment variables containing device MAC to name mappings</summary>
        static string? DEVICE_PREFIX = Environment.GetEnvironmentVariable("DevicePrefix");

        /// <summary>Lambda function name for logging and identification</summary>
        static string? FUNCTION_NAME = Environment.GetEnvironmentVariable("FunctionName");

        /// <summary>AWS Secrets Manager ARN containing Unifi Protect credentials</summary>
        static string? UNIFI_CREDENTIALS_SECRET_ARN = Environment.GetEnvironmentVariable("UnifiCredentialsSecretArn");

        /// <summary>Download directory for temporary video files. Defaults to /tmp for Lambda compatibility.</summary>
        static string DOWNLOAD_DIRECTORY = Environment.GetEnvironmentVariable("DownloadDirectory") ?? "/tmp";

        /// <summary>X coordinate for archive button click. Defaults to 1274.</summary>
        static int ARCHIVE_BUTTON_X = int.TryParse(Environment.GetEnvironmentVariable("ArchiveButtonX"), out var archiveX) ? archiveX : 1274;

        /// <summary>Y coordinate for archive button click. Defaults to 257.</summary>
        static int ARCHIVE_BUTTON_Y = int.TryParse(Environment.GetEnvironmentVariable("ArchiveButtonY"), out var archiveY) ? archiveY : 257;

        /// <summary>X coordinate for download button click. Defaults to 1095.</summary>
        static int DOWNLOAD_BUTTON_X = int.TryParse(Environment.GetEnvironmentVariable("DownloadButtonX"), out var downloadX) ? downloadX : 1095;

        /// <summary>Y coordinate for download button click. Defaults to 275.</summary>
        static int DOWNLOAD_BUTTON_Y = int.TryParse(Environment.GetEnvironmentVariable("DownloadButtonY"), out var downloadY) ? downloadY : 275;

        /// <summary>SQS queue URL for delayed alarm processing</summary>
        static string? ALARM_PROCESSING_QUEUE_URL = Environment.GetEnvironmentVariable("AlarmProcessingQueueUrl");

        /// <summary>Delay in seconds before processing alarm events (defaults to 2 minutes)</summary>
        static int PROCESSING_DELAY_SECONDS = int.TryParse(Environment.GetEnvironmentVariable("ProcessingDelaySeconds"), out var delay) ? delay : 120;

        /// <summary>AWS region for S3 operations</summary>
        static RegionEndpoint AWS_REGION = RegionEndpoint.USEast1;

        /// <summary>S3 client instance for bucket operations</summary>
        static AmazonS3Client s3Client = new AmazonS3Client(AWS_REGION);

        /// <summary>SQS client instance for queue operations</summary>
        static AmazonSQSClient sqsClient = new AmazonSQSClient(AWS_REGION);

        /// <summary>Secrets Manager client instance for credential retrieval</summary>
        static AmazonSecretsManagerClient secretsClient = new AmazonSecretsManagerClient(AWS_REGION);

        #endregion

        #region Unifi Credentials Management

        /// <summary>Class to hold Unifi Protect credentials retrieved from Secrets Manager</summary>
        public class UnifiCredentials
        {
            public string hostname { get; set; } = string.Empty;
            public string username { get; set; } = string.Empty;
            public string password { get; set; } = string.Empty;
        }

        /// <summary>Cache for Unifi credentials to avoid repeated Secrets Manager calls</summary>
        private static UnifiCredentials? _cachedCredentials;

        /// <summary>
        /// Retrieves Unifi Protect credentials from AWS Secrets Manager
        /// </summary>
        /// <returns>UnifiCredentials object containing hostname, username, and password</returns>
        [ExcludeFromCodeCoverage] // Requires AWS Secrets Manager connectivity
        private static async Task<UnifiCredentials> GetUnifiCredentialsAsync()
        {
            if (_cachedCredentials != null)
            {
                return _cachedCredentials;
            }

            if (string.IsNullOrEmpty(UNIFI_CREDENTIALS_SECRET_ARN))
            {
                throw new InvalidOperationException("UnifiCredentialsSecretArn environment variable is not set");
            }

            try
            {
                var request = new GetSecretValueRequest
                {
                    SecretId = UNIFI_CREDENTIALS_SECRET_ARN
                };

                var response = await secretsClient.GetSecretValueAsync(request);
                var credentials = JsonConvert.DeserializeObject<UnifiCredentials>(response.SecretString);
                
                if (credentials == null)
                {
                    throw new InvalidOperationException("Failed to deserialize Unifi credentials from Secrets Manager");
                }

                _cachedCredentials = credentials;
                return credentials;
            }
            catch (Exception ex)
            {
                log.LogError($"Error retrieving Unifi credentials from Secrets Manager: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Logging Infrastructure

        /// <summary>Lambda logger instance for function execution logging</summary>
        static ILambdaLogger log = new NullLogger();

        /// <summary>
        /// Null object pattern implementation for ILambdaLogger to prevent null reference exceptions
        /// when logger is not available during testing or initialization
        /// </summary>
        private sealed class NullLogger : ILambdaLogger
        {
            public void Log(string message) { }
            public void LogLine(string message) { }
        }

        #endregion

        #region Main Lambda Handler

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
        public static async Task<APIGatewayProxyResponse> FunctionHandler(Stream input, ILambdaContext context)
        {
            try
            {
                log = context?.Logger ?? new NullLogger();
                if (input == null)
                {
                    log.LogLine("Input stream was null.");
                    return CreateErrorResponse(HttpStatusCode.BadRequest, ERROR_MESSAGE_400 + ERROR_GENERAL);
                }

                var functionName = FUNCTION_NAME ?? "UnknownFunction";
                log.LogLine("C# trigger function processed a request for " + functionName + ".");

                // Read the input stream
                string requestBody = await ReadInputStreamAsync(input);
                log.LogLine("Request body: " + (requestBody?.Length > 0 ? "Present" : "Empty"));

                // Check if this is an SQS event and process accordingly
                var sqsResponse = await TryProcessSQSEvent(requestBody ?? "");
                if (sqsResponse != null)
                {
                    return sqsResponse;
                }

                // Otherwise, process as API Gateway event
                return await ProcessAPIGatewayEvent(requestBody ?? "");
            }
            catch (Exception e)
            {
                log.LogLine($"Error in main handler: {e}");
                return CreateErrorResponse(HttpStatusCode.InternalServerError, ERROR_MESSAGE_500 + e.Message);
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
        /// Attempts to process the request as an SQS event.
        /// </summary>
        /// <param name="requestBody">The request body to check and process</param>
        /// <returns>API Gateway response if this was an SQS event, null otherwise</returns>
        private static async Task<APIGatewayProxyResponse?> TryProcessSQSEvent(string requestBody)
        {
            if (string.IsNullOrEmpty(requestBody) || !IsSQSEvent(requestBody))
            {
                return null;
            }

            log.LogLine("Detected SQS event, processing delayed alarm");
            await ProcessSQSEvent(requestBody);
            
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(new { msg = "SQS event processed successfully" }),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }

        /// <summary>
        /// Determines if the request body represents an SQS event.
        /// </summary>
        /// <param name="requestBody">The request body to check</param>
        /// <returns>True if this is an SQS event, false otherwise</returns>
        private static bool IsSQSEvent(string requestBody)
        {
            return requestBody.Contains("\"Records\"") && requestBody.Contains("\"eventSource\":\"aws:sqs\"");
        }

        /// <summary>
        /// Processes SQS events containing delayed alarm processing requests.
        /// 
        /// This method handles SQS messages that contain alarm event data for processing
        /// after a delay period to ensure video files are fully available in Unifi Protect.
        /// </summary>
        /// <param name="requestBody">JSON string containing the SQS event</param>
        /// <returns>Task representing the asynchronous processing operation</returns>
        private static async Task ProcessSQSEvent(string requestBody)
        {
            try
            {
                var sqsEvent = JsonConvert.DeserializeObject<SQSEvent>(requestBody);
                if (sqsEvent?.Records == null)
                {
                    log.LogLine("No SQS records found in event");
                    return;
                }

                foreach (var record in sqsEvent.Records)
                {
                    await ProcessSingleSQSRecord(record);
                }
            }
            catch (Exception ex)
            {
                log.LogLine($"Error processing SQS event: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Processes a single SQS record containing alarm data.
        /// </summary>
        /// <param name="record">The SQS record to process</param>
        /// <returns>Task representing the asynchronous processing operation</returns>
        private static async Task ProcessSingleSQSRecord(SQSEvent.SQSMessage record)
        {
            try
            {
                log.LogLine($"Processing SQS message: {record.MessageId}");
                
                // Parse the alarm data from the message body
                var messageBody = record.Body;
                var alarm = JsonConvert.DeserializeObject<Alarm>(messageBody);
                
                if (alarm != null)
                {
                    log.LogLine($"Processing delayed alarm for device: {alarm.triggers?.FirstOrDefault()?.device}");
                    await AlarmReceiverFunction(alarm);
                    log.LogLine($"Successfully processed delayed alarm: {record.MessageId}");
                }
                else
                {
                    log.LogLine($"Failed to deserialize alarm from message: {record.MessageId}");
                }
            }
            catch (Exception ex)
            {
                log.LogLine($"Error processing SQS message {record.MessageId}: {ex.Message}");
                // Don't throw here - we want to continue processing other messages
            }
        }

        /// <summary>
        /// Handles the Unifi Protect login process with credentials.
        /// </summary>
        /// <param name="page">The Puppeteer page object</param>
        /// <param name="usernameField">The username input field element</param>
        /// <param name="passwordField">The password input field element</param>
        /// <param name="credentials">The Unifi credentials to use</param>
        /// <returns>Task representing the asynchronous login operation</returns>
        private static async Task PerformUnifiLogin(IPage page, IElementHandle usernameField, IElementHandle passwordField, UnifiCredentials credentials)
        {
            log.LogLine("Login form detected, attempting authentication...");

            // Take the username and password and * out all but the first 3 characters of the username and all of the characters of the password
            log.LogLine("Filling in credentials for login...");
            var maskedUsername = credentials.username.Length > 3 ? string.Concat(credentials.username.AsSpan(0, 3), new string('*', credentials.username.Length - 3)) : credentials.username;
            var maskedPassword = new string('*', credentials.password.Length);

            log.LogLine($"Using credentials - Username: {maskedUsername}, Password: {maskedPassword}");

            // Fill in credentials
            await usernameField.TypeAsync(credentials.username);
            await passwordField.TypeAsync(credentials.password);

            // Look for login button
            var loginButton = await page.QuerySelectorAsync("button[type='submit']");

            // Check if login button is present
            if (loginButton != null)
            {
                await loginButton.ClickAsync();
                log.LogLine("Login button clicked, waiting for authentication...");

                // Wait for navigation after login
                await page.WaitForNavigationAsync(new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                    Timeout = 10000
                });
            }
            else
            {
                log.LogLine("Login button not found, trying Enter key...");
                await passwordField.PressAsync("Enter");

                // Wait for navigation after Enter key
                try
                {
                    await page.WaitForNavigationAsync(new NavigationOptions
                    {
                        WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                        Timeout = 10000
                    });
                }
                catch (Exception)
                {
                    log.LogLine("Navigation timeout after Enter key, continuing...");
                }
            }
        }

        /// <summary>
        /// Processes download events from the browser to track download progress.
        /// </summary>
        /// <param name="messageId">The message ID from the browser event</param>
        /// <param name="messageData">The message data from the browser event</param>
        /// <param name="downloadStarted">Reference to track if download has started</param>
        /// <param name="downloadGuid">Reference to store the download GUID</param>
        private static void ProcessDownloadEvent(string messageId, JsonElement messageData, ref bool downloadStarted, ref string? downloadGuid)
        {
            if (messageId == "Browser.downloadWillBegin")
            {
                downloadStarted = true;
                log.LogLine("Download event detected: Browser.downloadWillBegin");
                ProcessDownloadBeginEvent(messageData, ref downloadGuid);
            }
            else if (messageId == "Browser.downloadProgress")
            {
                ProcessDownloadProgressEvent(messageData);
            }
        }

        /// <summary>
        /// Processes the download begin event to extract GUID information.
        /// </summary>
        /// <param name="data">The message data from the browser event</param>
        /// <param name="downloadGuid">Reference to store the download GUID</param>
        private static void ProcessDownloadBeginEvent(JsonElement data, ref string? downloadGuid)
        {
            if (data.ValueKind == System.Text.Json.JsonValueKind.Object && data.TryGetProperty("guid", out var guidElement))
            {
                downloadGuid = guidElement.GetString();
                log.LogLine($"Download started with GUID: {downloadGuid}");
            }
        }

        /// <summary>
        /// Processes the download progress event to track completion status.
        /// </summary>
        /// <param name="data">The message data from the browser event</param>
        private static void ProcessDownloadProgressEvent(JsonElement data)
        {
            if (data.ValueKind == System.Text.Json.JsonValueKind.Object && data.TryGetProperty("state", out var stateElement))
            {
                var state = stateElement.GetString();
                log.LogLine($"Download progress: {state}");
                if (state == "completed")
                {
                    log.LogLine("Download completed via event notification");
                }
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
        private static async Task<APIGatewayProxyResponse> ProcessAPIGatewayEvent(string requestBody)
        {
            try
            {
                // Handle scheduled events
                if (!string.IsNullOrEmpty(requestBody) && requestBody.Contains(SOURCE_EVENT_TRIGGER))
                {
                    return HandleScheduledEvent();
                }

                // Handle empty request body as bad request
                if (string.IsNullOrEmpty(requestBody))
                {
                    log.LogLine(ERROR_MESSAGE_400 + ERROR_GENERAL);
                    return CreateErrorResponse(HttpStatusCode.BadRequest, ERROR_MESSAGE_400 + ERROR_GENERAL);
                }

                // Parse the request
                var request = ParseApiGatewayRequest(requestBody, log);
                if (request == null)
                {
                    log.LogLine(ERROR_MESSAGE_400 + ERROR_GENERAL);
                    return CreateErrorResponse(HttpStatusCode.BadRequest, ERROR_MESSAGE_400 + ERROR_GENERAL);
                }

                // Route the request
                return await RouteRequest(request);
            }
            catch (Exception e)
            {
                log.LogLine(e.ToString());
                return CreateErrorResponse(HttpStatusCode.InternalServerError, ERROR_MESSAGE_500 + e.Message);
            }
        }

        #region API Gateway Helper Methods

        /// <summary>
        /// Handles scheduled event triggers.
        /// </summary>
        private static APIGatewayProxyResponse HandleScheduledEvent()
        {
            log.LogLine("Scheduled event trigger received.");
            return CreateSuccessResponse(new { msg = MESSAGE_202 });
        }

        /// <summary>
        /// Parses the API Gateway request from JSON.
        /// </summary>
        private static APIGatewayProxyRequest? ParseApiGatewayRequest(string requestBody, ILambdaLogger logger)
        {
            try
            {
                return JsonConvert.DeserializeObject<APIGatewayProxyRequest>(requestBody);
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                logger.LogLine("Failed to deserialize API Gateway request from: " + requestBody + ". Error: " + e.Message);
                // For JSON parsing errors, throw to trigger 500 error
                throw;
            }
            catch (Exception e)
            {
                logger.LogLine("Failed to deserialize API Gateway request from: " + requestBody + ". Error: " + e.Message);
                // For other errors, throw to trigger 500 error
                throw;
            }
        }

        /// <summary>
        /// Routes the request to the appropriate handler based on method and path.
        /// </summary>
        private static async Task<APIGatewayProxyResponse> RouteRequest(APIGatewayProxyRequest request)
        {
            if (!IsValidRequest(request))
            {
                return CreateErrorResponse(HttpStatusCode.BadRequest, ERROR_MESSAGE_400 + ERROR_GENERAL);
            }

            var routeInfo = ExtractRouteInfo(request);
            log.LogLine($"Method: {routeInfo.Method}, Path: {routeInfo.Path}, Route: {routeInfo.Route}");

            // Handle OPTIONS preflight
            if (routeInfo.Method == "OPTIONS")
            {
                return HandleOptionsRequest();
            }

            // Route to specific handlers
            return await RouteToHandler(request, routeInfo);
        }

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

            var route = path;
            if (path.Contains('/'))
            {
                route = path.Substring(0, path.LastIndexOf('/'));
            }

            return (request.HttpMethod!.ToUpper(), path, route);
        }

        /// <summary>
        /// Handles OPTIONS preflight requests.
        /// </summary>
        private static APIGatewayProxyResponse HandleOptionsRequest()
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = null,
                Headers = new Dictionary<string, string> {
                    { "Access-Control-Allow-Methods", "GET,POST,OPTIONS" },
                    { "Access-Control-Allow-Origin", "*" },
                    { "Access-Control-Allow-Headers", "Content-Type,X-Amz-Date,Authorization,x-api-key,X-Api-Key,X-Amz-Security-Token,Origin,Access-Control-Allow-Origin,Access-Control-Allow-Methods"}
                }
            };
        }

        /// <summary>
        /// Routes to the appropriate handler based on method and route.
        /// </summary>
        private static async Task<APIGatewayProxyResponse> RouteToHandler(APIGatewayProxyRequest request, (string Method, string Path, string Route) routeInfo)
        {
            // Check if route is valid
            if (string.IsNullOrEmpty(routeInfo.Route) && (request.QueryStringParameters?.Count ?? 0) == 0)
            {
                return CreateErrorResponse(HttpStatusCode.NotFound, ERROR_MESSAGE_404 + ERROR_INVALID_ROUTE);
            }

            return routeInfo.Method switch
            {
                "POST" when routeInfo.Route == ROUTE_ALARM => await HandleAlarmWebhook(request),
                "GET" when routeInfo.Route == ROUTE_LATEST_VIDEO => await HandleLatestVideoRequest(),
                "GET" => await HandleVideoDownloadRequest(request),
                _ => CreateInvalidRouteResponse(routeInfo.Route, routeInfo.Method)
            };
        }

        /// <summary>
        /// Handles alarm webhook POST requests.
        /// </summary>
        private static async Task<APIGatewayProxyResponse> HandleAlarmWebhook(APIGatewayProxyRequest request)
        {
            if (string.IsNullOrEmpty(request.Body))
            {
                return CreateErrorResponse(HttpStatusCode.BadRequest, ERROR_MESSAGE_400 + ERROR_GENERAL);
            }

            log.LogLine("Request: " + request.Body);

            var alarm = ParseAlarmFromRequest(request.Body);
            if (alarm == null)
            {
                return CreateErrorResponse(HttpStatusCode.BadRequest, ERROR_MESSAGE_400 + "Invalid alarm object format");
            }

            return await QueueAlarmForProcessing(alarm);
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
        private static async Task<APIGatewayProxyResponse> HandleLatestVideoRequest()
        {
            return await GetLatestVideoFunction();
        }

        /// <summary>
        /// Handles video download GET requests.
        /// </summary>
        private static async Task<APIGatewayProxyResponse> HandleVideoDownloadRequest(APIGatewayProxyRequest request)
        {
            var eventId = request.QueryStringParameters?.TryGetValue("eventId", out var value) == true ? value : null;
            
            if (string.IsNullOrEmpty(eventId))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = JsonConvert.SerializeObject(new { msg = "Missing required parameter. Provide 'eventId' for video download." }),
                    Headers = GetStandardHeaders()
                };
            }

            return await GetVideoByEventIdFunction(eventId);
        }

        /// <summary>
        /// Creates an invalid route response.
        /// </summary>
        private static APIGatewayProxyResponse CreateInvalidRouteResponse(string route, string method)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.NotFound,
                Body = JsonConvert.SerializeObject(new { msg = $"{ERROR_MESSAGE_404} {ERROR_INVALID_ROUTE}. Route: {route}, Method: {method}" }),
                Headers = GetStandardHeaders()
            };
        }

        /// <summary>
        /// Creates a standard error response.
        /// </summary>
        private static APIGatewayProxyResponse CreateErrorResponse(HttpStatusCode statusCode, string message)
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
        private static APIGatewayProxyResponse CreateSuccessResponse(object body)
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
        private static APIGatewayProxyResponse CreateSuccessResponse(Trigger trigger, long timestamp)
        {
            DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            string date = string.Format("{0:s}", dt);
            
            string bodyContent = FUNCTION_NAME + "has successfully processed the Unifi alarm event webhook with key " + trigger.eventKey +
                " for " + trigger.deviceName + " that occurred at " + date + ".";
            
            log.LogLine("Returning response: " + bodyContent);
            
            return CreateSuccessResponse(new { msg = bodyContent });
        }

        /// <summary>
        /// Gets standard CORS headers for responses.
        /// </summary>
        private static Dictionary<string, string> GetStandardHeaders()
        {
            return new Dictionary<string, string> 
            { 
                { "Content-Type", "application/json" }, 
                { "Access-Control-Allow-Origin", "*" } 
            };
        }

        #endregion


        #endregion

        #region SQS Queue Operations

        /// <summary>
        /// Queues an alarm event for delayed processing via SQS.
        /// 
        /// This method sends the alarm event to an SQS queue with a configurable delay
        /// to allow time for video files to become fully available in Unifi Protect
        /// before attempting to download them. This improves reliability and reduces
        /// the likelihood of download failures due to timing issues.
        /// </summary>
        /// <param name="alarm">Alarm event to queue for processing</param>
        /// <returns>API Gateway response indicating the event has been queued</returns>
        [ExcludeFromCodeCoverage] // Requires AWS SQS connectivity
        public static async Task<APIGatewayProxyResponse> QueueAlarmForProcessing(Alarm alarm)
        {
            log.LogLine("Queueing alarm event for delayed processing");

            try
            {
                if (string.IsNullOrEmpty(ALARM_PROCESSING_QUEUE_URL))
                {
                    log.LogLine("AlarmProcessingQueueUrl environment variable is not configured");
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = (int)HttpStatusCode.InternalServerError,
                        Body = JsonConvert.SerializeObject(new { msg = "Server configuration error: SQS queue not configured" }),
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                    };
                }

                // Serialize the alarm for the queue message
                string messageBody = JsonConvert.SerializeObject(alarm);
                
                // Get the first trigger for event details
                var trigger = alarm.triggers?.FirstOrDefault();
                string eventId = trigger?.eventId ?? "unknown";
                string device = trigger?.device ?? "unknown";

                // Send message to SQS with delay
                var sendMessageRequest = new SendMessageRequest
                {
                    QueueUrl = ALARM_PROCESSING_QUEUE_URL,
                    MessageBody = messageBody,
                    DelaySeconds = PROCESSING_DELAY_SECONDS,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        ["EventId"] = new MessageAttributeValue
                        {
                            DataType = "String",
                            StringValue = eventId
                        },
                        ["Device"] = new MessageAttributeValue
                        {
                            DataType = "String", 
                            StringValue = device
                        },
                        ["Timestamp"] = new MessageAttributeValue
                        {
                            DataType = "Number",
                            StringValue = alarm.timestamp.ToString()
                        }
                    }
                };

                var result = await sqsClient.SendMessageAsync(sendMessageRequest);
                
                log.LogLine($"Successfully queued alarm event {eventId} for processing in {PROCESSING_DELAY_SECONDS} seconds. MessageId: {result.MessageId}");

                // Return immediate success response
                var responseData = new
                {
                    msg = $"Alarm event has been queued for processing",
                    eventId = eventId,
                    device = device,
                    processingDelay = PROCESSING_DELAY_SECONDS,
                    messageId = result.MessageId,
                    estimatedProcessingTime = DateTime.UtcNow.AddSeconds(PROCESSING_DELAY_SECONDS).ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.OK,
                    Body = JsonConvert.SerializeObject(responseData),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                };
            }
            catch (Exception e)
            {
                log.LogLine($"Error queueing alarm for processing: {e.Message}");
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Body = JsonConvert.SerializeObject(new { msg = $"Error queueing alarm for processing: {e.Message}" }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                };
            }
        }

        #endregion

        #region Alarm Event Processing

        /// <summary>
        /// Processes Unifi Protect alarm events and stores them in S3.
        /// 
        /// This method handles the core business logic for processing alarm webhook events from
        /// Unifi Protect systems. It validates the alarm data, extracts device and trigger information,
        /// maps device MAC addresses to human-readable names using environment variables, and stores
        /// the complete alarm data as JSON in S3 with a date-organized folder structure.
        /// 
        /// The S3 key format is: YYYY-MM-DD/{deviceMac}_{timestamp}.json
        /// 
        /// Device name mapping is performed using environment variables with the pattern:
        /// {DEVICE_PREFIX}{deviceMac} = "Human Readable Device Name"
        /// </summary>
        /// <param name="alarm">Parsed alarm object containing triggers, device info, and event details</param>
        /// <returns>API Gateway response indicating success or failure of alarm processing</returns>
        [ExcludeFromCodeCoverage] // Requires AWS S3 and Secrets Manager connectivity
        public static async Task<APIGatewayProxyResponse> AlarmReceiverFunction(Alarm alarm)
        {
            log.LogLine("Executing alarm receiver function.");

            try
            {
                // Get Unifi credentials from Secrets Manager
                var credentials = await GetUnifiCredentialsAsync();

                // Validate the alarm object
                var validationResponse = ValidateAlarmObject(alarm);
                if (validationResponse != null)
                {
                    return validationResponse;
                }

                // Process the alarm and upload to S3
                return await ProcessValidAlarm(alarm!, credentials);
            }
            catch (Exception e)
            {
                // Return response
                log.LogLine(ERROR_MESSAGE_500 + e.Message);
                return CreateErrorResponse(HttpStatusCode.BadRequest, ERROR_MESSAGE_500 + e.Message);
            }
        }

        /// <summary>
        /// Validates the alarm object and returns an error response if invalid.
        /// </summary>
        /// <param name="alarm">The alarm object to validate</param>
        /// <returns>Error response if invalid, null if valid</returns>
        private static APIGatewayProxyResponse? ValidateAlarmObject(Alarm? alarm)
        {
            if (alarm == null)
            {
                log.LogLine("Error: " + ERROR_GENERAL);
                return CreateErrorResponse(HttpStatusCode.BadRequest, ERROR_MESSAGE_400 + ERROR_GENERAL);
            }

            log.LogLine("Alarm: " + JsonConvert.SerializeObject(alarm));

            if (alarm.triggers == null || alarm.triggers.Count == 0)
            {
                log.LogLine("Error: " + ERROR_TRIGGERS);
                return CreateErrorResponse(HttpStatusCode.BadRequest, ERROR_MESSAGE_400 + ERROR_TRIGGERS);
            }

            return null; // Valid alarm
        }

        /// <summary>
        /// Processes a valid alarm by extracting event details, mapping device names, and uploading to S3.
        /// </summary>
        /// <param name="alarm">The validated alarm object</param>
        /// <param name="credentials">Unifi credentials for video download</param>
        /// <returns>Success response with processed alarm details</returns>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private static async Task<APIGatewayProxyResponse> ProcessValidAlarm(Alarm alarm, UnifiCredentials credentials)
        {
            // Extract and enhance trigger information
            var trigger = ExtractAndEnhanceTriggerDetails(alarm);
            
            // Validate storage configuration
            if (string.IsNullOrEmpty(ALARM_BUCKET_NAME))
            {
                log.LogLine("StorageBucket environment variable is not configured");
                return CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured");
            }

            // Create file keys for S3 storage
            var (eventFileKey, videoFileKey) = CreateS3FileKeys(trigger, alarm.timestamp);

            // Upload alarm data and video to S3
            await UploadAlarmDataToS3(alarm, eventFileKey);
            await UploadVideoDataToS3(alarm, credentials, trigger.deviceName ?? "", videoFileKey);

            // Return success response
            return CreateSuccessResponse(trigger, alarm.timestamp);
        }

        /// <summary>
        /// Extracts trigger details and enhances with device mapping and timestamps.
        /// </summary>
        /// <param name="alarm">The alarm object containing trigger data</param>
        /// <returns>Enhanced trigger object with device name and date information</returns>
        private static Trigger ExtractAndEnhanceTriggerDetails(Alarm alarm)
        {
            Trigger trigger = alarm.triggers[0];
            string device = trigger.device;
            long timestamp = alarm.timestamp;
            string eventId = trigger.eventId;

            // Set date from timestamp
            DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            string date = string.Format("{0:s}", dt);
            trigger.date = date;
            log.LogInformation("Date: " + date);

            // Map device Mac to device name and update object
            string deviceName = GetDeviceNameFromMac(device);
            trigger.deviceName = deviceName;

            // Set event key and update alarm object
            string videoKey = eventId + "_" + device + "_" + timestamp.ToString() + ".mp4";
            string eventKey = eventId + "_" + device + "_" + timestamp.ToString() + ".json";
            trigger.eventKey = eventKey;
            trigger.videoKey = videoKey;
            alarm.triggers[0] = trigger;

            return trigger;
        }

        /// <summary>
        /// Gets the device name from the MAC address using environment variables.
        /// </summary>
        /// <param name="deviceMac">The device MAC address</param>
        /// <returns>The device name if found, empty string otherwise</returns>
        private static string GetDeviceNameFromMac(string deviceMac)
        {
            IDictionary envVars = Environment.GetEnvironmentVariables();
            if (envVars?[DEVICE_PREFIX + deviceMac] is string deviceName)
            {
                log.LogLine("Device name found for " + deviceMac + ": " + deviceName);
                return deviceName;
            }
            return "";
        }

        /// <summary>
        /// Creates S3 file keys for both event JSON and video file with date-based folder structure.
        /// </summary>
        /// <param name="trigger">The trigger containing event and video key information</param>
        /// <param name="timestamp">The event timestamp for date folder creation</param>
        /// <returns>Tuple containing event file key and video file key</returns>
        private static (string eventFileKey, string videoFileKey) CreateS3FileKeys(Trigger trigger, long timestamp)
        {
            DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            string dateFolder = $"{dt.Year}-{dt.Month:D2}-{dt.Day:D2}";
            
            string eventFileKey = $"{dateFolder}/{trigger.eventKey}";
            string videoFileKey = $"{dateFolder}/{trigger.videoKey}";
            
            return (eventFileKey, videoFileKey);
        }

        /// <summary>
        /// Uploads alarm event data to S3.
        /// </summary>
        /// <param name="alarm">The alarm object to serialize and upload</param>
        /// <param name="eventFileKey">The S3 key for the event file</param>
        private static async Task UploadAlarmDataToS3(Alarm alarm, string eventFileKey)
        {
            await UploadFileAsync(ALARM_BUCKET_NAME!, eventFileKey, JsonConvert.SerializeObject(alarm));
        }

        /// <summary>
        /// Downloads video from Unifi Protect and uploads to S3.
        /// </summary>
        /// <param name="alarm">The alarm object containing event path</param>
        /// <param name="credentials">Unifi credentials for video download</param>
        /// <param name="deviceName">The device name for logging</param>
        /// <param name="videoFileKey">The S3 key for the video file</param>
        private static async Task UploadVideoDataToS3(Alarm alarm, UnifiCredentials credentials, string deviceName, string videoFileKey)
        {
            string eventLocalLink = credentials.hostname + alarm.eventPath;
            byte[] videoData = await GetVideoFromLocalUnifiProtectViaHeadlessClient(eventLocalLink, deviceName, credentials);
            await UploadFileAsync(ALARM_BUCKET_NAME!, videoFileKey, videoData, "video/mp4");
        }

        #endregion

        #region S3 Storage Operations

        /// <summary>
        /// Uploads alarm event data to S3 as a JSON object.
        /// 
        /// This method handles the storage of processed alarm events in the configured S3 bucket.
        /// The content is stored as a JSON string with appropriate error handling for AWS S3 operations.
        /// </summary>
        /// <param name="bucketName">Target S3 bucket name for storage</param>
        /// <param name="keyName">S3 object key (file path within bucket)</param>
        /// <param name="obj">JSON string content to store</param>
        /// <returns>Task representing the asynchronous upload operation</returns>
        private static async Task UploadFileAsync(string bucketName, string keyName, string obj)
        {
            await UploadFileAsync(bucketName, keyName, obj, "application/json");
        }

        /// <summary>
        /// Uploads string content to S3 with specified content type.
        /// </summary>
        /// <param name="bucketName">Target S3 bucket name for storage</param>
        /// <param name="keyName">S3 object key (file path within bucket)</param>
        /// <param name="content">String content to store</param>
        /// <param name="contentType">MIME type of the content</param>
        /// <returns>Task representing the asynchronous upload operation</returns>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private static async Task UploadFileAsync(string bucketName, string keyName, string content, string contentType)
        {
            try
            {
                // Prepare request
                var putObjectRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName,
                    ContentBody = content,
                    ContentType = contentType,
                    StorageClass = S3StorageClass.StandardInfrequentAccess // Optimize for infrequent access
                };

                // Upload the object
                await s3Client.PutObjectAsync(putObjectRequest);
                log.LogLine("Successfully wrote the object to S3: " + bucketName + "/" + keyName);
            }
            catch (AmazonS3Exception e)
            {
                log.LogLine("Error encountered on object write: " + e.Message);
                log.LogLine(e.ToString());
            }
            catch (Exception e)
            {
                log.LogLine("Unknown encountered when writing an object: " + e.Message);
                log.LogLine(e.ToString());
            }
        }

        /// <summary>
        /// Uploads binary data to S3 with specified content type.
        /// </summary>
        /// <param name="bucketName">Target S3 bucket name for storage</param>
        /// <param name="keyName">S3 object key (file path within bucket)</param>
        /// <param name="data">Binary data to store</param>
        /// <param name="contentType">MIME type of the content</param>
        /// <returns>Task representing the asynchronous upload operation</returns>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private static async Task UploadFileAsync(string bucketName, string keyName, byte[] data, string contentType)
        {
            try
            {
                using var stream = new MemoryStream(data);

                // Prepare request
                var putObjectRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName,
                    InputStream = stream,
                    ContentType = contentType,
                    StorageClass = S3StorageClass.StandardInfrequentAccess // Optimize for infrequent access
                };

                // Upload the object
                await s3Client.PutObjectAsync(putObjectRequest);
                log.LogLine($"Successfully wrote the object to S3: {bucketName}/{keyName} ({data.Length} bytes)");
            }
            catch (AmazonS3Exception e)
            {
                log.LogLine("Error encountered on object write: " + e.Message);
                log.LogLine(e.ToString());
            }
            catch (Exception e)
            {
                log.LogLine("Unknown encountered when writing an object: " + e.Message);
                log.LogLine(e.ToString());
            }
        }


        #endregion

        #region Event Retrieval Operations
        /// Retrieves JSON content from S3 and returns it as a string.
        /// 
        /// This method handles the low-level S3 operations for fetching stored alarm event data.
        /// It performs the S3 GetObject operation, reads the response stream, and converts
        /// the binary content back to a UTF-8 string for JSON processing.
        /// 
        /// Handles common S3 exceptions including missing objects (NoSuchKey) and access errors.
        /// </summary>
        /// <param name="keyName">S3 object key to retrieve</param>
        /// <returns>JSON string content of the S3 object, or null if object doesn't exist</returns>
        private static async Task<String?> GetJsonFileFromS3BlobAsync(string keyName)
        {
            byte[] fileBytes;

            try
            {
                if (string.IsNullOrEmpty(ALARM_BUCKET_NAME))
                {
                    throw new InvalidOperationException("StorageBucket environment variable is not configured");
                }

                log.LogLine("Attempting to get object: " + keyName + " from " + ALARM_BUCKET_NAME + ".");

                // Prepare request
                var getObjectRequest = new GetObjectRequest
                {
                    BucketName = ALARM_BUCKET_NAME,
                    Key = keyName,
                };

                // Get the object
                MemoryStream ms = new MemoryStream();
                using (GetObjectResponse response = await s3Client.GetObjectAsync(getObjectRequest))
                using (Stream responseStream = response.ResponseStream)
                    await responseStream.CopyToAsync(ms);
                fileBytes = ms.ToArray();

                // Return the file byte array
                log.LogLine("Successfully retrieved the object from S3: " + ALARM_BUCKET_NAME + "/" + keyName + " with a size of: " + fileBytes.Length + " bytes");
                string objectString = Encoding.UTF8.GetString(fileBytes, 0, fileBytes.Length);
                return objectString;
            }
            catch (AmazonS3Exception e)
            {
                if (e.ErrorCode == "NoSuchKey")
                {
                    log.LogLine("Object doesn't exist.");
                    return null;
                }
                else
                {
                    log.LogLine("Error encountered while reading object from S3: " + e.Message);
                    throw new InvalidOperationException("Error encountered while getting file from S3.", e);
                }
            }
            catch (Exception e)
            {
                log.LogLine("Unknown encountered on server. Message:'{0}' when reading an object" + e.Message);
                throw new InvalidOperationException("Error encountered while getting file from S3.", e);
            }
        }

        /// <summary>
        /// Retrieves the latest video from S3 and returns a presigned URL for download along with event details.
        /// 
        /// This method efficiently searches through date-organized folders in S3 to find the most recent
        /// video file (.mp4) based on the timestamp in the filename. It starts from today's date folder
        /// and works backwards day by day until a video is found, making it much more efficient than
        /// scanning all objects in the bucket.
        /// 
        /// Instead of returning the video data directly (which would exceed API Gateway's 6MB limit),
        /// this method returns a presigned URL that allows direct download from S3. The URL expires
        /// after 1 hour for security purposes.
        /// 
        /// Additionally, this method retrieves the corresponding event JSON data (alarm details, device
        /// information, trigger types, etc.) by looking up the matching .json file and includes it in
        /// the response to provide complete context about the video.
        /// 
        /// The search looks through folders in YYYY-MM-DD format and finds files matching
        /// the pattern {deviceMac}_{timestamp}.mp4, returning metadata, download URL, and event details 
        /// for the one with the highest timestamp from the most recent date that contains videos.
        /// </summary>
        /// <returns>API Gateway response containing download URL, metadata, and event details, or error message</returns>
        /// <summary>
        /// Retrieves the latest video from S3 and returns a presigned URL for download along with event details.
        /// 
        /// This method efficiently searches through date-organized folders in S3 to find the most recent
        /// video file (.mp4) based on the timestamp in the filename. It starts from today's date folder
        /// and works backwards day by day until a video is found, making it much more efficient than
        /// scanning all objects in the bucket.
        /// 
        /// Instead of returning the video data directly (which would exceed API Gateway's 6MB limit),
        /// this method returns a presigned URL that allows direct download from S3. The URL expires
        /// after 1 hour for security purposes.
        /// 
        /// Additionally, this method retrieves the corresponding event JSON data (alarm details, device
        /// information, trigger types, etc.) by looking up the matching .json file and includes it in
        /// the response to provide complete context about the video.
        /// 
        /// The search looks through folders in YYYY-MM-DD format and finds files matching
        /// the pattern {deviceMac}_{timestamp}.mp4, returning metadata, download URL, and event details 
        /// for the one with the highest timestamp from the most recent date that contains videos.
        /// </summary>
        /// <returns>API Gateway response containing download URL, metadata, and event details, or error message</returns>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        public static async Task<APIGatewayProxyResponse> GetLatestVideoFunction()
        {
            log.LogLine("Executing Get latest video function");

            try
            {
                // Validate configuration
                var configError = ValidateLatestVideoConfiguration();
                if (configError != null) return configError;

                // Search for the latest video
                var searchResult = await SearchForLatestVideoAsync();
                if (searchResult.ErrorResponse != null) return searchResult.ErrorResponse;

                // Verify video exists
                var verificationError = await VerifyVideoExistsAsync(searchResult.VideoKey!);
                if (verificationError != null) return verificationError;

                // Get event data
                var eventData = await RetrieveEventDataAsync(searchResult.VideoKey!);

                // Build and return response
                return await BuildLatestVideoResponse(searchResult.VideoKey!, searchResult.Timestamp, eventData);
            }
            catch (Exception e)
            {
                log.LogLine($"Error retrieving latest video: {e.Message}");
                return CreateErrorResponse(HttpStatusCode.InternalServerError, $"Error retrieving latest video: {e.Message}");
            }
        }

        /// <summary>
        /// Validates the configuration required for latest video retrieval.
        /// </summary>
        /// <returns>Error response if configuration is invalid, null if valid</returns>
        internal static APIGatewayProxyResponse? ValidateLatestVideoConfiguration()
        {
            var storageBucket = Environment.GetEnvironmentVariable("StorageBucket");
            if (string.IsNullOrWhiteSpace(storageBucket))
            {
                log.LogLine("StorageBucket environment variable is not configured");
                return CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured");
            }
            return null;
        }

        /// <summary>
        /// Searches for the latest video file in S3 using date-organized folder structure.
        /// </summary>
        /// <returns>Search result containing video key and timestamp, or error response</returns>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        internal static async Task<(string? VideoKey, long Timestamp, APIGatewayProxyResponse? ErrorResponse)> SearchForLatestVideoAsync()
        {
            log.LogLine("Searching for latest video file in S3 bucket using date-organized approach: " + ALARM_BUCKET_NAME);

            DateTime searchDate = DateTime.UtcNow.Date;
            const int maxDaysToSearch = 30;
            int daysSearched = 0;

            while (daysSearched < maxDaysToSearch)
            {
                string dateFolder = searchDate.ToString("yyyy-MM-dd");
                log.LogLine($"Searching for videos in date folder: {dateFolder}");

                var dayResult = await SearchDateFolderForLatestVideoAsync(dateFolder);
                if (dayResult.VideoKey != null)
                {
                    log.LogLine($"Found latest video in {dateFolder}: {dayResult.VideoKey} with timestamp {dayResult.Timestamp}");
                    return (dayResult.VideoKey, dayResult.Timestamp, null);
                }

                // Move to previous day
                searchDate = searchDate.AddDays(-1);
                daysSearched++;
                log.LogLine($"No videos found in {dateFolder}, moving to previous day: {searchDate:yyyy-MM-dd}");
            }

            log.LogLine("No video files found in S3 bucket");
            return (null, 0, CreateErrorResponse(HttpStatusCode.NotFound, "No video files found"));
        }

        /// <summary>
        /// Searches a specific date folder for the latest video file.
        /// </summary>
        /// <param name="dateFolder">The date folder to search (YYYY-MM-DD format)</param>
        /// <returns>Video key and timestamp if found, null otherwise</returns>
        internal static async Task<(string? VideoKey, long Timestamp)> SearchDateFolderForLatestVideoAsync(string dateFolder)
        {
            var listRequest = new ListObjectsV2Request
            {
                BucketName = ALARM_BUCKET_NAME!,
                Prefix = dateFolder + "/",
                MaxKeys = 1000 // Should be plenty for a single day
            };

            string? latestVideoKey = null;
            long latestTimestamp = 0;

            do
            {
                var response = await s3Client.ListObjectsV2Async(listRequest);

                var videoTimestamps = response.S3Objects
                    .Where(obj => obj.Key.EndsWith(".mp4"))
                    .Select(obj => obj.Key)
                    .Select(key => new { Key = key, Timestamp = ExtractTimestampFromFileName(key) })
                    .Where(item => item.Timestamp > 0);

                foreach (var item in videoTimestamps)
                {
                    if (item.Timestamp > latestTimestamp)
                    {
                        latestTimestamp = item.Timestamp;
                        latestVideoKey = item.Key;
                    }
                }

                listRequest.ContinuationToken = response.NextContinuationToken;
            } while (listRequest.ContinuationToken != null);

            return (latestVideoKey, latestTimestamp);
        }

        /// <summary>
        /// Extracts timestamp from a video file name.
        /// </summary>
        /// <param name="s3Key">The S3 key containing the filename</param>
        /// <returns>Timestamp if successfully extracted, 0 otherwise</returns>
        internal static long ExtractTimestampFromFileName(string s3Key)
        {
            var fileName = Path.GetFileName(s3Key);
            var underscoreIndex = fileName.LastIndexOf('_');
            var dotIndex = fileName.LastIndexOf('.');

            if (underscoreIndex > 0 && dotIndex > underscoreIndex)
            {
                var timestampStr = fileName.Substring(underscoreIndex + 1, dotIndex - underscoreIndex - 1);
                if (long.TryParse(timestampStr, out var timestamp) && timestamp >= 0)
                {
                    return timestamp;
                }
            }
            return 0;
        }

        /// <summary>
        /// Verifies that a video file exists in S3.
        /// </summary>
        /// <param name="videoKey">The S3 key of the video file</param>
        /// <returns>Error response if video doesn't exist, null if it exists</returns>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        internal static async Task<APIGatewayProxyResponse?> VerifyVideoExistsAsync(string videoKey)
        {
            try
            {
                var headRequest = new GetObjectMetadataRequest
                {
                    BucketName = ALARM_BUCKET_NAME!,
                    Key = videoKey
                };
                var metadata = await s3Client.GetObjectMetadataAsync(headRequest);
                log.LogLine($"Video file confirmed in S3: {videoKey} ({metadata.ContentLength} bytes)");
                return null;
            }
            catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey")
            {
                log.LogLine($"Video file {videoKey} not found in S3");
                return CreateErrorResponse(HttpStatusCode.NotFound, "Video file not found");
            }
        }

        /// <summary>
        /// Retrieves event data associated with a video file.
        /// </summary>
        /// <param name="videoKey">The S3 key of the video file</param>
        /// <returns>Parsed event data object, or null if not found</returns>
        internal static async Task<object?> RetrieveEventDataAsync(string videoKey)
        {
            string eventKey = videoKey.Replace(".mp4", ".json");
            log.LogLine($"Looking for corresponding event data: {eventKey}");

            try
            {
                string? eventJsonData = await GetJsonFileFromS3BlobAsync(eventKey);
                if (eventJsonData != null)
                {
                    var eventData = JsonConvert.DeserializeObject(eventJsonData);
                    log.LogLine($"Successfully retrieved event data for {eventKey}");
                    return eventData;
                }
                else
                {
                    log.LogLine($"No event data found for {eventKey}");
                }
            }
            catch (Exception ex)
            {
                log.LogLine($"Error retrieving event data for {eventKey}: {ex.Message}");
                // Continue without event data rather than failing the entire request
            }

            return null;
        }

        /// <summary>
        /// Builds the API Gateway response for latest video request.
        /// </summary>
        /// <param name="videoKey">The S3 key of the video file</param>
        /// <param name="timestamp">The timestamp of the video</param>
        /// <param name="eventData">Associated event data</param>
        /// <returns>API Gateway response with video download information</returns>
        internal static async Task<APIGatewayProxyResponse> BuildLatestVideoResponse(string videoKey, long timestamp, object? eventData)
        {
            DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
            string suggestedFilename = $"latest_video_{dt:yyyy-MM-dd_HH-mm-ss}.mp4";
            string eventKey = videoKey.Replace(".mp4", ".json");

            // Generate presigned URL with 1 hour expiration and suggested filename
            var presignedRequest = new GetPreSignedUrlRequest
            {
                BucketName = ALARM_BUCKET_NAME!,
                Key = videoKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddHours(1),
                ResponseHeaderOverrides = new ResponseHeaderOverrides
                {
                    ContentDisposition = $"attachment; filename=\"{suggestedFilename}\""
                }
            };

            string presignedUrl = await s3Client.GetPreSignedURLAsync(presignedRequest);
            log.LogLine($"Generated presigned URL for {videoKey}, expires in 1 hour");

            var responseData = new
            {
                downloadUrl = presignedUrl,
                filename = suggestedFilename,
                videoKey = videoKey,
                eventKey = eventKey,
                timestamp = timestamp,
                eventDate = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                expiresAt = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss UTC"),
                eventData = eventData,
                message = "Use the downloadUrl to download the video file directly. URL expires in 1 hour."
            };

            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(responseData, Formatting.Indented),
                Headers = GetStandardHeaders()
            };
        }

        /// <summary>
        /// Retrieves a video by eventId and returns a presigned URL for download along with event details.
        /// 
        /// This method uses the new naming convention where files are named with eventId as prefix
        /// (e.g., {eventId}_{device}_{timestamp}.json) to efficiently find event files without 
        /// searching through JSON content. It searches through date-organized folders looking for 
        /// files that start with the specified eventId.
        /// 
        /// Returns a presigned URL for direct video download from S3 along with complete event metadata
        /// including device information, trigger details, and alarm context.
        /// </summary>
        /// <param name="eventId">The Unifi Protect event ID to search for</param>
        /// <returns>API Gateway response containing download URL, metadata, and event details, or error message</returns>
        public static async Task<APIGatewayProxyResponse> GetVideoByEventIdFunction(string eventId)
        {
            log.LogLine($"Executing Get video by eventId function for eventId: {eventId}");

            try
            {
                // Validate configuration and parameters
                var configValidation = ValidateEventIdConfiguration(eventId);
                if (configValidation != null) return configValidation;

                // Search for the event
                var searchResult = await SearchEventByIdAsync(eventId);
                if (searchResult.ErrorResponse != null) return searchResult.ErrorResponse;

                // Verify video file exists
                var verificationResult = await VerifyVideoFileExistsAsync(searchResult.VideoKey!, eventId);
                if (verificationResult != null) return verificationResult;

                // Retrieve event data (optional, don't fail if missing)
                var eventData = await RetrieveEventDataAsync(searchResult.EventKey!);

                // Generate presigned URL and build response
                return await BuildEventVideoResponse(searchResult.EventKey!, searchResult.VideoKey!, 
                    eventId, searchResult.Timestamp, eventData);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
            {
                log.LogLine($"Video or event data not found in S3 for eventId {eventId}: {ex.Message}");
                return CreateErrorResponse(HttpStatusCode.NotFound, 
                    $"Video file for event {eventId} is not available. The video may have been automatically deleted due to the 30-day retention policy or the video download may have failed during event processing.");
            }
            catch (AmazonS3Exception ex)
            {
                log.LogLine($"S3 error retrieving video by eventId {eventId}: {ex.ErrorCode} - {ex.Message}");
                return CreateErrorResponse(HttpStatusCode.InternalServerError, 
                    $"Storage service error while retrieving event {eventId}. Please try again later.");
            }
            catch (Exception e)
            {
                log.LogLine($"Unexpected error retrieving video by eventId {eventId}: {e.Message}");
                return CreateErrorResponse(HttpStatusCode.InternalServerError, 
                    $"An unexpected error occurred while retrieving event {eventId}. Please try again later.");
            }
        }

        /// <summary>
        /// Validates configuration and parameters for event ID video retrieval.
        /// </summary>
        internal static APIGatewayProxyResponse? ValidateEventIdConfiguration(string eventId)
        {
            if (string.IsNullOrEmpty(ALARM_BUCKET_NAME))
            {
                log.LogLine("StorageBucket environment variable is not configured");
                return CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured");
            }

            if (string.IsNullOrEmpty(eventId))
            {
                log.LogLine("EventId parameter is required");
                return CreateErrorResponse(HttpStatusCode.BadRequest, "EventId parameter is required");
            }

            return null;
        }

        /// <summary>
        /// Searches for an event by ID across date-organized folders.
        /// </summary>
        internal static async Task<(string? EventKey, string? VideoKey, long Timestamp, APIGatewayProxyResponse? ErrorResponse)> SearchEventByIdAsync(string eventId)
        {
            log.LogLine($"Searching for event file with eventId prefix: {eventId}");

            // Start from today and work backwards day by day to find the event
            DateTime searchDate = DateTime.UtcNow.Date;
            const int maxDaysToSearch = 90; // Search up to 90 days back
            int daysSearched = 0;

            while (daysSearched < maxDaysToSearch)
            {
                string dateFolder = searchDate.ToString("yyyy-MM-dd");
                log.LogLine($"Searching for eventId {eventId} in date folder: {dateFolder}");

                var result = await SearchEventInDateFolderAsync(eventId, dateFolder);
                if (result.EventKey != null)
                {
                    return (result.EventKey, result.VideoKey, result.Timestamp, null);
                }

                // Move to previous day
                searchDate = searchDate.AddDays(-1);
                daysSearched++;
            }

            log.LogLine($"Event with eventId {eventId} not found in S3 bucket");
            return (null, null, 0, CreateErrorResponse(HttpStatusCode.NotFound, $"Event with eventId {eventId} not found"));
        }

        /// <summary>
        /// Searches for an event within a specific date folder.
        /// </summary>
        internal static async Task<(string? EventKey, string? VideoKey, long Timestamp)> SearchEventInDateFolderAsync(string eventId, string dateFolder)
        {
            var listRequest = new ListObjectsV2Request
            {
                BucketName = ALARM_BUCKET_NAME!,
                Prefix = $"{dateFolder}/{eventId}_", // Look for files that start with eventId_
                MaxKeys = 10 // Should only be 1-2 files (JSON + MP4) per event
            };

            var response = await s3Client.ListObjectsV2Async(listRequest);

            var eventFile = response.S3Objects
                .Where(o => o.Key.EndsWith(".json"))
                .Select(obj => obj.Key)
                .FirstOrDefault();

            if (eventFile != null)
            {
                string eventKey = eventFile;
                string videoKey = eventFile.Replace(".json", ".mp4");
                long timestamp = ExtractTimestampFromEventFileName(eventFile);

                log.LogLine($"Found event file: {eventKey}, corresponding video: {videoKey}");
                return (eventKey, videoKey, timestamp);
            }

            return (null, null, 0);
        }

        /// <summary>
        /// Extracts timestamp from an event filename.
        /// Expected format: {dateFolder}/{eventId}_{device}_{timestamp}.json
        /// </summary>
        internal static long ExtractTimestampFromEventFileName(string eventKey)
        {
            var fileName = Path.GetFileName(eventKey);
            var parts = fileName.Split('_');
            
            if (parts.Length >= 3 && long.TryParse(parts[parts.Length - 1].Replace(".json", ""), out long timestamp) && timestamp >= 0)
            {
                return timestamp;
            }
            
            return 0;
        }

        /// <summary>
        /// Verifies that a video file exists in S3.
        /// </summary>
        internal static async Task<APIGatewayProxyResponse?> VerifyVideoFileExistsAsync(string videoKey, string eventId)
        {
            try
            {
                var headRequest = new GetObjectMetadataRequest
                {
                    BucketName = ALARM_BUCKET_NAME!,
                    Key = videoKey
                };
                var metadata = await s3Client.GetObjectMetadataAsync(headRequest);
                log.LogLine($"Video file confirmed in S3: {videoKey} ({metadata.ContentLength} bytes)");
                return null;
            }
            catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey")
            {
                log.LogLine($"Video file {videoKey} not found in S3");
                return CreateErrorResponse(HttpStatusCode.NotFound, $"Video file for event {eventId} not found");
            }
        }

        /// <summary>
        /// Builds the response for event video retrieval with presigned URL.
        /// </summary>
        internal static async Task<APIGatewayProxyResponse> BuildEventVideoResponse(string eventKey, string videoKey, 
            string eventId, long timestamp, object? eventData)
        {
            try
            {
                // Generate a presigned URL for direct download from S3
                DateTime dt = timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime : DateTime.UtcNow;
                string suggestedFilename = $"event_{eventId}_{dt:yyyy-MM-dd_HH-mm-ss}.mp4";
                
                // Generate presigned URL with 1 hour expiration and suggested filename
                var presignedRequest = new GetPreSignedUrlRequest
                {
                    BucketName = ALARM_BUCKET_NAME!,
                    Key = videoKey,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddHours(1),
                    ResponseHeaderOverrides = new ResponseHeaderOverrides
                    {
                        ContentDisposition = $"attachment; filename=\"{suggestedFilename}\""
                    }
                };

                string presignedUrl = await s3Client.GetPreSignedURLAsync(presignedRequest);
                log.LogLine($"Generated presigned URL for {videoKey}, expires in 1 hour");

                // Return the presigned URL, metadata, and event data
                var responseData = new
                {
                    downloadUrl = presignedUrl,
                    filename = suggestedFilename,
                    videoKey = videoKey,
                    eventKey = eventKey,
                    eventId = eventId,
                    timestamp = timestamp,
                    eventDate = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                    expiresAt = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    eventData = eventData,
                    message = "Use the downloadUrl to download the video file directly. URL expires in 1 hour."
                };

                return CreateSuccessResponse(responseData);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
            {
                log.LogLine($"Video file {videoKey} not found in S3 when generating presigned URL for event {eventId}");
                return CreateErrorResponse(HttpStatusCode.NotFound, 
                    $"Video file for event {eventId} is not available. The video may have been automatically deleted due to the 30-day retention policy or the video download may have failed during event processing.");
            }
            catch (AmazonS3Exception ex)
            {
                log.LogLine($"S3 error when generating presigned URL for {videoKey}: {ex.ErrorCode} - {ex.Message}");
                return CreateErrorResponse(HttpStatusCode.InternalServerError, 
                    $"Unable to generate download link for event {eventId}. Please try again later.");
            }
        }

        #endregion

        #region Video Download Operations

        /// <summary>
        /// Calculates device-specific click coordinates for video download automation.
        /// 
        /// Different devices/cameras may have slightly different UI layouts in Unifi Protect,
        /// requiring adjusted click coordinates for reliable automation.
        /// 
        /// Coordinate Logic:
        /// - "Door" device: Uses default coordinates from environment variables
        /// - Other devices: Uses adjusted coordinates (1205, 241) for archive and offset (-179, +18) for download
        /// </summary>
        /// <param name="deviceName">Name of the device to determine coordinates for</param>
        /// <returns>Tuple containing archive and download button coordinates</returns>
        private static ((int x, int y) archiveButton, (int x, int y) downloadButton) GetDeviceSpecificCoordinates(string deviceName)
        {
            // For "Door" device, use the default coordinates from environment variables
            if (string.Equals(deviceName, "Door", StringComparison.OrdinalIgnoreCase))
            {
                return (
                    archiveButton: (ARCHIVE_BUTTON_X, ARCHIVE_BUTTON_Y),
                    downloadButton: (DOWNLOAD_BUTTON_X, DOWNLOAD_BUTTON_Y)
                );
            }
            
            // For all other devices, use adjusted coordinates
            int archiveX = 1205;
            int archiveY = 241;
            int downloadX = archiveX - 179;  // 1205 - 179 = 1026
            int downloadY = archiveY + 18;   // 241 + 18 = 259
            
            return (
                archiveButton: (archiveX, archiveY),
                downloadButton: (downloadX, downloadY)
            );
        }

        /// <summary>
        /// Downloads video from Unifi Protect using automated browser navigation.
        /// 
        /// This method uses HeadlessChromium to automate a headless browser session that:
        /// 1. Navigates to the Unifi Protect event link
        /// 2. Authenticates using stored credentials
        /// 3. Downloads the video file for the event using device-specific coordinates
        /// 4. Returns the video data as a byte array
        /// 
        /// The method handles the complete workflow of video retrieval from Unifi Protect
        /// systems that require web-based authentication and interaction. Click coordinates
        /// are adjusted based on the device name to account for UI differences.
        /// </summary>
        /// <param name="eventLocalLink">Direct URL to the event in Unifi Protect web interface</param>
        /// <param name="deviceName">Name of the device to determine appropriate click coordinates</param>
        /// <returns>Byte array containing the downloaded video data</returns>
        [ExcludeFromCodeCoverage] // Requires headless browser and external network connectivity
        public static async Task<byte[]> GetVideoFromLocalUnifiProtectViaHeadlessClient(string eventLocalLink, string deviceName, UnifiCredentials credentials)
        {
            log.LogLine($"Starting video download for event from URL: {eventLocalLink}");

            // Validate all required environment variables first to fail fast
            ValidateVideoDownloadPrerequisites(eventLocalLink, credentials);

            try
            {
                // Launch headless browser with optimized settings
                using var browser = await LaunchOptimizedBrowser();
                using var page = await SetupBrowserPage(browser);

                // Configure download behavior and directory
                var downloadDirectory = await ConfigureDownloadBehavior(page);

                // Navigate to the event link and handle authentication
                await NavigateAndAuthenticate(page, eventLocalLink, credentials, downloadDirectory);

                // Wait for page to be ready for interaction and get the event handler for cleanup
                var downloadEventHandler = await WaitForPageReady(page, downloadDirectory);

                try
                {
                    // Perform video download actions
                    await PerformVideoDownloadActions(page, deviceName, downloadDirectory);

                    // Wait for download to complete and return video data
                    return await WaitForDownloadAndGetVideoData(downloadDirectory);
                }
                finally
                {
                    // Clean up the event handler FIRST to prevent disposed object access
                    await CleanupEventHandler(page, downloadEventHandler);
                }
            }
            catch (ObjectDisposedException ex)
            {
                log.LogLine($"Browser was disposed during operation: {ex.Message}");
                throw new InvalidOperationException("Video download failed due to browser lifecycle issue. Please try again.");
            }
            catch (Exception ex)
            {
                // Filter out disposed object errors and provide cleaner error messages
                if (IsDisposedObjectError(ex))
                {
                    log.LogLine($"Disposed object error detected: {ex.Message}");
                    throw new InvalidOperationException("Video download failed due to browser cleanup issue. Please try again.");
                }
                
                log.LogLine($"Error while processing video download: {ex.Message}");
                throw new InvalidOperationException($"Error downloading video: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Cleans up the event handler to prevent disposed object access issues.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="downloadEventHandler">The event handler to clean up</param>
        private static async Task CleanupEventHandler(IPage page, EventHandler<MessageEventArgs>? downloadEventHandler)
        {
            if (downloadEventHandler != null)
            {
                try
                {
                    page.Client.MessageReceived -= downloadEventHandler;
                    log.LogLine("Download event handler cleaned up");
                }
                catch (Exception ex)
                {
                    log.LogLine($"Warning: Error cleaning up event handler: {ex.Message}");
                }
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// Checks if an exception is related to disposed object access.
        /// </summary>
        /// <param name="ex">The exception to check</param>
        /// <returns>True if it's a disposed object error</returns>
        private static bool IsDisposedObjectError(Exception ex)
        {
            var errorMessage = ex.Message?.ToLower() ?? string.Empty;
            return errorMessage.Contains("cannot access a disposed object") || 
                   errorMessage.Contains("disposed") ||
                   ex is ObjectDisposedException;
        }

        /// <summary>
        /// Validates prerequisites for video download including credentials and configuration.
        /// </summary>
        /// <param name="eventLocalLink">The event URL to download from</param>
        /// <param name="credentials">Unifi credentials</param>
        private static void ValidateVideoDownloadPrerequisites(string eventLocalLink, UnifiCredentials credentials)
        {
            if (string.IsNullOrEmpty(eventLocalLink) || string.IsNullOrEmpty(credentials.username) || string.IsNullOrEmpty(credentials.password))
            {
                log.LogLine("Missing required Unifi Protect credentials in environment variables");
                throw new InvalidOperationException("Server configuration error: Unifi Protect credentials not configured");
            }

            if (string.IsNullOrEmpty(ALARM_BUCKET_NAME))
            {
                log.LogLine("StorageBucket environment variable is not configured");
                throw new InvalidOperationException("Server configuration error: StorageBucket not configured");
            }
        }

        /// <summary>
        /// Launches an optimized headless browser for video downloading.
        /// </summary>
        /// <returns>Browser instance configured for video downloading</returns>
        [ExcludeFromCodeCoverage] // Requires headless browser infrastructure
        private static async Task<IBrowser> LaunchOptimizedBrowser()
        {
            log.LogLine("Launching headless browser with HeadlessChromium...");

            try
            {
                // Create a logger factory for HeadlessChromium - don't dispose it yet as browser may need it
                var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { });
                
                var browserLauncher = new HeadlessChromiumPuppeteerLauncher(loggerFactory);

                // Use custom chrome arguments optimized for video downloading
                var chromeArgs = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--disable-web-security",
                    "--ignore-certificate-errors",
                    "--ignore-ssl-errors",
                    "--ignore-certificate-errors-spki-list",
                    "--no-first-run",
                    "--no-zygote",
                    "--disable-background-timer-throttling",
                    "--disable-backgrounding-occluded-windows",
                    "--disable-renderer-backgrounding",
                    "--disable-features=VizDisplayCompositor",
                    "--window-size=1920,1080",
                    "--disable-extensions",
                    "--disable-plugins",
                    "--disable-default-apps",
                    "--allow-running-insecure-content",
                    "--disable-background-networking",
                    "--enable-logging",
                    "--disable-ipc-flooding-protection"
                };

                var browser = await browserLauncher.LaunchAsync(chromeArgs);
                
                // Important: Don't dispose loggerFactory here as the browser might still need it
                // The loggerFactory will be disposed when the browser is disposed
                
                return browser;
            }
            catch (Exception ex)
            {
                log.LogLine($"Failed to launch browser: {ex.Message}");
                throw new InvalidOperationException($"Browser launch failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Sets up a browser page with proper viewport and configuration.
        /// </summary>
        /// <param name="browser">The browser instance</param>
        /// <returns>Configured page instance</returns>
        [ExcludeFromCodeCoverage] // Requires headless browser infrastructure
        private static async Task<IPage> SetupBrowserPage(IBrowser browser)
        {
            var page = await browser.NewPageAsync();

            // Set viewport to ensure consistent rendering
            await page.SetViewportAsync(new ViewPortOptions
            {
                Width = 1920,
                Height = 1080
            });

            return page;
        }

        /// <summary>
        /// Configures download behavior and ensures download directory exists.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <returns>The download directory path</returns>
        [ExcludeFromCodeCoverage] // Requires headless browser infrastructure
        private static async Task<string> ConfigureDownloadBehavior(IPage page)
        {
            var downloadDirectory = DOWNLOAD_DIRECTORY;

            // Ensure the download directory exists with proper permissions
            if (!Directory.Exists(downloadDirectory))
            {
                Directory.CreateDirectory(downloadDirectory);
                log.LogLine($"Created download directory: {downloadDirectory}");
            }

            // Set permissions on the download directory (required for Lambda)
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    log.LogLine($"Setting permissions for directory: {downloadDirectory}");
                }
            }
            catch (Exception ex)
            {
                log.LogLine($"Could not set directory permissions (this is normal in Lambda): {ex.Message}");
            }

            log.LogLine($"Using download directory: {downloadDirectory}");

            // Configure download behavior using CDP (Chrome DevTools Protocol)
            await page.Client.SendAsync("Page.setDownloadBehavior", new
            {
                behavior = "allow",
                downloadPath = downloadDirectory,
                eventsEnabled = true
            });

            // Try to enable download events in Browser domain for monitoring
            try
            {
                await page.Client.SendAsync("Browser.setDownloadBehavior", new
                {
                    behavior = "allow",
                    downloadPath = downloadDirectory,
                    eventsEnabled = true
                });
            }
            catch (Exception ex)
            {
                log.LogLine($"Could not set Browser.setDownloadBehavior (this is normal): {ex.Message}");
            }

            return downloadDirectory;
        }

        /// <summary>
        /// Navigates to the event URL and handles authentication if required.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="eventLocalLink">The event URL</param>
        /// <param name="credentials">Unifi credentials</param>
        /// <param name="downloadDirectory">Download directory for screenshots</param>
        private static async Task NavigateAndAuthenticate(IPage page, string eventLocalLink, UnifiCredentials credentials, string downloadDirectory)
        {
            log.LogLine($"Navigating to Unifi Protect: {eventLocalLink}");

            // Navigate to the event link
            await page.GoToAsync(eventLocalLink, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                Timeout = 20000 // 20 second timeout
            });
            log.LogLine("Page loaded, checking for login form...");

            // Check if we need to login (look for username/password fields)
            var usernameField = await page.QuerySelectorAsync("input[name='username'], input[type='email'], input[id*='username'], input[id*='email']");
            var passwordField = await page.QuerySelectorAsync("input[name='password'], input[type='password'], input[id*='password']");

            // Take a screenshot of the page
            var screenshotPath = Path.Combine(downloadDirectory, "login-screenshot.png");
            await page.ScreenshotAsync(screenshotPath);
            log.LogLine($"Screenshot taken: {screenshotPath}");
            await UploadFileAsync(ALARM_BUCKET_NAME!, "screenshots/login-screenshot.png", await File.ReadAllBytesAsync(screenshotPath), "image/png");

            // Check if username and password fields are present
            if (usernameField != null && passwordField != null)
            {
                await PerformUnifiLogin(page, usernameField, passwordField, credentials);
            }
        }

        /// <summary>
        /// Waits for the page to be ready for interaction after authentication.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="downloadDirectory">Download directory for screenshots</param>
        /// <returns>The download event handler that was attached, so it can be properly cleaned up</returns>
        private static async Task<EventHandler<MessageEventArgs>?> WaitForPageReady(IPage page, string downloadDirectory)
        {
            // Wait for the page to fully load after authentication
            log.LogLine("Waiting for page to load after authentication...");

            // Wait for any final network activity to settle
            try
            {
                await Task.Delay(2000); // Brief wait for any immediate changes

                // Check if the page has finished loading by evaluating ready state
                var isReady = await page.EvaluateExpressionAsync<bool>("document.readyState === 'complete'");
                if (!isReady)
                {
                    log.LogLine("Document not ready, waiting for complete state...");
                    await page.WaitForFunctionAsync("() => document.readyState === 'complete'", new WaitForFunctionOptions
                    {
                        Timeout = 10000
                    });
                }
                log.LogLine("Page document ready state is complete");
            }
            catch (Exception ex)
            {
                log.LogLine($"Timeout waiting for page ready state: {ex.Message}, but continuing with page interaction");
            }

            // Additionally wait for any dynamic content to load by checking for common UI elements
            try
            {
                // Wait for some common elements that might indicate the page is ready
                await page.WaitForSelectorAsync("body", new WaitForSelectorOptions { Timeout = 5000 });
                log.LogLine("Page body element found, page appears ready");
            }
            catch (Exception)
            {
                log.LogLine("Timeout waiting for page elements, but continuing...");
            }

            log.LogLine("Page loaded, preparing to click to download...");

            // Set up download event monitoring for better tracking
            bool downloadStarted = false;
            string? downloadGuid = null;

            // Define the event handler so we can properly unsubscribe later
            EventHandler<MessageEventArgs> downloadEventHandler = (sender, e) =>
            {
                try
                {
                    ProcessDownloadEvent(e.MessageID, e.MessageData, ref downloadStarted, ref downloadGuid);
                }
                catch (Exception ex)
                {
                    log.LogLine($"Error processing download event: {ex.Message}");
                }
            };

            // Listen for download events (simplified approach)
            page.Client.MessageReceived += downloadEventHandler;

            // Take a screenshot of the page
            await Task.Delay(3000); // Brief wait for any immediate changes
            var screenshotPath = Path.Combine(downloadDirectory, "pageload-screenshot.png");
            await page.ScreenshotAsync(screenshotPath);
            log.LogLine($"Screenshot taken of the loaded page: {screenshotPath}");
            await UploadFileAsync(ALARM_BUCKET_NAME!, "screenshots/pageload-screenshot.png", await File.ReadAllBytesAsync(screenshotPath), "image/png");
            
            // Return the event handler so it can be properly cleaned up
            return downloadEventHandler;
        }

        /// <summary>
        /// Performs the video download actions by clicking archive and download buttons.
        /// </summary>
        /// <param name="page">The browser page</param>
        /// <param name="deviceName">Device name for coordinate calculation</param>
        /// <param name="downloadDirectory">Download directory for screenshots</param>
        private static async Task PerformVideoDownloadActions(IPage page, string deviceName, string downloadDirectory)
        {
            // Calculate device-specific coordinates
            var coordinates = GetDeviceSpecificCoordinates(deviceName);
            
            //Create a dictionary of coordinates for clicks to download videos
            Dictionary<string, (int x, int y)> clickCoordinates = new Dictionary<string, (int x, int y)>
            {
                { "archiveButton", coordinates.archiveButton },
                { "downloadButton", coordinates.downloadButton }
            };

            log.LogLine($"Device: {deviceName ?? "Unknown"} - Using click coordinates - Archive: ({coordinates.archiveButton.x}, {coordinates.archiveButton.y}), Download: ({coordinates.downloadButton.x}, {coordinates.downloadButton.y})");

            // Click at click coordinates for archive button
            await page.Mouse.ClickAsync(clickCoordinates["archiveButton"].x, clickCoordinates["archiveButton"].y);
            log.LogLine("Clicked on archive button at coordinates: " + clickCoordinates["archiveButton"]);

            // Take a screenshot of the clicked archive button
            var screenshotPath = Path.Combine(downloadDirectory, "firstclick-screenshot.png");
            await page.ScreenshotAsync(screenshotPath);
            log.LogLine($"Screenshot taken of the clicked archive button: {screenshotPath}");
            await UploadFileAsync(ALARM_BUCKET_NAME!, "screenshots/firstclick-screenshot.png", await File.ReadAllBytesAsync(screenshotPath), "image/png");

            // Click at click coordinates for download button
            await page.Mouse.ClickAsync(clickCoordinates["downloadButton"].x, clickCoordinates["downloadButton"].y);
            log.LogLine("Clicked on download button at coordinates: " + clickCoordinates["downloadButton"]);

            // Take a screenshot of the clicked download button
            screenshotPath = Path.Combine(downloadDirectory, "secondclick-screenshot.png");
            await page.ScreenshotAsync(screenshotPath);
            log.LogLine($"Screenshot taken of the clicked download button: {screenshotPath}");
            await UploadFileAsync(ALARM_BUCKET_NAME!, "screenshots/secondclick-screenshot.png", await File.ReadAllBytesAsync(screenshotPath), "image/png");
        }

        /// <summary>
        /// Waits for the download to complete and returns the video data.
        /// </summary>
        /// <param name="downloadDirectory">The download directory to monitor</param>
        /// <returns>The downloaded video data as byte array</returns>
        private static async Task<byte[]> WaitForDownloadAndGetVideoData(string downloadDirectory)
        {
            // Wait for download to complete
            log.LogLine("Waiting for video download to complete...");

            // Monitor both download events and file system for better reliability
            var initialFileCount = Directory.GetFiles(downloadDirectory, "*.mp4").Length;
            var maxWaitTime = TimeSpan.FromSeconds(100);
            var checkInterval = TimeSpan.FromSeconds(1);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            log.LogLine($"Initial file count: {initialFileCount}");

            while (stopwatch.Elapsed < maxWaitTime)
            {
                var currentFileCount = Directory.GetFiles(downloadDirectory, "*.mp4").Length;
                if (currentFileCount > initialFileCount)
                {
                    log.LogLine($"New video file detected after {stopwatch.Elapsed.TotalSeconds:F1} seconds");

                    // Wait a bit more to ensure the file is completely written
                    await Task.Delay(2000);
                    break;
                }

                // Also check for any files with partial download extensions
                var partialFiles = Directory.GetFiles(downloadDirectory, "*.crdownload").Length;
                var tempFiles = Directory.GetFiles(downloadDirectory, "*.tmp").Length;

                if (partialFiles > 0 || tempFiles > 0)
                {
                    log.LogLine($"Partial download files detected: .crdownload={partialFiles}, .tmp={tempFiles}");
                }

                await Task.Delay(checkInterval);
            }

            if (stopwatch.Elapsed >= maxWaitTime)
            {
                log.LogLine("Download timeout reached, checking for any video files...");

                // List all files in download directory for debugging
                var allFiles = Directory.GetFiles(downloadDirectory);
                log.LogLine($"All files in download directory: {string.Join(", ", allFiles.Select(f => Path.GetFileName(f)))}");
            }

            // Get the video data from the downloaded video by checking for the latest mp4 file that was added to the directory
            var videoFiles = Directory.GetFiles(downloadDirectory, "*.mp4");
            log.LogLine($"Searching for .mp4 video files in directory: {downloadDirectory}");
            log.LogLine($"Found {videoFiles.Length} video files in the current directory.");

            // Order by creation time (most recent first) to get the actual latest file
            var latestVideoFile = videoFiles
                .OrderByDescending(f => File.GetCreationTime(f))
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(latestVideoFile))
            {
                var creationTime = File.GetCreationTime(latestVideoFile);
                log.LogLine($"Latest video file: {latestVideoFile} (created: {creationTime})");
            }
            else
            {
                log.LogLine("Latest video file: null");
            }

            if (string.IsNullOrEmpty(latestVideoFile))
            {
                log.LogLine("No video files found in download directory");
                throw new FileNotFoundException("No video files were downloaded");
            }

            byte[] videoData = await File.ReadAllBytesAsync(latestVideoFile);
            log.LogLine($"Video data size: {videoData.Length} bytes");

            // Return the video data
            return videoData;
        }

        #endregion
    }
}