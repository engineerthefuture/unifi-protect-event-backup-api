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
        private readonly AmazonS3Client _s3Client;
        private readonly IResponseHelper _responseHelper;
        private readonly ILambdaLogger _logger;

        /// <summary>
        /// Initializes a new instance of the S3StorageService.
        /// </summary>
        /// <param name="s3Client">AWS S3 client</param>
        /// <param name="responseHelper">Response helper service</param>
        /// <param name="logger">Lambda logger instance</param>
        public S3StorageService(AmazonS3Client s3Client, IResponseHelper responseHelper, ILambdaLogger logger)
        {
            _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
            _responseHelper = responseHelper ?? throw new ArgumentNullException(nameof(responseHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Stores alarm event data in S3 as JSON.
        /// </summary>
        /// <param name="alarm">The alarm data to store</param>
        /// <param name="trigger">The specific trigger information</param>
        /// <returns>The S3 key where the data was stored</returns>
        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        public async Task<string> StoreAlarmEventAsync(Alarm alarm, Trigger trigger)
        {
            ArgumentNullException.ThrowIfNull(alarm);
            ArgumentNullException.ThrowIfNull(trigger);
                
            if (string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
            {
                throw new InvalidOperationException("StorageBucket environment variable is not configured");
            }

            var (eventKey, _) = GenerateS3Keys(trigger, alarm.timestamp);
            var alarmJson = JsonConvert.SerializeObject(alarm);

            await UploadStringContentAsync(AppConfiguration.AlarmBucketName, eventKey, alarmJson, "application/json");
            return eventKey;
        }

        /// <summary>
        /// Stores a video file in S3.
        /// </summary>
        /// <param name="videoFilePath">Path to the video file to upload</param>
        /// <param name="s3Key">S3 key for the file</param>
        /// <returns>Task representing the upload operation</returns>
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
        /// Retrieves the latest video file from S3.
        /// </summary>
        /// <returns>API Gateway response with the video file</returns>
        public async Task<APIGatewayProxyResponse> GetLatestVideoAsync()
        {
            _logger.LogLine("Executing Get latest video function");

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
                _logger.LogLine($"Error retrieving latest video: {e.Message}");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, $"Error retrieving latest video: {e.Message}");
            }
        }

        /// <summary>
        /// Retrieves a video file by event ID from S3.
        /// </summary>
        /// <param name="eventId">The event ID to search for</param>
        /// <returns>API Gateway response with the video file</returns>
        public async Task<APIGatewayProxyResponse> GetVideoByEventIdAsync(string eventId)
        {
            _logger.LogLine($"Executing Get video by eventId function for eventId: {eventId}");

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
                _logger.LogLine($"Video or event data not found in S3 for eventId {eventId}: {ex.Message}");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.NotFound,
                    $"Video file for event {eventId} is not available. The video may have been automatically deleted due to the 30-day retention policy or the video download may have failed during event processing.");
            }
            catch (AmazonS3Exception ex)
            {
                _logger.LogLine($"S3 error retrieving video by eventId {eventId}: {ex.ErrorCode} - {ex.Message}");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError,
                    $"Storage service error while retrieving event {eventId}. Please try again later.");
            }
            catch (Exception e)
            {
                _logger.LogLine($"Unexpected error retrieving video by eventId {eventId}: {e.Message}");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError,
                    $"An unexpected error occurred while retrieving event {eventId}. Please try again later.");
            }
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

        private APIGatewayProxyResponse? ValidateLatestVideoConfiguration()
        {
            if (string.IsNullOrWhiteSpace(AppConfiguration.AlarmBucketName))
            {
                _logger.LogLine("StorageBucket environment variable is not configured");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured");
            }
            return null;
        }

        [ExcludeFromCodeCoverage] // Requires AWS S3 connectivity
        private async Task<(string? VideoKey, long Timestamp, APIGatewayProxyResponse? ErrorResponse)> SearchForLatestVideoAsync()
        {
            _logger.LogLine("Searching for latest video file in S3 bucket using date-organized approach: " + AppConfiguration.AlarmBucketName);

            DateTime searchDate = DateTime.UtcNow.Date;
            const int maxDaysToSearch = 30;
            int daysSearched = 0;

            while (daysSearched < maxDaysToSearch)
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
                    return eventData;
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
            const int maxDaysToSearch = 90;
            int daysSearched = 0;

            while (daysSearched < maxDaysToSearch)
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

            _logger.LogLine($"Event with eventId {eventId} not found in S3 bucket");
            return (null, null, 0, _responseHelper.CreateErrorResponse(HttpStatusCode.NotFound, $"Event with eventId {eventId} not found"));
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
            catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchKey")
            {
                _logger.LogLine($"Video file {videoKey} not found in S3");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.NotFound, $"Video file for event {eventId} not found");
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

                return _responseHelper.CreateSuccessResponse(responseData);
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error building event video response: {ex.Message}");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, $"Error building response: {ex.Message}");
            }
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
