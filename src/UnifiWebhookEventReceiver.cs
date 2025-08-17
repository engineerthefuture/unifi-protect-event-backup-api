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
using System.Net;
using System.Text;

// Third-party includes
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;
using HeadlessChromium.Puppeteer.Lambda.Dotnet;

// AWS includes
using Amazon;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class
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
    /// - OPTIONS: Handles CORS preflight requests for web client support
    /// 
    /// Environment Variables Required:
    /// - StorageBucket: S3 bucket name for storing alarm events
    /// - DevicePrefix: Prefix for environment variables containing device name mappings
    /// - DeployedEnv: Environment identifier (dev, prod, etc.)
    /// - FunctionName: Lambda function name for logging
    /// - UnifiHost: Hostname or IP of Unifi Protect system
    /// - UnifiUsername: Username for Unifi Protect authentication
    /// - UnifiPassword: Password for Unifi Protect authentication
    /// 
    /// Dependencies:
    /// - For local development, ensure PuppeteerSharp can download browser or provide custom path
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
        
        /// <summary>Error message for GET requests missing eventKey parameter</summary>
        const string ERROR_EVENTKEY = "you must provide an eventKey in the path";
        
        /// <summary>Error message for invalid API routes</summary>
        const string ERROR_INVALID_ROUTE = "please provide a valid route";
        
        /// <summary>Presigned URL expiration time in seconds (24 hours)</summary>
        const int EXPIRATION_SECONDS = 86400;

        /// <summary>API route for alarm event webhook processing</summary>
        const string ROUTE_ALARM = "alarmevent";
        
        /// <summary>Event source identifier for AWS scheduled events</summary>
        const string SOURCE_EVENT_TRIGGER = "aws.events";

        #endregion

        #region Environment Variables and AWS Configuration
        
        /// <summary>S3 bucket name for storing alarm event data</summary>
        static string? ALARM_BUCKET_NAME = Environment.GetEnvironmentVariable("StorageBucket");
        
        /// <summary>Prefix for environment variables containing device MAC to name mappings</summary>
        static string? DEVICE_PREFIX = Environment.GetEnvironmentVariable("DevicePrefix");
        
        /// <summary>Deployment environment identifier (dev, staging, prod)</summary>
        static string? DEPLOYED_ENV = Environment.GetEnvironmentVariable("DeployedEnv");
        
        /// <summary>Lambda function name for logging and identification</summary>
        static string? FUNCTION_NAME = Environment.GetEnvironmentVariable("FunctionName");

        /// <summary>Unifi Protect hostname or IP address for video downloads</summary>
        static string? UNIFI_HOST = Environment.GetEnvironmentVariable("UnifiHost");
        
        /// <summary>Unifi Protect username for authentication</summary>
        static string? UNIFI_USERNAME = Environment.GetEnvironmentVariable("UnifiUsername");
        
        /// <summary>Unifi Protect password for authentication</summary>
        static string? UNIFI_PASSWORD = Environment.GetEnvironmentVariable("UnifiPassword");

        /// <summary>Download directory for temporary video files. Defaults to /tmp for Lambda compatibility.</summary>
        static string DOWNLOAD_DIRECTORY = Environment.GetEnvironmentVariable("DownloadDirectory") ?? "/tmp";

        /// <summary>AWS region for S3 operations</summary>
        static RegionEndpoint AWS_REGION = RegionEndpoint.USEast1;
        
        /// <summary>S3 client instance for bucket operations</summary>
        static IAmazonS3 s3Client = new AmazonS3Client(AWS_REGION);

        #endregion

        #region Logging Infrastructure
        
        /// <summary>Lambda logger instance for function execution logging</summary>
        static ILambdaLogger log = new NullLogger();
        
        /// <summary>
        /// Null object pattern implementation for ILambdaLogger to prevent null reference exceptions
        /// when logger is not available during testing or initialization
        /// </summary>
        private class NullLogger : ILambdaLogger
        {
            public void Log(string message) { }
            public void LogLine(string message) { }
        }

        #endregion

        #region Main Lambda Handler

        /// <summary>
        /// Main AWS Lambda function handler for processing HTTP requests.
        /// 
        /// This method serves as the entry point for all incoming requests to the Lambda function.
        /// It handles various types of requests including:
        /// - Unifi Protect alarm webhook events (POST /alarmevent)
        /// - Event retrieval requests (GET /?eventKey={key})
        /// - CORS preflight requests (OPTIONS)
        /// - AWS scheduled events for keep-alive functionality
        /// 
        /// The function processes the input stream, deserializes the request, routes it to the
        /// appropriate handler, and returns a properly formatted API Gateway response with
        /// CORS headers for web client compatibility.
        /// </summary>
        /// <param name="input">Raw request stream containing the HTTP request data</param>
        /// <param name="context">Lambda execution context providing logging and runtime information</param>
        /// <returns>API Gateway proxy response with status code, headers, and JSON body</returns>
        public async Task<APIGatewayProxyResponse> FunctionHandler(Stream input, ILambdaContext context)
        {
            try
            {
                log = context?.Logger ?? new NullLogger();
                if (input == null)
                {
                    log.LogLine("Input stream was null.");
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest,
                        Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + ERROR_GENERAL) }),
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                    };
                }
                var functionName = FUNCTION_NAME ?? "UnknownFunction";
                log.LogLine("C# HTTP trigger function processed a request for " + functionName + ".");
            }
            catch (Exception ex)
            {
                // If we can't even log, return basic error response
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Body = JsonConvert.SerializeObject(new { msg = "Handler initialization error: " + ex.Message }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                };
            }

            try
            {
                // Read the request
                StreamReader streamReader = new StreamReader(input);
                string requestBody = streamReader.ReadToEnd();
                //log.LogLine("Request: " + requestBody);

                // Ensure there is a payload
                if (requestBody != null)
                {
                    // Event trigger
                    if (requestBody.Contains(SOURCE_EVENT_TRIGGER) == true)
                    {
                        log.LogLine("Scheduled event trigger received.");

                        var response = new APIGatewayProxyResponse
                        {
                            StatusCode = (int)HttpStatusCode.OK,
                            Body = JsonConvert.SerializeObject(new { msg = (MESSAGE_202) }),
                            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                            };
                            return response;
                    }
                    // API trigger
                    else
                    {
                        // Process the request object
                        APIGatewayProxyRequest? req = JsonConvert.DeserializeObject<APIGatewayProxyRequest>(requestBody);
                        if (req == null)
                        {
                            log.LogLine("Failed to deserialize API Gateway request from: " + requestBody);
                            return new APIGatewayProxyResponse
                            {
                                StatusCode = (int)HttpStatusCode.BadRequest,
                                Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + "malformed or invalid request format") }),
                                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                            };
                        }

                        // Determine the route
                        string route;
                        string method;
                        string path;
                        if (req != null && req.Path != null && req.Path != "" && req.HttpMethod != null)
                        {
                            // Parse the path
                            path = req.Path;
                            if (req.Path.Contains("/") == true)
                            {
                                path = req.Path.Substring(req.Path.IndexOf("/") + 1);
                            }

                            // Parse the route
                            if (path.Contains("/") == true)
                            {
                                route = path.Substring(0, path.LastIndexOf("/"));
                            }
                            else
                            {
                                route = path;
                            }

                            log.LogLine("Path: " + path);
                            log.LogLine("Route: " + route);

                            // Get the method
                            method = req.HttpMethod.ToUpper();
                            log.LogLine("Method: " + method);

                            // Preflight Options request
                            if (method == HttpMethod.Options.ToString().ToUpper())
                            {
                                log.LogLine("Preflight Options request.");
                                var response = new APIGatewayProxyResponse
                                {
                                    StatusCode = (int)HttpStatusCode.OK,
                                    Body = null,
                                    Headers = new Dictionary<string, string> {
                                        { "Access-Control-Allow-Methods", "GET,POST,OPTIONS" },
                                        { "Access-Control-Allow-Origin", "*" },
                                        { "Access-Control-Allow-Headers", "Content-Type,X-Amz-Date,Authorization,x-api-key,X-Api-Key,X-Amz-Security-Token,Origin,Access-Control-Allow-Origin,Access-Control-Allow-Methods"}
                                    }
                                };
                                return response;
                            }

                            // Determine the route
                            if (route != null && (route != "" || (req.QueryStringParameters != null && req.QueryStringParameters.Count >0)))
                            {
                                // New alarm event webhook route
                                if (method == HttpMethod.Post.ToString().ToUpper() && route == ROUTE_ALARM)
                                {
                                    try
                                    {
                                        // Ensure there is a payload
                                        if (req.Body != null && req.Body != "")
                                        {
                                            // Read the request
                                            log.LogLine("Request: " + req.Body);

                                            // Deserialize body
                                            JObject jo = JObject.Parse(req.Body);
                                            JObject? alarmObject = jo.SelectToken("alarm") as JObject;
                                            long timestamp = (long)0;
                                            String alarmObjectString = "";
                                            if(jo.SelectToken("timestamp") != null)
                                            {
                                                timestamp = (long)Convert.ToDouble(jo.SelectToken("timestamp"));
                                            }
                                            if(alarmObject != null)
                                            {
                                                alarmObjectString = alarmObject.ToString(); 
                                            }

                                            if (string.IsNullOrEmpty(alarmObjectString))
                                            {
                                                log.LogLine("No alarm object found in request body");
                                                APIGatewayProxyResponse errorResponse = new APIGatewayProxyResponse
                                                {
                                                    StatusCode = (int)HttpStatusCode.BadRequest,
                                                    Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + "No alarm object found in request") }),
                                                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                                                };
                                                return errorResponse;
                                            }
    
                                            // Process the webhook
                                            Alarm? alarm = JsonConvert.DeserializeObject<Alarm>(alarmObjectString);
                                            if (alarm == null)
                                            {
                                                log.LogLine("Failed to deserialize alarm object");
                                                APIGatewayProxyResponse errorResponse = new APIGatewayProxyResponse
                                                {
                                                    StatusCode = (int)HttpStatusCode.BadRequest,
                                                    Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + "Invalid alarm object format") }),
                                                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                                                };
                                                return errorResponse;
                                            }
                                            alarm.timestamp = timestamp;
                                            return await AlarmReceiverFunction(alarm);
                                        }
                                        else
                                        {
                                            // Return response
                                            log.LogLine(ERROR_MESSAGE_400 + ERROR_GENERAL);
                                            var response = new APIGatewayProxyResponse
                                            {
                                                StatusCode = (int)HttpStatusCode.BadRequest,
                                                Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + ERROR_GENERAL) }),
                                                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                                            };
                                            return response;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        // Return response
                                        log.LogLine(e.ToString());
                                        var response = new APIGatewayProxyResponse
                                        {
                                            StatusCode = (int)HttpStatusCode.InternalServerError,
                                            Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_500 + e.Message) }),
                                            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                                        };
                                        return response;
                                    }
                                }
                                // Get request to download an event object received
                                else if(method == HttpMethod.Get.ToString().ToUpper())
                                {
                                    string eventKey = req.QueryStringParameters["eventKey"];
                                    if(eventKey == null || eventKey.Length == 0)
                                    {
                                        // Return response
                                        log.LogLine(ERROR_MESSAGE_400 + ERROR_EVENTKEY);
                                        var response = new APIGatewayProxyResponse
                                        {
                                            StatusCode = (int)HttpStatusCode.BadRequest,
                                            Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + ERROR_EVENTKEY) }),
                                            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                                        };
                                        return response;
                                    }
                                    else
                                    {
                                        log.LogLine("eventKey: " + eventKey);
                                        return await GetEventFunction(eventKey);
                                    }
                                }
                                // Invalid route
                                else
                                {
                                    // Return response
                                    log.LogLine(ERROR_MESSAGE_404 + ERROR_INVALID_ROUTE);
                                    log.LogLine("Given route: " + route);
                                    log.LogLine("Given method: " + method);
                                    var response = new APIGatewayProxyResponse
                                    {
                                        StatusCode = (int)HttpStatusCode.NotFound,
                                        Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_404 + ERROR_INVALID_ROUTE) }),
                                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                                    };
                                    return response;
                                }
                            }
                            // Invalid route
                            else
                            {
                                // Return response
                                log.LogLine(ERROR_MESSAGE_404 + ERROR_INVALID_ROUTE);
                                log.LogLine("Given route: " + route);
                                log.LogLine("Given method: " + method);
                                var response = new APIGatewayProxyResponse
                                {
                                    StatusCode = (int)HttpStatusCode.NotFound,
                                    Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_404 + ERROR_INVALID_ROUTE) }),
                                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                                };
                                return response;
                            }
                        }
                        else
                        {
                            // Return response
                            log.LogLine(ERROR_MESSAGE_400 + ERROR_GENERAL);
                            var response = new APIGatewayProxyResponse
                            {
                                StatusCode = (int)HttpStatusCode.BadRequest,
                                Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + ERROR_GENERAL) }),
                                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                            };
                            return response;
                        }
                    }
                }
                else
                {
                    // Return response
                    log.LogLine(ERROR_MESSAGE_400 + ERROR_GENERAL);
                    var response = new APIGatewayProxyResponse
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest,
                        Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + ERROR_GENERAL) }),
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                    };
                    return response;
                }
            }
            catch (Exception e)
            {
                // Return response
                log.LogLine(e.ToString());
                var response = new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_500 + e.Message) }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                };
                return response;
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
        public static async Task<APIGatewayProxyResponse> AlarmReceiverFunction(Alarm alarm)
        {
            log.LogLine("Executing alarm receiver function.");
            var response = new APIGatewayProxyResponse();

            try
            {
                    // Check for null object
                    if (alarm != null)
                    {
                        log.LogLine("Alarm: " + JsonConvert.SerializeObject(alarm));
                        // Check triggers
                        if (alarm.triggers == null || alarm.triggers.Count == 0)
                        {
                            // Return response
                            log.LogLine("Error: " + ERROR_TRIGGERS);
                            response = new APIGatewayProxyResponse
                            {
                                StatusCode = (int)HttpStatusCode.BadRequest,
                                Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + ERROR_TRIGGERS) }),
                                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                            };
                            return response;
                        }

                        // Event details
                        Trigger trigger = alarm.triggers.ElementAt(0);
                        String device = trigger.device;
                        long timestamp = alarm.timestamp;
                        String triggerType = trigger.key;
                        String eventId = trigger.eventId;
                        String eventPath = alarm.eventPath ?? "";
                        String eventLocalLink = alarm.eventLocalLink ?? "";
                        String deviceName = "";

                        // Set date from timestamp
                        DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
                        String date = String.Format("{0:s}", dt);
                        trigger.date = date;
                        log.LogInformation("Date: " + date);

                        // Map device Mac to device name and update object
                        IDictionary envVars = Environment.GetEnvironmentVariables();
                        if(envVars != null && envVars[DEVICE_PREFIX + device] != null)
                        {
                            deviceName = (string?)envVars[DEVICE_PREFIX + device] ?? "";
                            log.LogLine("Device name found for " + device + ": " + deviceName);
                            trigger.deviceName = deviceName;
                        }

                        // Generate presigned URL 
                        /*
                        string presignedUrl = GeneratePreSignedURL(videoKey, HttpVerb.PUT, EXPIRATION_SECONDS, "video/mp4");
                        trigger.presignedUrl = presignedUrl;
                        log.LogLine("Presigned URL generated for " + videoKey + ": " + presignedUrl);
                        */

                    // Set event key and update alarm object
                        String videoKey = device + "_" + timestamp.ToString() + ".mp4";
                        String eventKey = device + "_" + timestamp.ToString() + ".json";
                        trigger.eventKey = eventKey;
                        trigger.videoKey = videoKey;
                        alarm.triggers[0] = trigger;

                    // Create a file key that saves into a subfolder based on the date formated like "2024-12-23"
                    String eventFileKey = $"{dt.Year}-{dt.Month.ToString("D2")}-{dt.Day.ToString("D2")}/{eventKey}";
                    String videoFileKey = $"{dt.Year}-{dt.Month.ToString("D2")}-{dt.Day.ToString("D2")}/{videoKey}";

                    if (string.IsNullOrEmpty(ALARM_BUCKET_NAME))
                    {
                        log.LogLine("StorageBucket environment variable is not configured");
                        APIGatewayProxyResponse errorResponse = new APIGatewayProxyResponse
                        {
                            StatusCode = (int)HttpStatusCode.InternalServerError,
                            Body = JsonConvert.SerializeObject(new { msg = "Server configuration error: StorageBucket not configured" }),
                            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                        };
                        return errorResponse;
                    }

                    // Upload the alarm event to S3
                    await UploadFileAsync(ALARM_BUCKET_NAME, eventFileKey, JsonConvert.SerializeObject(alarm));

                    // Get the video file byte array
                    eventLocalLink = UNIFI_HOST + alarm.eventPath;
                    byte[] videoData = await GetVideoFromLocalUnifiProtectViaHeadlessClient(eventLocalLink);

                    // Upload the video file to S3
                    await UploadFileAsync(ALARM_BUCKET_NAME, videoFileKey, videoData, "video/mp4");

                    // Return success response
                    String bodyContent = FUNCTION_NAME + "has successfully processed the Unifi alarm event webhook with key " + eventKey + 
                    " for " + deviceName + " that occurred at " + date + "."; //The corresponding video file can now be uploaded to the " + ALARM_BUCKET_NAME +
                    //" S3 bucket using the presigned URL for " + videoKey + " within the next " + EXPIRATION_SECONDS + " seconds.";
                    log.LogLine("Returning response: " + bodyContent);
                        response = new APIGatewayProxyResponse
                        {
                            StatusCode = (int)HttpStatusCode.OK,
                            Body = JsonConvert.SerializeObject(new { msg = (bodyContent) }),
                            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                        };
                        return response;
                    }
                    // Return 400 response
                    else
                    {
                        // Return response
                        log.LogLine("Error: " + ERROR_GENERAL);
                        response = new APIGatewayProxyResponse
                        {
                            StatusCode = (int)HttpStatusCode.BadRequest,
                            Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + ERROR_GENERAL) }),
                            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                        };
                        return response;
                }      
            }
            catch (Exception e)
            {
                // Return response
                log.LogLine(ERROR_MESSAGE_500 + e.Message);
                response = new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_500 + e.Message) }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                };
                return response;
            }
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


        /// <summary>
        /// Generates presigned URLs for S3 object access.
        /// 
        /// This method creates time-limited URLs that allow direct access to S3 objects without
        /// requiring AWS credentials. Supports both upload (PUT) and download (GET) operations
        /// with configurable expiration times.
        /// 
        /// Note: Currently used for future video upload functionality but not actively used
        /// in the current alarm event processing workflow.
        /// </summary>
        /// <param name="keyName">S3 object key for the target file</param>
        /// <param name="method">HTTP method (GET for download, PUT for upload)</param>
        /// <param name="validDuration">URL validity duration in seconds</param>
        /// <param name="contentType">MIME type for upload operations</param>
        /// <returns>Presigned URL string valid for the specified duration</returns>
        private static string GeneratePreSignedURL(string keyName, HttpVerb method, double validDuration, string contentType)
        {
            // Upload
            var request = new GetPreSignedUrlRequest
            {
                BucketName  = ALARM_BUCKET_NAME,
                Key         = keyName,
                Verb        = method,
                Expires     = DateTime.UtcNow.AddSeconds(validDuration),
                ContentType = contentType
            };

            // Download
            if(method == HttpVerb.GET)
            {
                request = new GetPreSignedUrlRequest
                {
                    BucketName = ALARM_BUCKET_NAME,
                    Key = keyName,
                    Expires = DateTime.UtcNow.AddSeconds(validDuration)
                };
            }

           string url = s3Client.GetPreSignedURL(request);
           return url;
        }

        #endregion

        #region Event Retrieval Operations

        /// <summary>
        /// Retrieves stored alarm event data from S3 by event key.
        /// 
        /// This method handles GET requests for retrieving previously stored alarm events.
        /// It searches for the specified event key in the S3 bucket and returns the JSON
        /// content if found, or appropriate error responses if the event doesn't exist.
        /// 
        /// The eventKey parameter should match the filename pattern: {deviceMac}_{timestamp}.json
        /// </summary>
        /// <param name="eventKey">Unique event identifier used as S3 object key</param>
        /// <returns>API Gateway response containing the event JSON data or error message</returns>
        public static async Task<APIGatewayProxyResponse> GetEventFunction(string eventKey)
        {
            log.LogLine("Executing Get event function for eventKey: " + eventKey);

            if (eventKey != null && eventKey != "")
            {
                try
                {
                    // Get the object from S3
                    String keyName = eventKey;
                    String? eventObject = await GetJsonFileFromS3BlobAsync(keyName);

                    if (eventObject == null)
                    {
                        // Return response for not found
                        log.LogLine("Event object for " + eventKey + " not found.");
                        var notFoundResponse = new APIGatewayProxyResponse
                        {
                            StatusCode = (int)HttpStatusCode.NotFound,
                            Body = JsonConvert.SerializeObject(new { msg = "Event not found" }),
                            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                        };
                        return notFoundResponse;
                    }

                    // Return response
                    log.LogLine("Event object for " + eventKey + " retrieved successfully.");
                    var response = new APIGatewayProxyResponse
                    {
                        StatusCode = (int)HttpStatusCode.OK,
                        Body = eventObject,
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                    };
                    return response;
                }
                catch (Exception e)
                {
                    // Return response
                    log.LogLine("error getting event object from S3: " + e.Message);
                    var response = new APIGatewayProxyResponse
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest,
                        Body = JsonConvert.SerializeObject(new { msg = ("error getting event object from S3: " + e.Message) }),
                        Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                    };
                    return response;
                }

            }
            else
            {
                // Return response
                log.LogLine("Error: " + ERROR_EVENTKEY);
                var response = new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = JsonConvert.SerializeObject(new { msg = (ERROR_MESSAGE_400 + ERROR_EVENTKEY) }),
                    Headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "Access-Control-Allow-Origin", "*" } }
                };
                return response;
            }
        }


        /// <summary>
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
                    responseStream.CopyTo(ms);
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
                    throw new Exception("Error encountered while getting file from S3.");
                }
            }
            catch (Exception e)
            {
                log.LogLine("Unknown encountered on server. Message:'{0}' when reading an object" + e.Message);
                throw new Exception("Error encountered while getting file from S3.");
            }
        }

        #endregion

        #region Video Download Operations

        /// <summary>
        /// Downloads video from Unifi Protect using automated browser navigation.
        /// 
        /// This method uses PuppeteerSharp to automate a headless browser session that:
        /// 1. Navigates to the Unifi Protect event link
        /// 2. Authenticates using stored credentials
        /// 3. Downloads the video file for the event
        /// 4. Returns the video data as a byte array
        /// 
        /// The method handles the complete workflow of video retrieval from Unifi Protect
        /// systems that require web-based authentication and interaction.
        /// </summary>
        /// <param name="eventLocalLink">Direct URL to the event in Unifi Protect web interface</param>
        /// <param name="eventKey">Unique event identifier for naming the video file</param>
        /// <returns>Byte array containing the downloaded video data</returns>
        public static async Task<byte[]> GetVideoFromLocalUnifiProtectViaHeadlessClient(string eventLocalLink)
        {
            log.LogLine($"Starting video download for event from URL: {eventLocalLink}");

            // Validate all required environment variables first to fail fast
            if (string.IsNullOrEmpty(eventLocalLink) || string.IsNullOrEmpty(UNIFI_USERNAME) || string.IsNullOrEmpty(UNIFI_PASSWORD))
            {
                log.LogLine("Missing required Unifi Protect credentials in environment variables");
                throw new InvalidOperationException("Server configuration error: Unifi Protect credentials not configured");
            }

            if (string.IsNullOrEmpty(ALARM_BUCKET_NAME))
            {
                log.LogLine("StorageBucket environment variable is not configured");
                throw new InvalidOperationException("Server configuration error: StorageBucket not configured");
            }

            //Create a dictionary of coordinates for clicks to download videos
            int archiveButtonX = 1205;
            int archiveButtonY = 245;
            int downloadButtonX = 1095;
            int downloadButtonY = 275;
            Dictionary<string, (int x, int y)> clickCoordinates = new Dictionary<string, (int x, int y)>
            {
                { "archiveButton", (archiveButtonX, archiveButtonY) },
                { "downloadButton", (downloadButtonX, downloadButtonY) }
            };

                try
                {
                // Launch headless browser using HeadlessChromium for AWS Lambda optimization
                log.LogLine("Launching headless browser with HeadlessChromium...");
                
                // Create a simple logger factory for HeadlessChromium
                using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { });
                
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
                    "--window-size=1920,1080"
                };
                
                using var browser = await browserLauncher.LaunchAsync(chromeArgs);

                // Create a new page
                using var page = await browser.NewPageAsync();

                // Set viewport to ensure consistent rendering
                await page.SetViewportAsync(new ViewPortOptions
                {
                    Width = 1920,
                    Height = 1080
                });

                // Set the download path for the page - use configurable directory with Lambda-compatible default
                var downloadDirectory = DOWNLOAD_DIRECTORY;
                
                // Ensure the download directory exists
                if (!Directory.Exists(downloadDirectory))
                {
                    Directory.CreateDirectory(downloadDirectory);
                    log.LogLine($"Created download directory: {downloadDirectory}");
                }
                
                log.LogLine($"Using download directory: {downloadDirectory}");
                
                await page.Client.SendAsync("Page.setDownloadBehavior", new
                {
                    behavior = "allow",
                    downloadPath = downloadDirectory
                });

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
                    await UploadFileAsync(ALARM_BUCKET_NAME, "screenshots/login-screenshot.png", File.ReadAllBytes(screenshotPath), "image/png");

                    // Check if username and password fields are present
                if (usernameField != null && passwordField != null)
                {
                    log.LogLine("Login form detected, attempting authentication...");

                    // Take the username and password and * out all but the first 3 characters of the username and all of the characters of the password
                    log.LogLine("Filling in credentials for login...");
                    var maskedUsername = UNIFI_USERNAME.Length > 3 ? UNIFI_USERNAME.Substring(0, 3) + new string('*', UNIFI_USERNAME.Length - 3) : UNIFI_USERNAME;
                    var maskedPassword = new string('*', UNIFI_PASSWORD.Length);

                    log.LogLine($"Using credentials - Username: {maskedUsername}, Password: {maskedPassword}");

                    // Fill in credentials
                    await usernameField.TypeAsync(UNIFI_USERNAME);
                    await passwordField.TypeAsync(UNIFI_PASSWORD);

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

                    // Take a screenshot of the page
                    screenshotPath = Path.Combine(downloadDirectory, "pageload-screenshot.png");
                    await page.ScreenshotAsync(screenshotPath);
                    log.LogLine($"Screenshot taken of the loaded page: {screenshotPath}");
                    await UploadFileAsync(ALARM_BUCKET_NAME, "screenshots/pageload-screenshot.png", File.ReadAllBytes(screenshotPath), "image/png");

                    // Click at click coordinates for archive button
                    await page.Mouse.ClickAsync(clickCoordinates["archiveButton"].x, clickCoordinates["archiveButton"].y);
                    log.LogLine("Clicked on archive button at coordinates: " + clickCoordinates["archiveButton"]);

                    // Take a screenshot of the clicked archive button
                    screenshotPath = Path.Combine(downloadDirectory, "firstclick-screenshot.png");
                    await page.ScreenshotAsync(screenshotPath);
                    log.LogLine($"Screenshot taken of the clicked archive button: {screenshotPath}");
                    await UploadFileAsync(ALARM_BUCKET_NAME, "screenshots/firstclick-screenshot.png", File.ReadAllBytes(screenshotPath), "image/png");

                    // Click at click coordinates for download button
                await page.Mouse.ClickAsync(clickCoordinates["downloadButton"].x, clickCoordinates["downloadButton"].y);
                    log.LogLine("Clicked on download button at coordinates: " + clickCoordinates["downloadButton"]);

                    // Take a screenshot of the clicked download button
                    screenshotPath = Path.Combine(downloadDirectory, "secondclick-screenshot.png");
                    await page.ScreenshotAsync(screenshotPath);
                    log.LogLine($"Screenshot taken of the clicked download button: {screenshotPath}");
                    await UploadFileAsync(ALARM_BUCKET_NAME, "screenshots/secondclick-screenshot.png", File.ReadAllBytes(screenshotPath), "image/png");

                    // Wait for download to complete
                log.LogLine("Waiting for video download to complete...");
                    
                    // Instead of a fixed delay, monitor the download directory for new files
                    var initialFileCount = Directory.GetFiles(downloadDirectory, "*.mp4").Length;
                    var maxWaitTime = TimeSpan.FromSeconds(30);
                    var checkInterval = TimeSpan.FromSeconds(1);
                    var startTime = DateTime.Now;
                    
                    while (DateTime.Now - startTime < maxWaitTime)
                    {
                        var currentFileCount = Directory.GetFiles(downloadDirectory, "*.mp4").Length;
                        if (currentFileCount > initialFileCount)
                        {
                            log.LogLine($"New video file detected after {(DateTime.Now - startTime).TotalSeconds:F1} seconds");
                            
                            // Wait a bit more to ensure the file is completely written
                            await Task.Delay(2000);
                            break;
                        }
                        
                        await Task.Delay(checkInterval);
                    }
                    
                    if (DateTime.Now - startTime >= maxWaitTime)
                    {
                        log.LogLine("Download timeout reached, checking for any video files...");
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

                byte[] videoData = File.ReadAllBytes(latestVideoFile);  
                log.LogLine($"Video data size: {videoData.Length} bytes");

                // Return the video data
                return videoData;

                }
                catch (Exception ex)
                {
                    log.LogLine($"Error while processing video download: {ex.Message}");
                    throw new Exception($"Error downloading video: {ex.Message}", ex);
                }
        }

        #endregion

            /// <summary>
            /// Uploads video binary data to S3.
            /// 
            /// This method handles the storage of video files in the configured S3 bucket.
            /// The content is stored as binary data with appropriate content type for video files.
            /// </summary>
            /// <param name="bucketName">Target S3 bucket name for storage</param>
            /// <param name="keyName">S3 object key (file path within bucket)</param>
            /// <param name="videoData">Binary video data to store</param>
            /// <returns>Task representing the asynchronous upload operation</returns>
        private static async Task UploadVideoToS3Async(string bucketName, string keyName, byte[] videoData)
        {
            try
            {
                using var stream = new MemoryStream(videoData);

                var putObjectRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName,
                    InputStream = stream,
                    ContentType = "video/mp4",
                    StorageClass = S3StorageClass.StandardInfrequentAccess // Optimize for infrequent access
                };

                await s3Client.PutObjectAsync(putObjectRequest);
                log.LogLine($"Successfully uploaded video to S3: {bucketName}/{keyName} ({videoData.Length} bytes)");
            }
            catch (AmazonS3Exception e)
            {
                log.LogLine($"S3 error uploading video: {e.Message}");
                throw;
            }
            catch (Exception e)
            {
                log.LogLine($"Error uploading video to S3: {e.Message}");
                throw;
            }
        }
    }
}
