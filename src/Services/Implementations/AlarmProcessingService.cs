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

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for processing Unifi Protect alarm events.
    /// </summary>
    public class AlarmProcessingService : IAlarmProcessingService
    {
        private readonly IS3StorageService _s3StorageService;
        private readonly IUnifiProtectService _unifiProtectService;
        private readonly ICredentialsService _credentialsService;
        private readonly IResponseHelper _responseHelper;
        private readonly ILambdaLogger _logger;

        /// <summary>
        /// Initializes a new instance of the AlarmProcessingService.
        /// </summary>
        public AlarmProcessingService(
            IS3StorageService s3StorageService,
            IUnifiProtectService unifiProtectService,
            ICredentialsService credentialsService,
            IResponseHelper responseHelper,
            ILambdaLogger logger)
        {
            _s3StorageService = s3StorageService ?? throw new ArgumentNullException(nameof(s3StorageService));
            _unifiProtectService = unifiProtectService ?? throw new ArgumentNullException(nameof(unifiProtectService));
            _credentialsService = credentialsService ?? throw new ArgumentNullException(nameof(credentialsService));
            _responseHelper = responseHelper ?? throw new ArgumentNullException(nameof(responseHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        /// Extracts trigger details and enhances with device mapping and timestamps.
        /// </summary>
        /// <param name="alarm">The alarm object containing trigger data</param>
        /// <returns>Enhanced trigger object with device name and date information</returns>
        private Trigger ExtractAndEnhanceTriggerDetails(Alarm alarm)
        {
            Trigger trigger = alarm.triggers[0];
            string device = trigger.device;
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
            trigger.videoKey = Path.GetFileName(videoKey);  // Store just the filename
            
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
        private async Task DownloadAndStoreVideo(Alarm alarm, UnifiCredentials credentials, Trigger trigger)
        {
            try
            {
                string eventLocalLink = credentials.hostname + alarm.eventPath;
                _logger.LogLine($"Starting video download for event: {trigger.eventId}");
                _logger.LogLine($"Event local link: {eventLocalLink}");
                _logger.LogLine($"Using credentials hostname: {credentials.hostname}");

                // Download video using the Unifi Protect service
                var tempVideoPath = await _unifiProtectService.DownloadVideoAsync(trigger, eventLocalLink);
                _logger.LogLine($"Video downloaded to temporary file: {tempVideoPath}");

                try
                {
                    // Generate S3 key for video storage
                    var (_, videoKey) = _s3StorageService.GenerateS3Keys(trigger, alarm.timestamp);
                    _logger.LogLine($"Generated S3 video key: {videoKey}");

                    // Store video in S3
                    await _s3StorageService.StoreVideoFileAsync(tempVideoPath, videoKey);
                    _logger.LogLine($"Video stored in S3 with key: {videoKey}");
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
                // Don't fail the entire alarm processing if video download fails
                // The event data is still valuable without the video
            }
        }

        #endregion
    }
}
