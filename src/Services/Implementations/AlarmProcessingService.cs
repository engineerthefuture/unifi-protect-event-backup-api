/************************
 * Unifi Webhook Event Receiver
 * AlarmProcessingService.cs
 * 
 * Service for processing Unifi Protect alarm events.
 * Handles alarm validation, enhancement, storage, and video download orchestration.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Models;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for processing Unifi Protect alarm events.
    /// </summary>
    public class AlarmProcessingService : IAlarmProcessingService
    {
        private const int PRESIGNED_VIDEO_URL_HOURS = 24;
        private readonly IS3StorageService _s3StorageService;
        private readonly IUnifiProtectService _unifiProtectService;
        private readonly ICredentialsService _credentialsService;
        private readonly IResponseHelper _responseHelper;
        private readonly ILambdaLogger _logger;
        private readonly ISummaryEventQueueService _summaryEventQueueService;

        /// <summary>
        /// Initializes a new instance of the AlarmProcessingService.
        /// </summary>
        public AlarmProcessingService(
            IS3StorageService s3StorageService,
            IUnifiProtectService unifiProtectService,
            ICredentialsService credentialsService,
            IResponseHelper responseHelper,
            ILambdaLogger logger,
            ISummaryEventQueueService summaryEventQueueService)
        {
            _s3StorageService = s3StorageService ?? throw new ArgumentNullException(nameof(s3StorageService));
            _unifiProtectService = unifiProtectService ?? throw new ArgumentNullException(nameof(unifiProtectService));
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _responseHelper = responseHelper ?? throw new ArgumentNullException(nameof(responseHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _summaryEventQueueService = summaryEventQueueService ?? throw new ArgumentNullException(nameof(summaryEventQueueService));
        }

        /// <summary>
        /// Processes a Unifi Protect alarm event and stores it in S3.
        /// </summary>
        /// <param name="alarm">The alarm event to process</param>
        /// <returns>API Gateway response indicating success or failure</returns>
        public async Task<APIGatewayProxyResponse> ProcessAlarmAsync(Alarm alarm)
        {
            _logger.LogLine("Executing alarm processing function.");

            try
            {
                // Get Unifi credentials from Secrets Manager
                var credentials = await _credentialsService.GetUnifiCredentialsAsync();

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
                _logger.LogLine(AppConfiguration.ERROR_MESSAGE_500 + e.Message);
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, AppConfiguration.ERROR_MESSAGE_500 + e.Message);
            }
        }

        /// <summary>
        /// Processes a Unifi Protect alarm event for SQS context.
        /// Throws exceptions instead of returning HTTP responses to allow SQS error handling.
        /// </summary>
        /// <param name="alarm">The alarm event to process</param>
        /// <returns>Task that completes when processing is done</returns>
        /// <exception cref="InvalidOperationException">Thrown when video download fails with "NoVideoFilesDownloaded"</exception>
        public async Task ProcessAlarmForSqsAsync(Alarm alarm)
        {
            _logger.LogLine("Executing SQS alarm processing function.");

            // Get Unifi credentials from Secrets Manager
            var credentials = await _credentialsService.GetUnifiCredentialsAsync();

            // Validate the alarm object
            if (alarm == null)
            {
                _logger.LogLine("Error: " + AppConfiguration.ERROR_GENERAL);
                throw new ArgumentException(AppConfiguration.ERROR_GENERAL);
            }

            _logger.LogLine("Alarm: " + JsonConvert.SerializeObject(alarm));

            if (alarm.triggers == null || alarm.triggers.Count == 0)
            {
                _logger.LogLine("Error: " + AppConfiguration.ERROR_TRIGGERS);
                throw new ArgumentException(AppConfiguration.ERROR_TRIGGERS);
            }

            // Process the alarm - this will throw exceptions if there are issues
            await ProcessValidAlarmForSqs(alarm, credentials);
        }

        #region Private Helper Methods

        /// <summary>
        /// Validates the alarm object and returns an error response if invalid.
        /// </summary>
        /// <param name="alarm">The alarm object to validate</param>
        /// <returns>Error response if invalid, null if valid</returns>
        private APIGatewayProxyResponse? ValidateAlarmObject(Alarm? alarm)
        {
            if (alarm == null)
            {
                _logger.LogLine("Error: " + AppConfiguration.ERROR_GENERAL);
                return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_GENERAL);
            }

            _logger.LogLine("Alarm: " + JsonConvert.SerializeObject(alarm));

            if (alarm.triggers == null || alarm.triggers.Count == 0)
            {
                _logger.LogLine("Error: " + AppConfiguration.ERROR_TRIGGERS);
                return _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, AppConfiguration.ERROR_MESSAGE_400 + AppConfiguration.ERROR_TRIGGERS);
            }

            return null; // Valid alarm
        }

        /// <summary>
        /// Processes a valid alarm by extracting event details, mapping device names, and uploading to S3.
        /// </summary>
        /// <param name="alarm">The validated alarm object</param>
        /// <param name="credentials">Unifi credentials for video download</param>
        /// <returns>Success response with processed alarm details</returns>
        private async Task<APIGatewayProxyResponse> ProcessValidAlarm(Alarm alarm, UnifiCredentials credentials)
        {
            // Extract and enhance trigger information
            var trigger = ExtractAndEnhanceTriggerDetails(alarm);
            
            // Validate storage configuration
            if (string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
            {
                _logger.LogLine("StorageBucket environment variable is not configured");
                return _responseHelper.CreateErrorResponse(HttpStatusCode.InternalServerError, "Server configuration error: StorageBucket not configured");
            }

            // Store alarm data in S3
            var eventKey = await _s3StorageService.StoreAlarmEventAsync(alarm, trigger);
            _logger.LogLine($"Alarm event stored in S3 with key: {eventKey}");

            // Store thumbnail if provided
            if (!string.IsNullOrEmpty(trigger.thumbnail))
            {
                var thumbnailKey = GenerateThumbnailKey(trigger, alarm.timestamp);
                _logger.LogLine($"Thumbnail data found, storing to S3 with key: {thumbnailKey}");
                await _s3StorageService.StoreThumbnailAsync(trigger.thumbnail, thumbnailKey);
            }
            else
            {
                _logger.LogLine("No thumbnail data provided in trigger");
            }

            // Download and store video if event path is available
            if (!string.IsNullOrEmpty(alarm.eventPath))
            {
                _logger.LogLine($"Event path found: {alarm.eventPath}, initiating video download");
                await DownloadAndStoreVideo(alarm, credentials, trigger);
            }
            else
            {
                _logger.LogLine("No event path provided, skipping video download");
            }

            // Return success response
            return _responseHelper.CreateSuccessResponse(trigger, alarm.timestamp);
        }

        /// <summary>
        /// Processes a valid alarm for SQS context - throws exceptions instead of returning responses.
        /// </summary>
        /// <param name="alarm">The validated alarm object</param>
        /// <param name="credentials">Unifi credentials for video download</param>
        /// <returns>Task that completes when processing is done</returns>
        /// <exception cref="InvalidOperationException">Thrown when video download fails or configuration is invalid</exception>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "Complex alarm processing logic with comprehensive error handling")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1541:Methods should not be too complex", Justification = "Complex alarm processing logic with comprehensive error handling")]
        private async Task ProcessValidAlarmForSqs(Alarm alarm, UnifiCredentials credentials)
        {
            // Extract and enhance trigger information
            var trigger = ExtractAndEnhanceTriggerDetails(alarm);
            
            // Validate storage configuration
            if (string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
            {
                _logger.LogLine("StorageBucket environment variable is not configured");
                throw new InvalidOperationException("Server configuration error: StorageBucket not configured");
            }

            // Store alarm data in S3
            var eventKey = await _s3StorageService.StoreAlarmEventAsync(alarm, trigger);
            _logger.LogLine($"Alarm event stored in S3 with key: {eventKey}");

            // Store thumbnail if available
            if (!string.IsNullOrEmpty(trigger.thumbnail))
            {
                try
                {
                    var thumbnailKey = GenerateThumbnailKey(trigger, alarm.timestamp);
                    await _s3StorageService.StoreThumbnailAsync(trigger.thumbnail, thumbnailKey);
                    _logger.LogLine($"Thumbnail stored in S3 with key: {thumbnailKey}");
                }
                catch (Exception ex)
                {
                    _logger.LogLine($"Failed to store thumbnail (non-critical): {ex.Message}");
                }
            }

            // Download and store video if event path is available
            if (!string.IsNullOrEmpty(alarm.eventPath))
            {
                _logger.LogLine($"Event path found: {alarm.eventPath}, initiating video download");
                await DownloadAndStoreVideo(alarm, credentials, trigger);
            }
            else
            {
                _logger.LogLine("No event path provided, skipping video download");
            }

            // Queue the alarm results for summary event processing
            var triggerForSummary = alarm.triggers?.FirstOrDefault();
            string? presignedVideoUrl = null;
            if (!string.IsNullOrEmpty(triggerForSummary?.videoKey) && !string.IsNullOrEmpty(AppConfiguration.AlarmBucketName))
            {
                try
                {
                    presignedVideoUrl = await _s3StorageService.GetPresignedUrlAsync(
                        AppConfiguration.AlarmBucketName,
                        triggerForSummary.videoKey,
                        DateTime.UtcNow,
                        PRESIGNED_VIDEO_URL_HOURS);
                }
                catch (Exception ex)
                {
                    _logger.LogLine($"Failed to generate presigned video URL: {ex.Message}");
                }
            }
            var summaryEvent = new SummaryEvent
            {
                EventId = triggerForSummary?.eventId,
                Device = triggerForSummary?.device,
                Timestamp = alarm.timestamp,
                AlarmS3Key = eventKey,
                VideoS3Key = triggerForSummary?.videoKey,
                PresignedVideoUrl = presignedVideoUrl,
                AlarmName = alarm.name,
                DeviceName = triggerForSummary?.deviceName,
                EventType = triggerForSummary?.key,
                Metadata = new System.Collections.Generic.Dictionary<string, string>()
            };
            
            // Include thumbnail data in metadata if available
            if (!string.IsNullOrEmpty(triggerForSummary?.thumbnail))
            {
                summaryEvent.Metadata["thumbnail"] = triggerForSummary.thumbnail;
            }
            
            // Include original filename in metadata if available
            if (!string.IsNullOrEmpty(triggerForSummary?.originalFileName))
            {
                summaryEvent.Metadata["originalFileName"] = triggerForSummary.originalFileName;
                _logger.LogLine($"Added originalFileName to summary event metadata: {triggerForSummary.originalFileName}");
            }
            else
            {
                _logger.LogLine("No originalFileName available in trigger for summary event");
            }
            await _summaryEventQueueService.SendSummaryEventAsync(summaryEvent);

            _logger.LogLine("SQS alarm processing completed successfully");
        }

        /// <summary>
        /// Extracts trigger details and enhances with device mapping and timestamps.
        /// </summary>
        /// <param name="alarm">The alarm object containing trigger data</param>
        /// <returns>Enhanced trigger object with device name and date information</returns>
        private Trigger ExtractAndEnhanceTriggerDetails(Alarm alarm)
        {
            // Validate alarm and triggers
            if (alarm?.triggers == null || alarm.triggers.Count == 0)
            {
                throw new ArgumentException("Alarm must contain at least one trigger");
            }

            Trigger trigger = alarm.triggers[0];
            if (trigger == null)
            {
                throw new ArgumentException("First trigger cannot be null");
            }

            string device = trigger.device ?? string.Empty;
            long timestamp = alarm.timestamp;

            // Set date from timestamp
            DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            string date = string.Format("{0:s}", dt);
            trigger.date = date;
            _logger.LogLine("Date: " + date);

            // Map device Mac to device name and update object
            string deviceName = AppConfiguration.GetDeviceName(device);
            trigger.deviceName = deviceName;

            // Generate S3 keys for the trigger
            var (eventKey, videoKey) = _s3StorageService.GenerateS3Keys(trigger, timestamp);
            trigger.eventKey = Path.GetFileName(eventKey);  // Store just the filename
            trigger.videoKey = videoKey;  // Store the full S3 key including date folder
            
            // Update the alarm object with the enhanced trigger
            alarm.triggers[0] = trigger;

            return trigger;
        }

        /// <summary>
        /// Downloads video from Unifi Protect and stores it in S3.
        /// </summary>
        /// <param name="alarm">The alarm object containing event path</param>
        /// <param name="credentials">Unifi credentials for video download</param>
        /// <param name="trigger">The enhanced trigger information</param>
        /// <summary>
        /// Downloads video file from Unifi Protect and stores it in S3.
        /// Handles error scenarios gracefully and cleans up temporary files.
        /// </summary>
        /// <param name="alarm">The alarm containing video metadata</param>
        /// <param name="credentials">Unifi credentials for authentication</param>
        /// <param name="trigger">Trigger information for file naming</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "Complex video download logic with comprehensive error handling")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1541:Methods should not be too complex", Justification = "Complex video download logic with comprehensive error handling")]
        private async Task DownloadAndStoreVideo(Alarm alarm, UnifiCredentials credentials, Trigger trigger)
        {
            try
            {
                // Ensure hostname has protocol prefix
                string hostname = credentials.hostname;
                if (!hostname.StartsWith("http://") && !hostname.StartsWith("https://"))
                {
                    hostname = "https://" + hostname;
                }
                
                string eventLocalLink = hostname + alarm.eventPath;
                _logger.LogLine($"Starting video download for event: {trigger.eventId}");
                _logger.LogLine($"Event local link: {eventLocalLink}");
                _logger.LogLine($"Using credentials hostname: {credentials.hostname}");

                // Download video using the Unifi Protect service
                var tempVideoPath = await _unifiProtectService.DownloadVideoAsync(trigger, eventLocalLink, alarm.timestamp);
                _logger.LogLine($"Video downloaded to temporary file: {tempVideoPath}");
                
                // Verify the file exists and check its size
                if (File.Exists(tempVideoPath))
                {
                    var fileInfo = new FileInfo(tempVideoPath);
                    _logger.LogLine($"Temporary video file exists: {fileInfo.Length} bytes");
                }
                else
                {
                    _logger.LogLine($"ERROR: Temporary video file does not exist: {tempVideoPath}");
                    return;
                }

                try
                {
                    // Generate S3 key for video storage
                    var (_, videoKey) = _s3StorageService.GenerateS3Keys(trigger, alarm.timestamp);
                    _logger.LogLine($"Generated S3 video key: {videoKey}");

                    // Store video in S3
                    _logger.LogLine("About to upload video to S3...");
                    await _s3StorageService.StoreVideoFileAsync(tempVideoPath, videoKey);
                    _logger.LogLine($"Video successfully stored in S3 with key: {videoKey}");
                    
                    // Update alarm in S3 with the original filename information
                    if (!string.IsNullOrEmpty(trigger.originalFileName))
                    {
                        _logger.LogLine($"Updating alarm in S3 with original filename: {trigger.originalFileName}");
                        await _s3StorageService.StoreAlarmEventAsync(alarm, trigger);
                        _logger.LogLine("Alarm updated in S3 with original filename information");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogLine($"ERROR uploading video to S3: {ex}");
                    // Don't rethrow - let the outer catch handle all video-related failures
                }
                finally
                {
                    // Clean up temporary file
                    _logger.LogLine($"Cleaning up temporary video file: {tempVideoPath}");
                    _unifiProtectService.CleanupTempFile(tempVideoPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error downloading or storing video for event {trigger.eventId}: {ex.Message}");
                _logger.LogLine($"Exception details: {ex}");
                
                // Check if this is the "No video files were downloaded" error
                if (ex.InnerException is FileNotFoundException fileNotFound && 
                    fileNotFound.Message.Contains("No video files were downloaded"))
                {
                    _logger.LogLine("Detected 'No video files were downloaded'");
                    // Throw a specific exception that can be caught by SqsService
                    throw new InvalidOperationException("NoVideoFilesDownloaded", ex);
                }
                
                // Don't fail the entire alarm processing if video download fails
                // The event data is still valuable without the video
            }
        }

        /// <summary>
        /// Generates an S3 key for storing thumbnail images.
        /// </summary>
        /// <param name="trigger">The trigger information</param>
        /// <param name="timestamp">The event timestamp</param>
        /// <returns>S3 key for the thumbnail file</returns>
        private static string GenerateThumbnailKey(Trigger trigger, long timestamp)
        {
            DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            string dateFolder = $"{dt.Year}-{dt.Month:D2}-{dt.Day:D2}";
            return $"{dateFolder}/{trigger.eventId}_{trigger.device}_{timestamp}.jpg";
        }

        #endregion
    }
}
