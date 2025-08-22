using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.Lambda.Core;
using System.Text;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver;
using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using UnifiWebhookEventReceiver.Services;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for sending email notifications using AWS SES.
    /// </summary>
    [ExcludeFromCodeCoverage] // Requires AWS SES connectivity
    public class EmailService : IEmailService
    {
        private readonly AmazonSimpleEmailServiceClient _sesClient;
        private readonly AmazonCloudWatchLogsClient _cloudWatchLogsClient;
        private readonly ILambdaLogger _logger;
        private readonly IS3StorageService _s3StorageService;

        public EmailService(AmazonSimpleEmailServiceClient sesClient, AmazonCloudWatchLogsClient cloudWatchLogsClient, ILambdaLogger logger, IS3StorageService s3StorageService)
        {
            _sesClient = sesClient;
            _cloudWatchLogsClient = cloudWatchLogsClient;
            _logger = logger;
            _s3StorageService = s3StorageService;
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
                
                // Get CloudWatch logs for the failure
                var cloudWatchLogs = await GetRecentCloudWatchLogs();
                
                // Get screenshots from S3
                var screenshots = await GetScreenshotsFromS3(alarm);
                
                // Generate email body with embedded JSON
                var body = GenerateEmailBodyWithAttachments(alarm, failureReason, messageId, retryAttempt, cloudWatchLogs, screenshots);
                
                // Send email with attachments using SendRawEmail
                var success = await SendRawEmailWithAttachments(supportEmail, subject, body, cloudWatchLogs, screenshots, alarm);
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Failed to send failure notification email: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Retrieves recent CloudWatch logs for the Lambda function.
        /// </summary>
        private async Task<string> GetRecentCloudWatchLogs()
        {
            try
            {
                var functionName = AppConfiguration.FunctionName;
                if (string.IsNullOrEmpty(functionName))
                {
                    return "CloudWatch logs unavailable - function name not configured";
                }

                var logGroupName = $"/aws/lambda/{functionName}";
                var endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var startTime = endTime - (30 * 60 * 1000); // Last 30 minutes

                var filterRequest = new FilterLogEventsRequest
                {
                    LogGroupName = logGroupName,
                    StartTime = startTime,
                    EndTime = endTime,
                    Limit = 100
                };

                _logger.LogLine($"Retrieving CloudWatch logs from {logGroupName}");
                var response = await _cloudWatchLogsClient.FilterLogEventsAsync(filterRequest);
                
                if (response.Events?.Count > 0)
                {
                    var logEvents = response.Events
                        .OrderByDescending(e => e.Timestamp)
                        .Take(50) // Get last 50 log entries
                        .Select(e => $"{DateTimeOffset.FromUnixTimeMilliseconds(e.Timestamp):yyyy-MM-dd HH:mm:ss UTC} - {e.Message}")
                        .ToList();
                    
                    return string.Join("\n", logEvents);
                }
                
                return "No recent log events found";
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error retrieving CloudWatch logs: {ex.Message}");
                return $"Error retrieving CloudWatch logs: {ex.Message}";
            }
        }

        /// <summary>
        /// Retrieves screenshots from S3 for the alarm event.
        /// </summary>
        private async Task<List<(string name, byte[] data)>> GetScreenshotsFromS3(Alarm alarm)
        {
            var screenshots = new List<(string name, byte[] data)>();
            
            try
            {
                var trigger = alarm.triggers?.FirstOrDefault();
                if (trigger == null)
                {
                    _logger.LogLine("No trigger found in alarm for screenshot retrieval");
                    return screenshots;
                }

                // Calculate the date folder for screenshots
                DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(alarm.timestamp).LocalDateTime;
                string dateFolder = $"{dt.Year}-{dt.Month:D2}-{dt.Day:D2}";
                string basePrefix = $"{trigger.eventId}_{trigger.device}_{alarm.timestamp}";
                
                var screenshotTypes = new[] { "login-screenshot", "pageload-screenshot", "firstclick-screenshot", "secondclick-screenshot" };
                
                foreach (var screenshotType in screenshotTypes)
                {
                    await TryRetrieveScreenshot(screenshotType, dateFolder, basePrefix, screenshots);
                }
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Error retrieving screenshots from S3: {ex.Message}");
            }
            
            return screenshots;
        }

        /// <summary>
        /// Attempts to retrieve a single screenshot from S3.
        /// </summary>
        private async Task TryRetrieveScreenshot(string screenshotType, string dateFolder, string basePrefix, List<(string name, byte[] data)> screenshots)
        {
            var s3Key = $"screenshots/{dateFolder}/{basePrefix}_{screenshotType}.png";
            _logger.LogLine($"Attempting to retrieve screenshot: {s3Key}");
            
            try
            {
                var screenshotData = await _s3StorageService.GetFileAsync(s3Key);
                if (screenshotData != null && screenshotData.Length > 0)
                {
                    screenshots.Add(($"{screenshotType}.png", screenshotData));
                    _logger.LogLine($"Retrieved screenshot {screenshotType}: {screenshotData.Length} bytes");
                }
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Could not retrieve screenshot {screenshotType}: {ex.Message}");
            }
        }

        /// <summary>
        /// Generates email body with embedded JSON and attachment information.
        /// </summary>
        private static string GenerateEmailBodyWithAttachments(Alarm alarm, string failureReason, string messageId, string retryAttempt, string cloudWatchLogs, List<(string name, byte[] data)> screenshots)
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

            var sb = new StringBuilder();
            
            // Add HTML header and styles
            AddHtmlHeaderAndStyles(sb);
            
            // Add failure information
            AddFailureInformation(sb, failureReason, messageId, retryAttempt);
            
            // Add event information
            AddEventInformation(sb, eventKey, deviceName, eventTime, eventPath);
            
            // Add alarm JSON and logs
            AddAlarmDataAndLogs(sb, alarm, cloudWatchLogs);
            
            // Add attachments information
            AddAttachmentsInformation(sb, screenshots);
            
            // Add next steps and footer
            AddNextStepsAndFooter(sb);

            sb.AppendLine("</body></html>");
            
            return sb.ToString();
        }

        /// <summary>
        /// Adds HTML header and CSS styles to the email body.
        /// </summary>
        private static void AddHtmlHeaderAndStyles(StringBuilder sb)
        {
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><style>");
            sb.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; margin: 20px 0; }");
            sb.AppendLine("th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }");
            sb.AppendLine("th { background-color: #f2f2f2; }");
            sb.AppendLine(".failure { color: #d32f2f; font-weight: bold; }");
            sb.AppendLine(".info { background-color: #e3f2fd; padding: 15px; border-radius: 5px; margin: 15px 0; }");
            sb.AppendLine(".logs { background-color: #f5f5f5; padding: 15px; border-radius: 5px; font-family: monospace; font-size: 12px; white-space: pre-line; max-height: 400px; overflow-y: auto; }");
            sb.AppendLine("a { color: #1976d2; text-decoration: none; }");
            sb.AppendLine("a:hover { text-decoration: underline; }");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine("<h1>🚨 Unifi Protect Video Download Failure</h1>");
            sb.AppendLine($"<p class='failure'>A video download has failed and been sent to the Dead Letter Queue for retry.</p>");
        }

        /// <summary>
        /// Adds failure details section to the email body.
        /// </summary>
        private static void AddFailureInformation(StringBuilder sb, string failureReason, string messageId, string retryAttempt)
        {
            sb.AppendLine("<h2>📋 Failure Details</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><th>Failure Reason</th><td class='failure'>{failureReason}</td></tr>");
            sb.AppendLine($"<tr><th>SQS Message ID</th><td>{messageId}</td></tr>");
            sb.AppendLine($"<tr><th>Retry Attempt Time</th><td>{retryAttempt}</td></tr>");
            sb.AppendLine("</table>");
        }

        /// <summary>
        /// Adds event information section to the email body.
        /// </summary>
        private static void AddEventInformation(StringBuilder sb, string eventKey, string deviceName, string eventTime, string eventPath)
        {
            sb.AppendLine("<h2>🏠 Event Information</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><th>Event ID</th><td>{eventKey}</td></tr>");
            sb.AppendLine($"<tr><th>Device</th><td>{deviceName}</td></tr>");
            sb.AppendLine($"<tr><th>Event Time</th><td>{eventTime}</td></tr>");
            sb.AppendLine($"<tr><th>Event Path</th><td>{eventPath}</td></tr>");
            sb.AppendLine("</table>");
        }

        /// <summary>
        /// Adds alarm JSON data and CloudWatch logs to the email body.
        /// </summary>
        private static void AddAlarmDataAndLogs(StringBuilder sb, Alarm alarm, string cloudWatchLogs)
        {
            sb.AppendLine("<h2>📄 Alarm Event JSON</h2>");
            sb.AppendLine("<div class='logs'>");
            sb.AppendLine(JsonConvert.SerializeObject(alarm, Formatting.Indented));
            sb.AppendLine("</div>");

            sb.AppendLine("<h2>📊 CloudWatch Logs</h2>");
            sb.AppendLine("<div class='logs'>");
            sb.AppendLine(cloudWatchLogs);
            sb.AppendLine("</div>");
        }

        /// <summary>
        /// Adds attachments information section to the email body.
        /// </summary>
        private static void AddAttachmentsInformation(StringBuilder sb, List<(string name, byte[] data)> screenshots)
        {
            sb.AppendLine("<h2>📸 Attachments</h2>");
            sb.AppendLine("<div class='info'>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li><strong>CloudWatch Logs</strong> - Recent execution logs attached as cloudwatch-logs.txt</li>");
            sb.AppendLine("<li><strong>Alarm JSON</strong> - Complete alarm data attached as alarm-event.json</li>");
            
            if (screenshots.Count > 0)
            {
                sb.AppendLine("<li><strong>Screenshots</strong> - Process screenshots attached:</li>");
                sb.AppendLine("<ul>");
                foreach (var screenshot in screenshots)
                {
                    sb.AppendLine($"<li>{screenshot.name} ({screenshot.data.Length} bytes)</li>");
                }
                sb.AppendLine("</ul>");
            }
            else
            {
                sb.AppendLine("<li><strong>Screenshots</strong> - No screenshots available for this event</li>");
            }
            
            sb.AppendLine("</ul>");
            sb.AppendLine("</div>");
        }

        /// <summary>
        /// Adds next steps section and footer to the email body.
        /// </summary>
        private static void AddNextStepsAndFooter(StringBuilder sb)
        {
            sb.AppendLine("<div class='info'>");
            sb.AppendLine("<h3>🔧 Next Steps</h3>");
            sb.AppendLine("<ol>");
            sb.AppendLine("<li>Review the attached CloudWatch logs for detailed error information</li>");
            sb.AppendLine("<li>Check the attached screenshots to see the process state at failure</li>");
            sb.AppendLine("<li>Verify the Unifi Protect system is accessible and responsive</li>");
            sb.AppendLine("<li>Check the alarm JSON data for any inconsistencies</li>");
            sb.AppendLine("<li>Manually retry processing from the SQS Dead Letter Queue if needed</li>");
            sb.AppendLine("</ol>");
            sb.AppendLine("</div>");

            sb.AppendLine("<hr>");
            sb.AppendLine("<p><small>This is an automated notification from the Unifi Protect Event Backup API. ");
            sb.AppendLine("For more information, check the <a href='https://github.com/engineerthefuture/unifi-protect-event-backup-api' target='_blank'>project repository</a>.</small></p>");
        }

        /// <summary>
        /// Sends raw email with attachments using SES SendRawEmail.
        /// </summary>
        private async Task<bool> SendRawEmailWithAttachments(string supportEmail, string subject, string htmlBody, string cloudWatchLogs, List<(string name, byte[] data)> screenshots, Alarm alarm)
        {
            try
            {
                var boundary = Guid.NewGuid().ToString("N");
                var rawMessage = new StringBuilder();

                // Email headers
                rawMessage.AppendLine($"From: {supportEmail}");
                rawMessage.AppendLine($"To: {supportEmail}");
                rawMessage.AppendLine($"Subject: {subject}");
                rawMessage.AppendLine($"MIME-Version: 1.0");
                rawMessage.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
                rawMessage.AppendLine();

                // HTML body part
                rawMessage.AppendLine($"--{boundary}");
                rawMessage.AppendLine("Content-Type: text/html; charset=UTF-8");
                rawMessage.AppendLine("Content-Transfer-Encoding: quoted-printable");
                rawMessage.AppendLine();
                rawMessage.AppendLine(htmlBody);
                rawMessage.AppendLine();

                // CloudWatch logs attachment
                rawMessage.AppendLine($"--{boundary}");
                rawMessage.AppendLine("Content-Type: text/plain; charset=UTF-8");
                rawMessage.AppendLine("Content-Transfer-Encoding: base64");
                rawMessage.AppendLine("Content-Disposition: attachment; filename=\"cloudwatch-logs.txt\"");
                rawMessage.AppendLine();
                rawMessage.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(cloudWatchLogs)));
                rawMessage.AppendLine();

                // Alarm JSON attachment
                var alarmJson = JsonConvert.SerializeObject(alarm, Formatting.Indented);
                rawMessage.AppendLine($"--{boundary}");
                rawMessage.AppendLine("Content-Type: application/json; charset=UTF-8");
                rawMessage.AppendLine("Content-Transfer-Encoding: base64");
                rawMessage.AppendLine("Content-Disposition: attachment; filename=\"alarm-event.json\"");
                rawMessage.AppendLine();
                rawMessage.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(alarmJson)));
                rawMessage.AppendLine();

                // Screenshot attachments
                foreach (var screenshot in screenshots)
                {
                    rawMessage.AppendLine($"--{boundary}");
                    rawMessage.AppendLine("Content-Type: image/png");
                    rawMessage.AppendLine("Content-Transfer-Encoding: base64");
                    rawMessage.AppendLine($"Content-Disposition: attachment; filename=\"{screenshot.name}\"");
                    rawMessage.AppendLine();
                    rawMessage.AppendLine(Convert.ToBase64String(screenshot.data));
                    rawMessage.AppendLine();
                }

                // End boundary
                rawMessage.AppendLine($"--{boundary}--");

                var sendRequest = new SendRawEmailRequest
                {
                    Source = supportEmail,
                    Destinations = new List<string> { supportEmail },
                    RawMessage = new RawMessage
                    {
                        Data = new MemoryStream(Encoding.UTF8.GetBytes(rawMessage.ToString()))
                    }
                };

                _logger.LogLine($"Sending failure notification email with attachments to {supportEmail}");
                var response = await _sesClient.SendRawEmailAsync(sendRequest);
                _logger.LogLine($"Email sent successfully with MessageId: {response.MessageId}");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Failed to send raw email with attachments: {ex.Message}");
                return false;
            }
        }

    }
}
