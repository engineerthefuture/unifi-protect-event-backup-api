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
                var trigger = alarm.triggers?.FirstOrDefault();
                var deviceName = !string.IsNullOrEmpty(trigger?.device) 
                    ? AppConfiguration.GetDeviceName(trigger.device.Replace(":", ""))
                    : "Unknown Device";
                var environment = AppConfiguration.DeployedEnv ?? "Unknown";
                
                var subject = $"[{environment.ToUpper()}] Unifi Protect Video Download Failure - {deviceName} - Event {trigger?.eventId ?? "Unknown"}";
                
                // Get CloudWatch logs for the failure
                var cloudWatchLogs = await GetRecentCloudWatchLogs(alarm);
                
                // Get screenshots from S3
                var screenshots = await GetScreenshotsFromS3(alarm);
                
                // Generate email body with embedded JSON
                var body = GenerateEmailBodyWithAttachments(alarm, failureReason, messageId, retryAttempt, cloudWatchLogs, screenshots);
                
                // Send email using SendRawEmail (attachments capability preserved for future use)
                var success = await SendRawEmailWithAttachments(supportEmail, subject, body);
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Failed to send failure notification email: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Retrieves recent CloudWatch logs for the Lambda function around the time of the alarm event.
        /// </summary>
        [SuppressMessage("SonarQube", "S1541:Methods should not be too complex", Justification = "CloudWatch logs retrieval requires multiple conditional checks and pagination logic")]
        private async Task<string> GetRecentCloudWatchLogs(Alarm alarm)
        {
            try
            {
                var functionName = AppConfiguration.FunctionName;
                if (string.IsNullOrEmpty(functionName))
                {
                    return "CloudWatch logs unavailable - function name not configured";
                }

                var logGroupName = $"/aws/lambda/{functionName}";
                
                // Use alarm timestamp to get logs from the relevant time period
                // Expanded window: 15 minutes before the alarm to 60 minutes after
                var alarmTime = alarm.timestamp > 0 
                    ? DateTimeOffset.FromUnixTimeMilliseconds(alarm.timestamp)
                    : DateTimeOffset.UtcNow.AddMinutes(-30);
                
                var startTime = alarmTime.AddMinutes(-15).ToUnixTimeMilliseconds();
                var endTime = alarmTime.AddMinutes(60).ToUnixTimeMilliseconds();

                _logger.LogLine($"Retrieving CloudWatch logs from {logGroupName} for alarm time {alarmTime:yyyy-MM-dd HH:mm:ss UTC}");
                
                // Get logs with pagination to retrieve more comprehensive data
                var allEvents = new List<FilteredLogEvent>();
                string? nextToken = null;
                int maxPages = 3; // Limit to prevent excessive API calls
                int pageCount = 0;

                do
                {
                    var filterRequest = new FilterLogEventsRequest
                    {
                        LogGroupName = logGroupName,
                        StartTime = startTime,
                        EndTime = endTime,
                        Limit = 1000,  // Maximum allowed per request
                        NextToken = nextToken
                    };

                    var response = await _cloudWatchLogsClient.FilterLogEventsAsync(filterRequest);
                    
                    if (response.Events != null && response.Events.Count > 0)
                    {
                        allEvents.AddRange(response.Events);
                    }
                    
                    nextToken = response.NextToken;
                    pageCount++;
                    
                } while (!string.IsNullOrEmpty(nextToken) && pageCount < maxPages);

                if (allEvents.Count > 0)
                {
                    // Prioritize error and warning messages, but include all logs
                    var allLogEvents = allEvents
                        .OrderByDescending(e => e.Timestamp)
                        .Select(e => new { 
                            Timestamp = e.Timestamp, 
                            Message = e.Message,
                            IsError = e.Message.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || 
                                     e.Message.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
                                     e.Message.Contains("Exception", StringComparison.OrdinalIgnoreCase),
                            IsWarning = e.Message.Contains("WARN", StringComparison.OrdinalIgnoreCase) ||
                                       e.Message.Contains("WARNING", StringComparison.OrdinalIgnoreCase)
                        })
                        .ToList();
                    
                    // Take up to 75 error/warning logs and fill remaining with other logs (increased coverage)
                    var errorWarningLogs = allLogEvents.Where(e => e.IsError || e.IsWarning).Take(75);
                    var otherLogs = allLogEvents.Where(e => !e.IsError && !e.IsWarning).Take(75);
                    
                    var selectedLogs = errorWarningLogs.Concat(otherLogs)
                        .OrderByDescending(e => e.Timestamp)
                        .Take(150) // Increased from 100
                        .Select(e => $"{DateTimeOffset.FromUnixTimeMilliseconds(e.Timestamp):yyyy-MM-dd HH:mm:ss UTC} - {e.Message}")
                        .ToList();
                    
                    _logger.LogLine($"Retrieved {allEvents.Count} total log events across {pageCount} pages (using {selectedLogs.Count} selected logs)");
                    return string.Join("\n", selectedLogs);
                }
                
                return $"No log events found for alarm time {alarmTime:yyyy-MM-dd HH:mm:ss UTC}";
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
                
                // First, try to get the alarm trigger thumbnail
                await TryRetrieveThumbnail(trigger, alarm.timestamp, screenshots);
                
                var screenshotTypes = new[] { "login-screenshot", "pageload-screenshot", "afterarchivebuttonclick-screenshot", "signout-screenshot" };
                
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
        /// Attempts to retrieve the alarm trigger thumbnail from S3.
        /// </summary>
        private async Task TryRetrieveThumbnail(Trigger trigger, long timestamp, List<(string name, byte[] data)> screenshots)
        {
            try
            {
                // Convert to Eastern Time (UTC-5) to match file organization
                DateTime dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime.AddHours(-5);
                string dateFolder = $"{dt.Year}-{dt.Month:D2}-{dt.Day:D2}";
                var thumbnailKey = $"{dateFolder}/{trigger.eventId}_{trigger.device}_{timestamp}.jpg";
                
                _logger.LogLine($"Attempting to retrieve alarm trigger thumbnail: {thumbnailKey}");
                
                var thumbnailData = await _s3StorageService.GetFileAsync(thumbnailKey);
                if (thumbnailData != null && thumbnailData.Length > 0)
                {
                    screenshots.Add(("alarm-trigger-thumbnail.jpg", thumbnailData));
                    _logger.LogLine($"Retrieved alarm trigger thumbnail: {thumbnailData.Length} bytes");
                }
                else
                {
                    _logger.LogLine("No alarm trigger thumbnail found in S3");
                }
            }
            catch (Exception ex)
            {
                _logger.LogLine($"Could not retrieve alarm trigger thumbnail: {ex.Message}");
            }
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
        [SuppressMessage("SonarQube", "S1541:Methods should not be too complex", Justification = "Email generation requires multiple conditional checks and formatting logic")]
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
            var eventKeyType = GetEventKeyType(eventKey);
            var deviceMacAddress = trigger?.device ?? "Unknown";
            var triggerKey = trigger?.key ?? "Unknown";
            AddEventInformation(sb, eventKey, deviceName, eventTime, eventPath, eventKeyType, deviceMacAddress, triggerKey);
            
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

            sb.AppendLine("<h1>🚨 Unifi Protect Video Backup Failure</h1>");
            sb.AppendLine($"<p class='failure'>A video download has failed and has been sent to the Dead Letter Queue for manual intervention.</p>");
        }

        /// <summary>
        /// Adds failure details section to the email body.
        /// </summary>
        private static void AddFailureInformation(StringBuilder sb, string failureReason, string messageId, string retryAttempt)
        {
            var environment = AppConfiguration.DeployedEnv ?? "Unknown";
            var buildSha = AppConfiguration.BuildSha ?? "Unknown";
            var buildTimestamp = AppConfiguration.BuildTimestamp ?? "Unknown";
            
            sb.AppendLine("<h2>📋 Failure Details</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><th>Environment</th><td>{environment}</td></tr>");
            sb.AppendLine($"<tr><th>Failure Reason</th><td class='failure'>{failureReason}</td></tr>");
            sb.AppendLine($"<tr><th>SQS Message ID</th><td>{messageId}</td></tr>");
            sb.AppendLine($"<tr><th>Retry Attempt Time</th><td>{retryAttempt}</td></tr>");
            sb.AppendLine("</table>");
            
            sb.AppendLine("<h2>🔧 Build Information</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><th>Build SHA</th><td>{buildSha}</td></tr>");
            sb.AppendLine($"<tr><th>Build Timestamp</th><td>{buildTimestamp}</td></tr>");
            sb.AppendLine($"<tr><th>Lambda Function</th><td>{AppConfiguration.FunctionName ?? "Unknown"}</td></tr>");
            sb.AppendLine("</table>");
        }

        /// <summary>
        /// Adds event information section to the email body.
        /// </summary>
        private static void AddEventInformation(StringBuilder sb, string eventKey, string deviceName, string eventTime, string eventPath, string eventKeyType, string deviceMacAddress, string triggerKey)
        {
            sb.AppendLine("<h2>🏠 Event Information</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<tr><th>Event ID</th><td>{eventKey}</td></tr>");
            sb.AppendLine($"<tr><th>Event Type</th><td>{eventKeyType}</td></tr>");
            sb.AppendLine($"<tr><th>Trigger Key</th><td>{triggerKey}</td></tr>");
            sb.AppendLine($"<tr><th>Device Name</th><td>{deviceName}</td></tr>");
            sb.AppendLine($"<tr><th>Device MAC</th><td>{deviceMacAddress}</td></tr>");
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
            sb.AppendLine("<h2>📸 Process Screenshots</h2>");
            sb.AppendLine("<div class='info'>");
            sb.AppendLine("<p>CloudWatch logs and alarm event data are included in the sections above. Screenshots from the automation process are embedded below:</p>");
            sb.AppendLine("</div>");
            
            if (screenshots.Count > 0)
            {
                sb.AppendLine("<h3>📷 Automation Screenshots</h3>");
                sb.AppendLine("<p>The following screenshots document the automation process at the time of failure:</p>");
                
                foreach (var screenshot in screenshots)
                {
                    var description = GetScreenshotDescription(screenshot.name);
                    var base64Image = Convert.ToBase64String(screenshot.data);
                    
                    sb.AppendLine("<div style='margin: 20px 0; padding: 15px; border: 1px solid #ddd; border-radius: 5px;'>");
                    sb.AppendLine($"<h4 style='margin-top: 0; color: #1976d2;'>{description}</h4>");
                    sb.AppendLine($"<p><strong>File:</strong> {screenshot.name}</p>");
                    sb.AppendLine($"<p><strong>Size:</strong> {screenshot.data.Length:N0} bytes</p>");
                    sb.AppendLine($"<div style='text-align: center; margin: 15px 0;'>");
                    sb.AppendLine($"<img src='data:image/png;base64,{base64Image}' alt='{description}' style='max-width: 100%; max-height: 600px; border: 1px solid #ccc; border-radius: 4px;' />");
                    sb.AppendLine("</div>");
                    sb.AppendLine("</div>");
                }
            }
            else
            {
                sb.AppendLine("<div class='info'>");
                sb.AppendLine("<p><strong>Screenshots:</strong> No screenshots available for this event</p>");
                sb.AppendLine("</div>");
            }
        }

        /// <summary>
        /// Gets a descriptive title for each screenshot type.
        /// </summary>
        private static string GetScreenshotDescription(string filename)
        {
            return filename.ToLower() switch
            {
                var name when name.Contains("alarm-trigger-thumbnail") => "🚨 Alarm Trigger Thumbnail",
                var name when name.Contains("login-screenshot") => "🔐 Login Page Screenshot",
                var name when name.Contains("pageload-screenshot") => "📄 Page Load Screenshot", 
                var name when name.Contains("afterarchivebuttonclick-screenshot") => "🖱️ After Archive Button Click Screenshot",
                var name when name.Contains("signout-screenshot") => "🚪 Signout Page Screenshot",
                _ => "📸 Process Screenshot"
            };
        }

        /// <summary>
        /// Extracts the event key type from the event ID.
        /// </summary>
        private static string GetEventKeyType(string eventKey)
        {
            if (string.IsNullOrEmpty(eventKey))
                return "Unknown";

            return eventKey switch
            {
                var key when key.StartsWith("alm_") => "Alarm Event",
                var key when key.StartsWith("evt_") => "Motion Event", 
                var key when key.StartsWith("test_") => "Test Event",
                var key when key.StartsWith("rec_") => "Recording Event",
                var key when key.StartsWith("live_") => "Live Stream Event",
                _ => ExtractPrefixFromEventKey(eventKey)
            };
        }

        /// <summary>
        /// Extracts and formats the prefix from an event key for unknown types.
        /// </summary>
        private static string ExtractPrefixFromEventKey(string eventKey)
        {
            var underscoreIndex = eventKey.IndexOf('_');
            if (underscoreIndex > 0)
            {
                var prefix = eventKey.Substring(0, underscoreIndex);
                return $"{char.ToUpper(prefix[0])}{prefix.Substring(1)} Event";
            }
            return "Custom Event";
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
        private async Task<bool> SendRawEmailWithAttachments(string supportEmail, string subject, string htmlBody)
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

                // Note: Logs and JSON data are now embedded in the HTML body above
                // Additional attachments can be added here in the future if needed
                
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
