/************************
 * Unifi Webhook Event Receiver
 * S3StorageService.cs
 * 
 * Service for handling S3 storage operations.
 * Manages file storage, retrieval, and presigned URL generation for alarm events and videos.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for handling S3 storage operations.
    /// </summary>
    public class S3StorageService : IS3StorageService
    {
        /// <summary>Default duration (in hours) for presigned S3 URLs.</summary>
        private const int DEFAULT_PRESIGNED_URL_HOURS = 1;
        private const int MAX_PRESIGNED_URL_HOURS = 24;

        /// <summary>
        /// Returns the latest video file and associated event data as a presigned download link.
        /// </summary>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        public async Task<APIGatewayProxyResponse> GetLatestVideoAsync()
        {
            var (videoKey, timestamp, errorResponse) = await SearchForLatestVideoAsync();
            if (errorResponse != null)
                return errorResponse;

            var eventData = await RetrieveEventDataAsync(videoKey!);
            return await BuildLatestVideoResponse(videoKey!, timestamp, eventData);
        }

        /// <summary>
        /// Retrieves a video file by event ID from S3.
        /// </summary>
        /// <param name="eventId">The event ID to search for</param>
        /// <returns>API Gateway response with the video file</returns>
        public async Task<APIGatewayProxyResponse> GetVideoByEventIdAsync(string eventId)
        {
            var configError = ValidateEventIdConfiguration(eventId);
            if (configError != null)
                return configError;

            var (eventKey, videoKey, timestamp, errorResponse) = await SearchEventByIdAsync(eventId);
            if (errorResponse != null)
                return errorResponse;

            var eventData = await RetrieveEventDataAsync(videoKey!);
            var videoExists = await VerifyVideoFileExistsAsync(videoKey!, eventId);
            if (videoExists != null)
                return videoExists;

            return await BuildEventVideoResponse(eventKey!, videoKey!, eventId, timestamp, eventData);
        }
    private readonly IAmazonS3 _s3Client;
    private readonly IResponseHelper _responseHelper;
    private readonly ILambdaLogger _logger;
    private readonly ISqsService? _sqsService;

        /// <summary>
        /// Initializes a new instance of the S3StorageService.
        /// </summary>
        /// <param name="s3Client">AWS S3 client</param>
        /// <param name="responseHelper">Response helper service</param>
        /// <param name="logger">Lambda logger instance</param>
        public S3StorageService(IAmazonS3 s3Client, IResponseHelper responseHelper, ILambdaLogger logger)
        {
            _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
            _responseHelper = responseHelper ?? throw new ArgumentNullException(nameof(responseHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sqsService = null;
        }

        public S3StorageService(IAmazonS3 s3Client, IResponseHelper responseHelper, ILambdaLogger logger, ISqsService sqsService)
        {
            _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
            _responseHelper = responseHelper ?? throw new ArgumentNullException(nameof(responseHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sqsService = sqsService ?? throw new ArgumentNullException(nameof(sqsService));
        }

        // --- Public interface methods ---

        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        public async Task<string> StoreAlarmEventAsync(Alarm alarm, Trigger trigger)
        {
            ArgumentNullException.ThrowIfNull(alarm);
            ArgumentNullException.ThrowIfNull(trigger);
            if (string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
                throw new InvalidOperationException("StorageBucket environment variable is not configured");
            var (eventKey, _) = GenerateS3Keys(trigger, alarm.timestamp);
            var alarmJson = JsonConvert.SerializeObject(alarm);
            await UploadStringContentAsync(AppConfiguration.AlarmBucketName, eventKey, alarmJson, "application/json");
            return eventKey;
        }

        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        public async Task StoreVideoFileAsync(string videoFilePath, string s3Key)
        {
            _logger.LogLine($"Video file path: {videoFilePath}");
            _logger.LogLine($"S3 key: {s3Key}");
            _logger.LogLine($"Bucket name: {AppConfiguration.AlarmBucketName}");
            if (string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
            {
                _logger.LogLine("ERROR: StorageBucket environment variable is not configured");
                throw new InvalidOperationException("StorageBucket environment variable is not configured");
            }
            if (!File.Exists(videoFilePath))
            {
                _logger.LogLine($"ERROR: Video file does not exist: {videoFilePath}");
                throw new FileNotFoundException($"Video file not found: {videoFilePath}");
            }
            var videoData = await File.ReadAllBytesAsync(videoFilePath);
            _logger.LogLine($"Successfully read video file: {videoData.Length} bytes");
            _logger.LogLine("About to upload video data to S3...");
            await UploadBinaryContentAsync(AppConfiguration.AlarmBucketName, s3Key, videoData, "video/mp4");
        }

        /// <summary>
        /// Returns a summary of the last event and event count per camera in the last 24 hours, with a presigned video link for each.
        /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S3776:Refactor this method to reduce its Cognitive Complexity from 23 to the 15 allowed.", Justification = "Business logic requires this complexity.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S1541:The Cyclomatic Complexity of this method is 13 which is greater than 10 authorized.", Justification = "Business logic requires this complexity.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("SonarQube", "S134:Refactor this code to not nest more than 3 control flow statements.", Justification = "Business logic requires this nesting.")]
    public async Task<APIGatewayProxyResponse> GetEventSummaryAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var bucket = AppConfiguration.AlarmBucketName!;
                var cameras = new Dictionary<string, CameraSummaryMulti>();
                var missingEvents = new List<CameraEventSummary>();
                int totalCount = 0;
                int objectsCount = 0;
                int activityCount = 0;
                var triggerKeyCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var headers = _responseHelper.GetStandardHeaders();

                for (int i = 0; i < 2; i++)
                {
                    var date = now.AddDays(-i);
                    var prefix = $"{date:yyyy-MM-dd}/";
                    var listReq = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix };
                    var listResp = await _s3Client.ListObjectsV2Async(listReq);
                    foreach (var obj in listResp.S3Objects.Select(o => o.Key).Where(k => k.EndsWith(".json")))
                    {
                        try
                        {
                            var alarm = await ReadAlarmFromS3Async(bucket, obj);
                            if (alarm == null || alarm.triggers == null || alarm.triggers.Count == 0)
                                continue;
                            var trigger = alarm.triggers[0];
                            // Count trigger keys for all triggers in the event
                            foreach (var key in alarm.triggers.Select(t => t.key).Where(k => !string.IsNullOrEmpty(k)))
                            {
                                if (!triggerKeyCounts.TryAdd(key!, 1))
                                    triggerKeyCounts[key!]++;
                            }
                            var cameraId = trigger.device;
                            var cameraName = trigger.deviceName ?? cameraId;
                            var videoKey = obj.Replace(".json", ".mp4");
                            var originalFileName = trigger.originalFileName ?? System.IO.Path.GetFileName(videoKey);
                            if (string.IsNullOrEmpty(cameraId) || string.IsNullOrEmpty(videoKey))
                                continue;

                            // Check if video exists in S3
                            bool videoExists = false;
                            try
                            {
                                var headRequest = new GetObjectMetadataRequest
                                {
                                    BucketName = bucket,
                                    Key = videoKey
                                };
                                await _s3Client.GetObjectMetadataAsync(headRequest);
                                videoExists = true;
                            }
                            catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey" || e.ErrorCode == "NotFound")
                            {
                                // Treat as missing video, do not log as error
                                videoExists = false;
                            }
                            catch (AmazonS3Exception e)
                            {
                                // Log and skip only for other S3 errors
                                _logger.LogLine($"Error checking video existence for {videoKey}: {e.Message}");
                                continue;
                            }

                            var sanitizedAlarm = CloneAlarmWithoutSourcesAndConditions(alarm);
                            var eventObj = new CameraEventSummary {
                                eventData = sanitizedAlarm,
                                videoUrl = videoExists ? await GetPresignedUrlAsync(bucket, videoKey, now, DEFAULT_PRESIGNED_URL_HOURS) : null,
                                originalFileName = originalFileName
                            };

                            if (videoExists)
                            {
                                if (!cameras.TryGetValue(cameraId, out var cam))
                                {
                                    cam = new CameraSummaryMulti {
                                        cameraId = cameraId,
                                        cameraName = cameraName,
                                        events = new List<CameraEventSummary>(),
                                        count24h = 0
                                    };
                                    cameras[cameraId] = cam;
                                }
                                cam.events.Add(eventObj);
                                cam.count24h++;
                                totalCount++;
                            }
                            else
                            {
                                missingEvents.Add(eventObj);
                            }

                            // Count event types
                            if (alarm.name == "Backup Alarm Event: Objects")
                                objectsCount++;
                            else if (alarm.name == "Backup Alarm Event: Activity")
                                activityCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogLine($"Error processing event file {obj}: {ex.Message}");
                        }
                    }
                }

                // Limit to last 3 events per camera, sorted by timestamp descending
                foreach (var cam in cameras.Values)
                {
                    cam.events = cam.events
                        .OrderByDescending(e => e.eventData?.timestamp ?? 0)
                        .Take(3)
                        .ToList();
                }
                var perCameraCounts = string.Join(", ", cameras.Values.Select(c => $"{c.cameraName} ({c.cameraId}): {c.count24h}"));
                var triggerKeySummary = string.Join(", ", triggerKeyCounts.Select(kvp => $"{kvp.Key}: {kvp.Value}"));

                // Get DLQ message count if SQS service is available
                int dlqMessageCount = 0;
                if (_sqsService != null)
                {
                    try
                    {
                        dlqMessageCount = await _sqsService.GetDlqMessageCountAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogLine($"Error retrieving DLQ message count: {ex.Message}");
                    }
                }

                var summaryMessage = $"In the past 24 hours, there were {totalCount} total events across all cameras: {objectsCount} 'Objects' events and {activityCount} 'Activity' events. Per camera: {perCameraCounts}. Trigger keys: {triggerKeySummary}. DLQ messages: {dlqMessageCount}.";
                var response = new {
                    cameras = cameras.Values,
                    missing = missingEvents,
                    totalCount,
                    objectsCount,
                    activityCount,
                    triggerKeyCounts,
                    dlqMessageCount,
                    summaryMessage
                };
                return new APIGatewayProxyResponse {
                    StatusCode = 200,
                    Body = JsonConvert.SerializeObject(response, Formatting.Indented),
                    Headers = headers
                };
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error in GetEventSummaryAsync: {ex.Message}");
                var headers = _responseHelper.GetStandardHeaders();
                var response = new { cameras = Array.Empty<object>(), totalCount = 0, error = ex.Message };
                return new APIGatewayProxyResponse {
                    StatusCode = 200,
                    Body = JsonConvert.SerializeObject(response, Formatting.Indented),
                    Headers = headers
                };
            }
        }

        private async Task<Alarm?> ReadAlarmFromS3Async(string bucket, string key)
        {
            using var getResp = await _s3Client.GetObjectAsync(bucket, key);
            using var reader = new StreamReader(getResp.ResponseStream);
            var json = await reader.ReadToEndAsync();
            return JsonConvert.DeserializeObject<Alarm>(json);
        }

    public async Task<string> GetPresignedUrlAsync(string bucket, string videoKey, DateTime now, int durationHours)
        {
            var req = new GetPreSignedUrlRequest
            {
                BucketName = bucket,
                Key = videoKey,
                Verb = HttpVerb.GET,
                Expires = now.AddHours(durationHours)
            };
            return await _s3Client.GetPreSignedURLAsync(req);
        }

        private sealed class CameraSummaryMulti
        {
            public string cameraId { get; set; } = string.Empty;
            public string cameraName { get; set; } = string.Empty;
            public List<CameraEventSummary> events { get; set; } = new();
            public int count24h { get; set; }
        }

        private sealed class CameraEventSummary
        {
            public Alarm? eventData { get; set; }
            public string? videoUrl { get; set; } = string.Empty;
            public string originalFileName { get; set; } = string.Empty;
        }

        /// <summary>
        /// Stores a raw JSON string in S3 at the specified key.
        /// </summary>
        public async Task StoreJsonStringAsync(string json, string s3Key)
        {
            ArgumentNullException.ThrowIfNull(json);
            // Store a JSON string in S3 at the specified key
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var putRequest = new PutObjectRequest
            {
                BucketName = AppConfiguration.AlarmBucketName!,
                Key = s3Key,
                InputStream = stream,
                ContentType = "application/json"
            };
            await _s3Client.PutObjectAsync(putRequest);
        }

        /// <summary>
        /// Generates S3 file keys for event data and video files.
        /// </summary>
        /// <param name="trigger">The trigger information</param>
        /// <param name="timestamp">The event timestamp</param>
        /// <returns>Tuple containing event key and video key</returns>
        public (string eventKey, string videoKey) GenerateS3Keys(Trigger trigger, long timestamp)
        {
            DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            string dateFolder = $"{dt.Year}-{dt.Month:D2}-{dt.Day:D2}";

            string eventKey = $"{dateFolder}/{trigger.eventId}_{trigger.device}_{timestamp}.json";
            string videoKey = $"{dateFolder}/{trigger.eventId}_{trigger.device}_{timestamp}.mp4";

            return (eventKey, videoKey);
        }

        /// <summary>
        /// Uploads a screenshot file to S3 with appropriate content type.
        /// </summary>
        /// <param name="screenshotFilePath">Path to the local screenshot file</param>
        /// <param name="s3Key">S3 key for the screenshot</param>
        public async Task StoreScreenshotFileAsync(string screenshotFilePath, string s3Key)
        {
            _logger.LogLine($"Screenshot file path: {screenshotFilePath}");
            _logger.LogLine($"S3 key: {s3Key}");
            _logger.LogLine($"Bucket name: {AppConfiguration.AlarmBucketName}");
            
            if (string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
            {
                _logger.LogLine("ERROR: StorageBucket environment variable is not configured");
                throw new InvalidOperationException("StorageBucket environment variable is not configured");
            }

            if (!File.Exists(screenshotFilePath))
            {
                _logger.LogLine($"ERROR: Screenshot file does not exist: {screenshotFilePath}");
                throw new FileNotFoundException($"Screenshot file not found: {screenshotFilePath}");
            }

            var screenshotData = await File.ReadAllBytesAsync(screenshotFilePath);
            _logger.LogLine($"Successfully read screenshot file: {screenshotData.Length} bytes");
            
            // Determine content type based on file extension
            string contentType = Path.GetExtension(screenshotFilePath).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };
            
            _logger.LogLine($"Using content type: {contentType}");
            _logger.LogLine("About to upload screenshot data to S3...");
            await UploadBinaryContentAsync(AppConfiguration.AlarmBucketName, s3Key, screenshotData, contentType);
        }

        #region Private Helper Methods

        /// <summary>
        /// Uploads string content to S3 with specified content type.
        /// </summary>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task UploadStringContentAsync(string bucketName, string keyName, string content, string contentType)
        {
            try
            {
                var putObjectRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName,
                    ContentBody = content,
                    ContentType = contentType,
                    StorageClass = S3StorageClass.StandardInfrequentAccess
                };

                await _s3Client.PutObjectAsync(putObjectRequest);
                _logger.LogLine("Successfully wrote the object to S3: " + bucketName + "/" + keyName);
            }
            catch (AmazonS3Exception e)
            {
                _logger.LogLine("Error encountered on object write: " + e.Message);
                throw;
            }
            catch (Exception e)
            {
                _logger.LogLine("Unknown encountered when writing an object: " + e.Message);
                throw;
            }
        }

        /// <summary>
        /// Uploads binary data to S3 with specified content type.
        /// </summary>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task UploadBinaryContentAsync(string bucketName, string keyName, byte[] data, string contentType)
        {
            try
            {
                using var stream = new MemoryStream(data);

                var putObjectRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName,
                    InputStream = stream,
                    ContentType = contentType,
                    StorageClass = S3StorageClass.StandardInfrequentAccess
                };

                await _s3Client.PutObjectAsync(putObjectRequest);
                _logger.LogLine($"Successfully wrote the object to S3: {bucketName}/{keyName} ({data.Length} bytes)");
            }
            catch (AmazonS3Exception e)
            {
                _logger.LogLine("Error encountered on object write: " + e.Message);
                throw;
            }
            catch (Exception e)
            {
                _logger.LogLine("Unknown encountered when writing an object: " + e.Message);
                throw;
            }
        }

        /// <summary>
        /// Retrieves JSON content from S3 and returns it as a string.
        /// </summary>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task<string?> GetJsonFileFromS3BlobAsync(string keyName)
        {
            try
            {
                if (string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
                {
                    throw new InvalidOperationException("StorageBucket environment variable is not configured");
                }

                _logger.LogLine("Attempting to get object: " + keyName + " from " + AppConfiguration.AlarmBucketName + ".");

                var getObjectRequest = new GetObjectRequest
                {
                    BucketName = AppConfiguration.AlarmBucketName,
                    Key = keyName,
                };

                MemoryStream ms = new MemoryStream();
                using (GetObjectResponse response = await _s3Client.GetObjectAsync(getObjectRequest))
                using (Stream responseStream = response.ResponseStream)
                    await responseStream.CopyToAsync(ms);
                    
                byte[] fileBytes = ms.ToArray();

                _logger.LogLine("Successfully retrieved the object from S3: " + AppConfiguration.AlarmBucketName + "/" + keyName + " with a size of: " + fileBytes.Length + " bytes");
                string objectString = Encoding.UTF8.GetString(fileBytes, 0, fileBytes.Length);
                return objectString;
            }
            catch (AmazonS3Exception e)
            {
                if (e.ErrorCode == "NoSuchKey")
                {
                    _logger.LogLine("Object doesn't exist.");
                    return null;
                }
                else
                {
                    _logger.LogLine("Error encountered while reading object from S3: " + e.Message);
                    throw new InvalidOperationException("Error encountered while getting file from S3.", e);
                }
            }
            catch (Exception e)
            {
                _logger.LogLine("Unknown encountered on server. Message:'{0}' when reading an object" + e.Message);
                throw new InvalidOperationException("Error encountered while getting file from S3.", e);
            }
        }


        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task<(string? VideoKey, long Timestamp, APIGatewayProxyResponse? ErrorResponse)> SearchForLatestVideoAsync()
        {
            _logger.LogLine("Searching for latest video file in S3 bucket using date-organized approach: " + AppConfiguration.AlarmBucketName);

            DateTime searchDate = DateTime.UtcNow.Date;
            int daysSearched = 0;

            while (daysSearched < AppConfiguration.MaxRetentionDays)
            {
                string dateFolder = searchDate.ToString("yyyy-MM-dd");
                _logger.LogLine($"Searching for videos in date folder: {dateFolder}");

                var dayResult = await SearchDateFolderForLatestVideoAsync(dateFolder);
                if (dayResult.VideoKey != null)
                {
                    _logger.LogLine($"Found latest video in {dateFolder}: {dayResult.VideoKey} with timestamp {dayResult.Timestamp}");
                    return (dayResult.VideoKey, dayResult.Timestamp, null);
                }

                searchDate = searchDate.AddDays(-1);
                daysSearched++;
                _logger.LogLine($"No videos found in {dateFolder}, moving to previous day: {searchDate:yyyy-MM-dd}");
            }

            _logger.LogLine("No video files found in S3 bucket");
            return (null, 0, _responseHelper.CreateErrorResponse(HttpStatusCode.NotFound, "No video files found"));
        }

        private async Task<(string? VideoKey, long Timestamp)> SearchDateFolderForLatestVideoAsync(string dateFolder)
        {
            var listRequest = new ListObjectsV2Request
            {
                BucketName = AppConfiguration.AlarmBucketName!,
                Prefix = dateFolder + "/",
                MaxKeys = 1000
            };

            string? latestVideoKey = null;
            long latestTimestamp = 0;

            do
            {
                var response = await _s3Client.ListObjectsV2Async(listRequest);

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

        private static long ExtractTimestampFromFileName(string s3Key)
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

        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task<APIGatewayProxyResponse?> VerifyVideoExistsAsync(string videoKey)
        {
            try
            {
                var headRequest = new GetObjectMetadataRequest
                {
                    BucketName = AppConfiguration.AlarmBucketName!,
                    Key = videoKey
                };
                var metadata = await _s3Client.GetObjectMetadataAsync(headRequest);
                _logger.LogLine($"Video file confirmed in S3: {videoKey} ({metadata.ContentLength} bytes)");
                return null;
            }
            catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey")
            {
                _logger.LogLine($"Video file {videoKey} not found in S3");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.NotFound, "Video file not found");
            }
        }

        private async Task<object?> RetrieveEventDataAsync(string videoKey)
        {
            string eventKey = videoKey.Replace(".mp4", ".json");
            _logger.LogLine($"Looking for corresponding event data: {eventKey}");

            try
            {
                string? eventJsonData = await GetJsonFileFromS3BlobAsync(eventKey);
                if (eventJsonData != null)
                {
                    var eventData = JsonConvert.DeserializeObject<Alarm>(eventJsonData);
                    _logger.LogLine($"Successfully retrieved event data for {eventKey}");
                        if (eventData != null)
                            return CloneAlarmWithoutSourcesAndConditions(eventData);
                        return null;
                }
                else
                {
                    _logger.LogLine($"No event data found for {eventKey}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error retrieving event data for {eventKey}: {ex.Message}");
            }

            return null;
        }

        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task<APIGatewayProxyResponse> BuildLatestVideoResponse(string videoKey, long timestamp, object? eventData)
        {
            DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
            
            // Try to get the original filename from the event data
            string suggestedFilename = Path.GetFileName(videoKey); // Default fallback
            if (eventData is Alarm alarm && alarm.triggers?.Count > 0 && !string.IsNullOrEmpty(alarm.triggers[0].originalFileName))
            {
                suggestedFilename = alarm.triggers[0].originalFileName!;
                _logger.LogLine($"Using original downloaded filename: {suggestedFilename}");
            }
            else
            {
                _logger.LogLine($"Using S3 key filename as fallback: {suggestedFilename}");
            }
            
            string eventKey = videoKey.Replace(".mp4", ".json");

            var presignedRequest = new GetPreSignedUrlRequest
            {
                BucketName = AppConfiguration.AlarmBucketName!,
                Key = videoKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddHours(1),
                ResponseHeaderOverrides = new ResponseHeaderOverrides
                {
                    ContentDisposition = $"attachment; filename=\"{suggestedFilename}\""
                }
            };

            string presignedUrl = await _s3Client.GetPreSignedURLAsync(presignedRequest);
            _logger.LogLine($"Generated presigned URL for {videoKey}, expires in 1 hour");

            // Remove sources and conditions from eventData if present
            object? sanitizedEventData = eventData is Alarm alarmObj ? CloneAlarmWithoutSourcesAndConditions(alarmObj) : eventData;
            var responseData = new
            {
                downloadUrl = presignedUrl,
                filename = suggestedFilename,
                videoKey = videoKey,
                eventKey = eventKey,
                timestamp = timestamp,
                eventDate = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                expiresAt = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss UTC"),
                eventData = sanitizedEventData,
                message = "Use the downloadUrl to download the video file directly. URL expires in 1 hour."
            };

            return _responseHelper.CreateSuccessResponse(responseData);
        }

        private APIGatewayProxyResponse? ValidateEventIdConfiguration(string eventId)
        {
            if (string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
            {
                _logger.LogLine("StorageBucket environment variable is not configured");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured");
            }

            if (string.IsNullOrWhiteSpace(eventId))
            {
                _logger.LogLine("EventId parameter is required");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, "EventId parameter is required");
            }

            return null;
        }

        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task<(string? EventKey, string? VideoKey, long Timestamp, APIGatewayProxyResponse? ErrorResponse)> SearchEventByIdAsync(string eventId)
        {
            _logger.LogLine($"Searching for event file with eventId prefix: {eventId}");

            DateTime searchDate = DateTime.UtcNow.Date;
            int daysSearched = 0;

            while (daysSearched < AppConfiguration.MaxRetentionDays)
            {
                string dateFolder = searchDate.ToString("yyyy-MM-dd");
                _logger.LogLine($"Searching for eventId {eventId} in date folder: {dateFolder}");

                var result = await SearchEventInDateFolderAsync(eventId, dateFolder);
                if (result.EventKey != null)
                {
                    return (result.EventKey, result.VideoKey, result.Timestamp, null);
                }
                searchDate = searchDate.AddDays(-1);
                daysSearched++;
            }
            return (null, null, 0, _responseHelper.CreateErrorResponse(HttpStatusCode.NotFound, $"Event with ID {eventId} not found in the last {AppConfiguration.MaxRetentionDays} days."));
        }

        private async Task<(string? EventKey, string? VideoKey, long Timestamp)> SearchEventInDateFolderAsync(string eventId, string dateFolder)
        {
            var listRequest = new ListObjectsV2Request
            {
                BucketName = AppConfiguration.AlarmBucketName!,
                Prefix = $"{dateFolder}/{eventId}_",
                MaxKeys = 10
            };

            var response = await _s3Client.ListObjectsV2Async(listRequest);

            var eventFile = response.S3Objects
                .Where(o => o.Key.EndsWith(".json"))
                .Select(obj => obj.Key)
                .FirstOrDefault();

            if (eventFile != null)
            {
                string eventKey = eventFile;
                string videoKey = eventFile.Replace(".json", ".mp4");
                long timestamp = ExtractTimestampFromEventFileName(eventFile);

                _logger.LogLine($"Found event file: {eventKey}, corresponding video: {videoKey}");
                return (eventKey, videoKey, timestamp);
            }

            return (null, null, 0);
        }

        private static long ExtractTimestampFromEventFileName(string eventKey)
        {
            var fileName = Path.GetFileName(eventKey);
            var parts = fileName.Split('_');

            if (parts.Length >= 3 && long.TryParse(parts[parts.Length - 1].Replace(".json", ""), out long timestamp) && timestamp >= 0)
            {
                return timestamp;
            }

            return 0;
        }

        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task<APIGatewayProxyResponse?> VerifyVideoFileExistsAsync(string videoKey, string eventId)
        {
            try
            {
                var headRequest = new GetObjectMetadataRequest
                {
                    BucketName = AppConfiguration.AlarmBucketName!,
                    Key = videoKey
                };
                var metadata = await _s3Client.GetObjectMetadataAsync(headRequest);
                _logger.LogLine($"Video file confirmed in S3: {videoKey} ({metadata.ContentLength} bytes)");
                return null;
            }
            catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey" || e.ErrorCode == "NotFound")
            {
                _logger.LogLine($"Video file {videoKey} not found in S3");
                // Try to get the event data (JSON) for this eventId
                var eventKey = videoKey.Replace(".mp4", ".json");
                object? eventData = null;
                try
                {
                    eventData = await RetrieveEventDataAsync(eventKey);
                }
                catch (Exception ex)
                {
                    _logger.LogLine($"Could not retrieve event data for missing video: {eventKey}: {ex.Message}");
                }
                var response = new {
                    eventId = eventId,
                    eventData = eventData,
                    message = $"Video file for event {eventId} not found. Event data is available, but no video exists."
                };
                return new APIGatewayProxyResponse {
                    StatusCode = 404,
                    Body = JsonConvert.SerializeObject(response, Formatting.Indented),
                    Headers = _responseHelper.GetStandardHeaders()
                };
            }
        }

        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task<APIGatewayProxyResponse> BuildEventVideoResponse(string eventKey, string videoKey,
            string eventId, long timestamp, object? eventData)
        {
            try
            {
                DateTime dt = timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime : DateTime.UtcNow;
                
                // Try to get the original filename from the event data
                string suggestedFilename = Path.GetFileName(videoKey); // Default fallback
                if (eventData is Alarm alarm && alarm.triggers?.Count > 0 && !string.IsNullOrEmpty(alarm.triggers[0].originalFileName))
                {
                    suggestedFilename = alarm.triggers[0].originalFileName!;
                    _logger.LogLine($"Using original downloaded filename: {suggestedFilename}");
                }
                else
                {
                    _logger.LogLine($"Using S3 key filename as fallback: {suggestedFilename}");
                }

                var presignedRequest = new GetPreSignedUrlRequest
                {
                    BucketName = AppConfiguration.AlarmBucketName!,
                    Key = videoKey,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddHours(1),
                    ResponseHeaderOverrides = new ResponseHeaderOverrides
                    {
                        ContentDisposition = $"attachment; filename=\"{suggestedFilename}\""
                    }
                };

                string presignedUrl = await _s3Client.GetPreSignedURLAsync(presignedRequest);
                _logger.LogLine($"Generated presigned URL for {videoKey}, expires in 1 hour");

                // Remove sources and conditions from eventData if present
                object? sanitizedEventData = eventData is Alarm alarmObj ? CloneAlarmWithoutSourcesAndConditions(alarmObj) : eventData;
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
                    eventData = sanitizedEventData,
                    message = "Use the downloadUrl to download the video file directly. URL expires in 1 hour."
                };
                return _responseHelper.CreateSuccessResponse(responseData);
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error building event video response: {ex.Message}");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, $"Error building response: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns a copy of the Alarm object with sources and conditions set to null.
        /// </summary>
        private static Alarm CloneAlarmWithoutSourcesAndConditions(Alarm alarm)
        {
            if (alarm == null) return null!;
            return new Alarm
            {
                name = alarm.name,
                sources = null,
                conditions = null,
                triggers = alarm.triggers,
                timestamp = alarm.timestamp,
                eventPath = alarm.eventPath,
                eventLocalLink = alarm.eventLocalLink
            };
        }

        /// <summary>
        /// Retrieves a binary file from S3.
        /// </summary>
        /// <param name="s3Key">The S3 key of the file to retrieve</param>
        /// <returns>The file data as byte array, or null if not found</returns>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        public async Task<byte[]?> GetFileAsync(string s3Key)
        {
            try
            {
                if (string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
                {
                    _logger.LogLine("StorageBucket environment variable is not configured");
                    return null;
                }

                _logger.LogLine($"Attempting to get binary file: {s3Key} from {AppConfiguration.AlarmBucketName}");

                var getObjectRequest = new GetObjectRequest
                {
                    BucketName = AppConfiguration.AlarmBucketName,
                    Key = s3Key,
                };

                using var response = await _s3Client.GetObjectAsync(getObjectRequest);
                using var ms = new MemoryStream();
                await response.ResponseStream.CopyToAsync(ms);
                
                var fileBytes = ms.ToArray();
                _logger.LogLine($"Successfully retrieved binary file from S3: {s3Key} ({fileBytes.Length} bytes)");
                return fileBytes;
            }
            catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey")
            {
                _logger.LogLine($"Binary file not found in S3: {s3Key}");
                return null;
            }
            catch (Exception e)
            {
                _logger.LogLine($"Error retrieving binary file from S3 {s3Key}: {e.Message}");
                return null;
            }
        }

        #endregion
    }
}