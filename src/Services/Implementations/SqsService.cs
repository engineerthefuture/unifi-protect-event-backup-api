/************************
 * Unifi Webhook Event Receiver
 * SqsService.cs
 * 
 * Service for handling SQS message processing operations.
 * Manages queue operations and delayed alarm processing.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using System.Diagnostics.CodeAnalysis;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for handling SQS message processing operations.
    /// </summary>
    public class SqsService : ISqsService
    {
        private readonly AmazonSQSClient _sqsClient;
        private readonly IAlarmProcessingService _alarmProcessingService;
        private readonly IEmailService _emailService;
        private readonly IResponseHelper _responseHelper;
        private readonly ILambdaLogger _logger;

        /// <summary>
        /// Gets the number of messages currently in the DLQ.
        /// </summary>
        /// <returns>Approximate number of messages in the DLQ</returns>
        public async Task<int> GetDlqMessageCountAsync()
        {
            var dlqUrl = AppConfiguration.AlarmProcessingDlqUrl;
            if (string.IsNullOrEmpty(dlqUrl))
            {
                _logger.LogLine("DLQ URL is not configured.");
                return 0;
            }
            try
            {
                var attrs = await _sqsClient.GetQueueAttributesAsync(dlqUrl, new List<string> { "ApproximateNumberOfMessages" });
                if (attrs.Attributes.TryGetValue("ApproximateNumberOfMessages", out var countStr) && int.TryParse(countStr, out var count))
                {
                    return count;
                }
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error fetching DLQ message count: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Initializes a new instance of the SqsService.
        /// </summary>
        /// <param name="sqsClient">AWS SQS client</param>
        /// <param name="alarmProcessingService">Alarm processing service</param>
        /// <param name="emailService">Email notification service</param>
        /// <param name="responseHelper">Response helper service</param>
        /// <param name="logger">Lambda logger instance</param>
        public SqsService(
            AmazonSQSClient sqsClient, 
            IAlarmProcessingService alarmProcessingService,
            IEmailService emailService,
            IResponseHelper responseHelper,
            ILambdaLogger logger)
        {
            _sqsClient = sqsClient ?? throw new ArgumentNullException(nameof(sqsClient));
            _alarmProcessingService = alarmProcessingService ?? throw new ArgumentNullException(nameof(alarmProcessingService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _responseHelper = responseHelper ?? throw new ArgumentNullException(nameof(responseHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes SQS events containing delayed alarm processing requests.
        /// </summary>
        /// <param name="requestBody">JSON string containing the SQS event</param>
        /// <returns>API Gateway response indicating processing status</returns>
        public async Task ProcessSqsEventAsync(string requestBody)
        {
            try
            {
                _logger.LogLine("Detected SQS event, processing delayed alarm");
                await ProcessSqsEventInternal(requestBody);
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error processing SQS event: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Determines if the request body represents an SQS event.
        /// </summary>
        /// <param name="requestBody">The request body to check</param>
        /// <returns>True if this is an SQS event, false otherwise</returns>
        public bool IsSqsEvent(string requestBody)
        {
            _logger.LogLine("Checking if request is SQS event...");
            _logger.LogLine($"Request body length: {requestBody?.Length ?? 0}");
            
            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogLine("Request body is null or empty - not an SQS event");
                return false;
            }
            
            // Log a snippet of the request body for debugging (first 500 chars)
            var snippet = requestBody.Length > 500 ? string.Concat(requestBody.AsSpan(0, 500), "...") : requestBody;
            _logger.LogLine($"Request body snippet: {snippet}");
            
            bool hasRecords = requestBody.Contains("\"Records\"");
            bool hasEventSource = requestBody.Contains("\"eventSource\":\"aws:sqs\"");
            
            _logger.LogLine($"Has 'Records': {hasRecords}");
            _logger.LogLine($"Has 'eventSource:aws:sqs': {hasEventSource}");
            
            bool isSqsEvent = hasRecords && hasEventSource;
            _logger.LogLine($"Is SQS event: {isSqsEvent}");
            
            return isSqsEvent;
        }

        /// <summary>
        /// Sends an alarm event to the SQS queue for delayed processing.
        /// </summary>
        /// <param name="alarm">The alarm event to queue</param>
        /// <returns>SQS message ID</returns>
        [ExcludeFromCodeCoverage] // Requires AWS SQS connectivity
        public async Task<string> SendAlarmToQueueAsync(Alarm alarm)
        {
            if (string.IsNullOrEmpty(AppConfiguration.AlarmProcessingQueueUrl))
            {
                throw new InvalidOperationException("AlarmProcessingQueueUrl environment variable is not configured");
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
                QueueUrl = AppConfiguration.AlarmProcessingQueueUrl,
                MessageBody = messageBody,
                DelaySeconds = AppConfiguration.ProcessingDelaySeconds,
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

            var result = await _sqsClient.SendMessageAsync(sendMessageRequest);

            _logger.LogLine($"Successfully queued alarm event {eventId} for processing in {AppConfiguration.ProcessingDelaySeconds} seconds. MessageId: {result.MessageId}");

            return result.MessageId;
        }

        /// <summary>
        /// Queues an alarm event for delayed processing and returns an appropriate response.
        /// </summary>
        /// <param name="alarm">The alarm event to queue</param>
        /// <returns>API Gateway response indicating the event has been queued</returns>
        public async Task<APIGatewayProxyResponse> QueueAlarmForProcessingAsync(Alarm alarm)
        {
            _logger.LogLine("Queueing alarm event for delayed processing");

            try
            {
                if (string.IsNullOrEmpty(AppConfiguration.AlarmProcessingQueueUrl))
                {
                    _logger.LogLine("AlarmProcessingQueueUrl environment variable is not configured");
                    return _responseHelper.CreateErrorResponse(System.Net.HttpStatusCode.InternalServerError,
                        "Server configuration error: SQS queue not configured");
                }

                // Send alarm to SQS queue
                var messageId = await SendAlarmToQueueAsync(alarm);

                // Get the first trigger for event details
                var trigger = alarm.triggers?.FirstOrDefault();
                string eventId = trigger?.eventId ?? "unknown";
                string device = trigger?.device ?? "unknown";

                // Return immediate success response
                var responseData = new
                {
                    msg = $"Alarm event has been queued for processing",
                    eventId = eventId,
                    device = device,
                    processingDelay = AppConfiguration.ProcessingDelaySeconds,
                    messageId = messageId,
                    estimatedProcessingTime = DateTime.UtcNow.AddSeconds(AppConfiguration.ProcessingDelaySeconds).ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                return _responseHelper.CreateSuccessResponse(responseData);
            }
            catch (Exception e)
            {
                _logger.LogLine($"Error queueing alarm for processing: {e.Message}");
                return _responseHelper.CreateErrorResponse(System.Net.HttpStatusCode.InternalServerError,
                    $"Error queueing alarm for processing: {e.Message}");
            }
        }

        /// <summary>
        /// Internal method to process SQS events.
        /// </summary>
        /// <param name="requestBody">JSON string containing the SQS event</param>
        private async Task ProcessSqsEventInternal(string requestBody)
        {
            var sqsEvent = JsonConvert.DeserializeObject<SQSEvent>(requestBody);
            if (sqsEvent?.Records == null)
            {
                _logger.LogLine("No SQS records found in event");
                return;
            }

            foreach (var record in sqsEvent.Records)
            {
                await ProcessSingleSqsRecord(record);
            }
        }

        /// <summary>
        /// Processes a single SQS record containing alarm data.
        /// </summary>
        /// <param name="record">The SQS record to process</param>
        private async Task ProcessSingleSqsRecord(SQSEvent.SQSMessage record)
        {
            try
            {
                _logger.LogLine($"Processing SQS message: {record.MessageId}");

                // Parse the alarm data from the message body
                var messageBody = record.Body;
                var alarm = JsonConvert.DeserializeObject<Alarm>(messageBody);

                if (alarm != null)
                {
                    _logger.LogLine($"Processing delayed alarm for device: {alarm.triggers?.FirstOrDefault()?.device}");
                    await _alarmProcessingService.ProcessAlarmForSqsAsync(alarm);
                    _logger.LogLine($"Successfully processed delayed alarm: {record.MessageId}");
                }
                else
                {
                    _logger.LogLine($"Failed to deserialize alarm from message: {record.MessageId}");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message == "NoVideoFilesDownloaded")
            {
                _logger.LogLine($"No video files were downloaded for alarm in message {record.MessageId}, sending to DLQ for retry");
                
                try
                {
                    // Parse the alarm data from the message body to send to DLQ
                    var alarm = JsonConvert.DeserializeObject<Alarm>(record.Body);
                    if (alarm != null)
                    {
                        await SendAlarmToDlqAsync(alarm, "No video files were downloaded - may require retry");
                        _logger.LogLine($"Successfully sent alarm to DLQ for retry: {record.MessageId}");
                    }
                }
                catch (Exception dlqEx)
                {
                    _logger.LogLine($"Failed to send alarm to DLQ: {dlqEx.Message}");
                    // Don't throw here - we want to continue processing other messages
                }
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error processing SQS message {record.MessageId}: {ex.Message}");
                // Don't throw here - we want to continue processing other messages
            }
        }

        /// <summary>
        /// Sends an alarm event to the dead letter queue for failed processing scenarios.
        /// </summary>
        /// <param name="alarm">The alarm event to send to DLQ</param>
        /// <param name="reason">The reason for sending to DLQ</param>
        /// <returns>SQS message ID</returns>
        public async Task<string> SendAlarmToDlqAsync(Alarm alarm, string reason)
        {
            var dlqUrl = AppConfiguration.AlarmProcessingDlqUrl;
            if (string.IsNullOrEmpty(dlqUrl))
            {
                _logger.LogLine("DLQ URL not configured, cannot send alarm to DLQ");
                throw new InvalidOperationException("DLQ URL not configured");
            }

            try
            {
                var messageBody = JsonConvert.SerializeObject(alarm);
                var messageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["FailureReason"] = new MessageAttributeValue
                    {
                        DataType = "String",
                        StringValue = reason
                    },
                    ["OriginalTimestamp"] = new MessageAttributeValue
                    {
                        DataType = "Number",
                        StringValue = alarm.timestamp.ToString()
                    },
                    ["RetryAttempt"] = new MessageAttributeValue
                    {
                        DataType = "String",
                        StringValue = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    }
                };

                var sendRequest = new SendMessageRequest
                {
                    QueueUrl = dlqUrl,
                    MessageBody = messageBody,
                    MessageAttributes = messageAttributes
                };

                _logger.LogLine($"Sending alarm to DLQ. Reason: {reason}");
                var response = await _sqsClient.SendMessageAsync(sendRequest);
                _logger.LogLine($"Successfully sent alarm to DLQ with message ID: {response.MessageId}");
                
                // Send email notification about the failure
                try
                {
                    _logger.LogLine("Sending failure notification email");
                    var retryAttemptTime = messageAttributes["RetryAttempt"].StringValue;
                    var emailSent = await _emailService.SendFailureNotificationAsync(alarm, reason, response.MessageId, retryAttemptTime);
                    if (emailSent)
                    {
                        _logger.LogLine("Failure notification email sent successfully");
                    }
                    else
                    {
                        _logger.LogLine("Failed to send failure notification email");
                    }
                }
                catch (Exception emailEx)
                {
                    _logger.LogLine($"Error sending failure notification email: {emailEx.Message}");
                    // Don't throw here - DLQ message was sent successfully, email is secondary
                }
                
                return response.MessageId;
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error sending alarm to DLQ: {ex.Message}");
                throw;
            }
        }
    }
}
