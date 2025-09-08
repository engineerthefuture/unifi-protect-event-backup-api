/************************
 * Unifi Webhook Event Receiver
 * LoggerExtensions.cs
 * 
 * Extension methods for ILambdaLogger to provide coverage-excluded logging.
 * 
 * Author: Brent Foster
 * Created: 09-07-2025
 ***********************/

using System.Diagnostics.CodeAnalysis;
using Amazon.Lambda.Core;
using UnifiWebhookEventReceiver.Attributes;

namespace UnifiWebhookEventReceiver.Extensions
{
    /// <summary>
    /// Extension methods for ILambdaLogger that exclude logging calls from code coverage.
    /// </summary>
    [ExcludeFromCoverage("Logging extensions should not be included in coverage calculations")]
    public static class LoggerExtensions
    {
        /// <summary>
        /// Logs a line of text, excluded from code coverage.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="message">The message to log.</param>
        [ExcludeFromCodeCoverage]
        [ExcludeFromCoverage("Logging should not be included in coverage calculations")]
        public static void LogLineExcluded(this ILambdaLogger logger, string message)
        {
            logger.LogLine(message);
        }
    }
}
