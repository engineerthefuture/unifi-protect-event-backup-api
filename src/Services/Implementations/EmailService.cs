using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.Lambda.Core;
using System.Text;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for sending email notifications using AWS SES.
    /// </summary>
    [ExcludeFromCodeCoverage] // Requires AWS SES connectivity
    public class EmailService : IEmailService
    {
        private readonly AmazonSimpleEmailServiceClient _sesClient;
        private readonly ILambdaLogger _logger;

        public EmailService(AmazonSimpleEmailServiceClient sesClient, ILambdaLogger logger, IS3StorageService s3StorageService)
        {
            _sesClient = sesClient;
            _logger = logger;
            // s3StorageService parameter is kept for future use but not currently needed
        }

        /// <summary>
        /// Sends a failure notification email with attachments for a failed DLQ message.
        /// </summary>
        /// <param name="alarm">The failed alarm event</param>
        /// <param name="failureReason">The reason for the failure</param>
        /// <param name="messageId">The SQS message ID</param>
        /// <param name="retryAttempt">The retry attempt timestamp</param>
        /// <returns>True if email was sent successfully, false otherwise</returns>
        public async Task<bool> SendFailureNotificationAsync(Alarm alarm, string failureReason, string messageId, string retryAttempt)
        {
            var supportEmail = AppConfiguration.SupportEmail;
            if (string.IsNullOrEmpty(supportEmail))
            {
                _logger.LogLine("Support email not configured, skipping failure notification");
                return false;
            }

            try
            {
                var subject = $"Unifi Protect Video Download Failure - Event {alarm.triggers?.FirstOrDefault()?.eventId ?? "Unknown"}";
                var body = GenerateEmailBody(alarm, failureReason, messageId, retryAttempt);
                
                var sendRequest = new SendEmailRequest
                {
                    Source = supportEmail,
                    Destination = new Destination
                    {
                        ToAddresses = new List<string> { supportEmail }
                    },
                    Message = new Message
                    {
                        Subject = new Content(subject),
                        Body = new Body
                        {
                            Html = new Content(body)
                        }
                    }
                };

                _logger.LogLine($"Sending failure notification email to {supportEmail}");
                var response = await _sesClient.SendEmailAsync(sendRequest);
                _logger.LogLine($"Email sent successfully with MessageId: {response.MessageId}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Failed to send failure notification email: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Generates the HTML email body for failure notifications.
        /// </summary>
        private static string GenerateEmailBody(Alarm alarm, string failureReason, string messageId, string retryAttempt)
        {
            var trigger = alarm.triggers?.FirstOrDefault();
            var deviceName = !string.IsNullOrEmpty(trigger?.device) 
                ? AppConfiguration.GetDeviceName(trigger.device.Replace(":", ""))
                : "Unknown Device";
            
            var eventTime = alarm.timestamp > 0 
                ? DateTimeOffset.FromUnixTimeMilliseconds(alarm.timestamp).ToString("yyyy-MM-dd HH:mm:ss UTC")
                : "Unknown";

            var eventKey = trigger?.eventId ?? "Unknown";
            var eventPath = alarm.eventPath ?? "Not available";

            var logGroupUrl = GenerateCloudWatchLogsUrl();
            var s3EventUrl = GenerateS3EventUrl(eventKey);

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><style>");
            sb.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; margin: 20px 0; }");
            sb.AppendLine("th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }");
            sb.AppendLine("th { background-color: #f2f2f2; }");
            sb.AppendLine(".failure { color: #d32f2f; font-weight: bold; }");
            sb.AppendLine(".info { background-color: #e3f2fd; padding: 15px; border-radius: 5px; margin: 15px 0; }");
            sb.AppendLine("a { color: #1976d2; text-decoration: none; }");
            sb.AppendLine("a:hover { text-decoration: underline; }");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine("<h1>🚨 Unifi Protect Video Download Failure</h1>");
            sb.AppendLine($"<p class='failure'>A video download has failed and been sent to the Dead Letter Queue for retry.</p>");

            sb.AppendLine("<h2>📋 Failure Details</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><th>Failure Reason</th><td class='failure'>{failureReason}</td></tr>");
            sb.AppendLine($"<tr><th>SQS Message ID</th><td>{messageId}</td></tr>");
            sb.AppendLine($"<tr><th>Retry Attempt Time</th><td>{retryAttempt}</td></tr>");
            sb.AppendLine("</table>");

            sb.AppendLine("<h2>🏠 Event Information</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><th>Event ID</th><td>{eventKey}</td></tr>");
            sb.AppendLine($"<tr><th>Device</th><td>{deviceName}</td></tr>");
            sb.AppendLine($"<tr><th>Event Time</th><td>{eventTime}</td></tr>");
            sb.AppendLine($"<tr><th>Event Path</th><td>{eventPath}</td></tr>");
            sb.AppendLine("</table>");

            sb.AppendLine("<h2>🔗 Related Resources</h2>");
            sb.AppendLine("<div class='info'>");
            sb.AppendLine("<ul>");
            
            if (!string.IsNullOrEmpty(logGroupUrl))
            {
                sb.AppendLine($"<li><a href='{logGroupUrl}' target='_blank'>📊 CloudWatch Logs</a> - View detailed execution logs</li>");
            }
            
            if (!string.IsNullOrEmpty(s3EventUrl))
            {
                sb.AppendLine($"<li><a href='{s3EventUrl}' target='_blank'>📁 S3 Event Data</a> - View stored alarm event data</li>");
            }
            
            sb.AppendLine("<li>📱 <strong>SQS Dead Letter Queue</strong> - Check AWS Console for retry options</li>");
            sb.AppendLine("</ul>");
            sb.AppendLine("</div>");

            sb.AppendLine("<h2>📄 Alarm Event JSON</h2>");
            sb.AppendLine("<details><summary>Click to expand full alarm data</summary>");
            sb.AppendLine("<pre style='background-color: #f5f5f5; padding: 15px; border-radius: 5px; overflow-x: auto;'>");
            sb.AppendLine(JsonConvert.SerializeObject(alarm, Formatting.Indented));
            sb.AppendLine("</pre>");
            sb.AppendLine("</details>");

            sb.AppendLine("<div class='info'>");
            sb.AppendLine("<h3>🔧 Next Steps</h3>");
            sb.AppendLine("<ol>");
            sb.AppendLine("<li>Review the CloudWatch logs for detailed error information</li>");
            sb.AppendLine("<li>Check if the Unifi Protect system is accessible and responsive</li>");
            sb.AppendLine("<li>Verify the event path is valid and the video is available</li>");
            sb.AppendLine("<li>Manually retry processing from the SQS Dead Letter Queue if needed</li>");
            sb.AppendLine("</ol>");
            sb.AppendLine("</div>");

            sb.AppendLine("<hr>");
            sb.AppendLine("<p><small>This is an automated notification from the Unifi Protect Event Backup API. ");
            sb.AppendLine("For more information, check the <a href='https://github.com/engineerthefuture/unifi-protect-event-backup-api' target='_blank'>project repository</a>.</small></p>");

            sb.AppendLine("</body></html>");
            
            return sb.ToString();
        }

        /// <summary>
        /// Generates a CloudWatch Logs URL for the specific execution.
        /// </summary>
        private static string? GenerateCloudWatchLogsUrl()
        {
            try
            {
                var functionName = AppConfiguration.FunctionName;
                if (string.IsNullOrEmpty(functionName))
                    return null;

                var region = AppConfiguration.AwsRegion.SystemName;
                var logGroup = $"/aws/lambda/{functionName}";
                
                // Generate approximate time range (last hour for context)
                var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var startTime = endTime - (60 * 60 * 1000); // 1 hour ago

                return $"https://{region}.console.aws.amazon.com/cloudwatch/home?region={region}#logsV2:log-groups/log-group/{Uri.EscapeDataString(logGroup)}/log-events?start={startTime}&end={endTime}";
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Generates an S3 URL for the stored event data.
        /// </summary>
        private static string? GenerateS3EventUrl(string eventKey)
        {
            try
            {
                var bucketName = AppConfiguration.AlarmBucketName;
                if (string.IsNullOrEmpty(bucketName) || string.IsNullOrEmpty(eventKey))
                    return null;

                var region = AppConfiguration.AwsRegion.SystemName;
                var objectKey = $"alarm-events/{eventKey}.json";
                
                return $"https://s3.console.aws.amazon.com/s3/object/{bucketName}?region={region}&prefix={Uri.EscapeDataString(objectKey)}";
            }
            catch
            {
                return null;
            }
        }
    }
}
