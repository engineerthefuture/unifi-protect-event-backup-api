/************************
 * Unifi Webhook Event Receiver
 * UnifiWebhookEventReceiver.cs
 * Receives alarm event webhooks from Unifi Dream Machine
 * Brent Foster
 * 12-23-2024
 ***********************/

// Includes
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using Amazon.Lambda.Core;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Lambda.APIGatewayEvents;
using System.Collections;
using System.Text;
//using Amazon.SecretsManager;
//using Amazon.SecretsManager.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace UnifiWebhookEventReceiver
{
    // Main class
    public class UnifiWebhookEventReceiver
    {
        // Return messages
        const string ERROR_MESSAGE_500 = "An internal server error has occured: ";
        const string ERROR_MESSAGE_400 = "Your request is malformed or invalid: ";
        const string ERROR_MESSAGE_404 = "Route not found: ";
        const string MESSAGE_202 = "No action taken on request.";
        const string ERROR_GENERAL = "you must have a valid body object in your request";
        const string ERROR_TRIGGERS = "you must have triggers in your payload";
        const string ERROR_EVENTKEY = "you must provide an eventKey in the path";
        const string ERROR_INVALID_ROUTE = "please provide a valid route";
        const int EXPIRATION_SECONDS = 86400;

        // Routes
        const string ROUTE_ALARM = "alarmevent";

        // Environment variables
        static string ALARM_BUCKET_NAME = Environment.GetEnvironmentVariable("StorageBucket");
        static string DEVICE_PREFIX = Environment.GetEnvironmentVariable("DevicePrefix");

        // AWS connectivity
        static RegionEndpoint AWS_REGION = RegionEndpoint.USEast1;
        static IAmazonS3 s3Client = new AmazonS3Client(AWS_REGION);
        const string SOURCE_EVENT_TRIGGER = "aws.events";

        // Deployed environment
        static string DEPLOYED_ENV = Environment.GetEnvironmentVariable("DeployedEnv");
        static string FUNCTION_NAME = Environment.GetEnvironmentVariable("FunctionName");
        //static string SECRET_NAME = $"{DEPLOYED_ENV}/UnifiWebhookEventReceiver";
        //static IAmazonSecretsManager secretClient = new AmazonSecretsManagerClient(AWS_REGION);

        // Logging
        static ILambdaLogger log;

        /*
         *
         * Main function handler
         *
         */
        public APIGatewayProxyResponse FunctionHandler(Stream input, ILambdaContext context)
        {
            log = context.Logger;
            log.LogLine("C# HTTP trigger function processed a request for " + FUNCTION_NAME + ".");

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
                        APIGatewayProxyRequest req = JsonConvert.DeserializeObject<APIGatewayProxyRequest>(requestBody);

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
                                            JObject alarmObject = (JObject)jo.SelectToken("alarm");
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
    
                                            // Process the webhook
                                            Alarm alarm = JsonConvert.DeserializeObject<Alarm>(alarmObjectString);
                                            alarm.timestamp = timestamp;
                                            return AlarmReceiverFunction(alarm).Result;
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
                                        return GetEventFunction(eventKey).Result;
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

        
        /*
         * 
         * Processes Alarm Event
         * 
         */
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
                            deviceName = (string)envVars[DEVICE_PREFIX + device];
                            log.LogLine("Device name found for " + device + ": " + deviceName);
                            trigger.deviceName = deviceName;
                        }

                        // Generate presigned URL 
                        /*
                        String videoKey = deviceName + "/" + device + "_" + timestamp.ToString() + "_" + triggerType + "-video.mp4";
                        string presignedUrl = GeneratePreSignedURL(videoKey, HttpVerb.PUT, EXPIRATION_SECONDS, "video/mp4");
                        trigger.presignedUrl = presignedUrl;
                        trigger.videoKey = videoKey;
                        log.LogLine("Presigned URL generated for " + videoKey + ": " + presignedUrl);
                        */

                        // Save alarm event to S3
                        String eventKey = device + "_" + timestamp.ToString() + ".json";
                        trigger.eventKey = eventKey;
                        alarm.triggers[0] = trigger;
                        await UploadFileAsync(ALARM_BUCKET_NAME, eventKey, JsonConvert.SerializeObject(alarm));

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


        /*
         * 
         * Uploads the file into S3 as a JSON object
         * 
         */
        private static async Task UploadFileAsync(string bucketName, string keyName, string obj)
        {
            try
            {
                // Prepare request
                var putObjectRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName,
                    ContentBody = obj
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


        /*
         *
         * Generates a presigned URL for an s3 upload
         *
         */
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

        /*
         *
         * Returns an event object from S3
         *
         */
        public static async Task<APIGatewayProxyResponse> GetEventFunction(string eventKey)
        {
            log.LogLine("Executing Get event function for eventKey: " + eventKey);

            if (eventKey != null && eventKey != "")
            {
                try
                {
                    // Get the object from S3
                    String keyName = eventKey;
                    String eventObject = GetJsonFileFromS3BlobAsync(keyName);

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


        /*
         *
         * Gets a JSON file from blob storage in S3 and return as a string
         *
         */
        private static String GetJsonFileFromS3BlobAsync(string keyName)
        {
            byte[] fileBytes;

            try
            {
                log.LogLine("Attempting to get object: " + keyName + " from " + ALARM_BUCKET_NAME + ".");

                // Prepare request
                var getObjectRequest = new GetObjectRequest
                {
                    BucketName = ALARM_BUCKET_NAME,
                    Key = keyName,
                };

                // Get the object
                MemoryStream ms = new MemoryStream();
                using (GetObjectResponse response = s3Client.GetObjectAsync(getObjectRequest).Result)
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

        /*
        * Gets a secret from the Secrets Manager
        */
        /*
        public static async Task<Dictionary<string, string>> GetSecret()
        {
            // Prepare to get the secret
            string secret = "";
            MemoryStream memoryStream = new MemoryStream();
            GetSecretValueRequest request = new GetSecretValueRequest();
            request.SecretId = SECRET_NAME;
            request.VersionStage = "AWSCURRENT";

            GetSecretValueResponse response = null;

            try
            {
                response = await secretClient.GetSecretValueAsync(request);
            }
            catch (DecryptionFailureException e)
            {
                log.LogLine("Exception encountered while getting secret: " + e.Message);
                throw;
            }
            catch (InternalServiceErrorException e)
            {
                log.LogLine("Exception encountered while getting secret: " + e.Message);
                throw;
            }
            catch (InvalidParameterException e)
            {
                log.LogLine("Exception encountered while getting secret: " + e.Message);
                throw;
            }
            catch (InvalidRequestException e)
            {
                log.LogLine("Exception encountered while getting secret: " + e.Message);
                throw;
            }
            catch (ResourceNotFoundException e)
            {
                log.LogLine("Exception encountered while getting secret: " + e.Message);
                throw;
            }
            catch (System.AggregateException ae)
            {
                log.LogLine("Exception encountered while getting secret: " + ae.Message);
                throw;
            }

            // Decrypts secret using the associated KMS CMK.
            // Depending on whether the secret is a string or binary, one of these fields will be populated.
            if (response.SecretString != null)
            {
                secret = response.SecretString;
            }
            else
            {
                memoryStream = response.SecretBinary;
                StreamReader reader = new StreamReader(memoryStream);
                string decodedBinarySecret = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(reader.ReadToEnd()));
            }

            // Parse into dictionary
            Dictionary<string, string> secretDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(secret);

            // Return the secrets
            return secretDictionary;
        }
        */
    }
}
