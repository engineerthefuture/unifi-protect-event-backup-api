using UnifiWebhookEventReceiver;

namespace UnifiWebhookEventReceiver.Services
{
    /// <summary>
    /// Interface for email notification operations.
    /// </summary>
    public interface IEmailService
    {
        /// <summary>
        /// Sends a failure notification email with attachments for a failed DLQ message.
        /// </summary>
        /// <param name="alarm">The failed alarm event</param>
        /// <param name="failureReason">The reason for the failure</param>
        /// <param name="messageId">The SQS message ID</param>
        /// <param name="retryAttempt">The retry attempt timestamp</param>
        /// <returns>True if email was sent successfully, false otherwise</returns>
        Task<bool> SendFailureNotificationAsync(Alarm alarm, string failureReason, string messageId, string retryAttempt);
    }
}
