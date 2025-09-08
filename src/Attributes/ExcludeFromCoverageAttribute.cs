/************************
 * Unifi Webhook Event Receiver
 * ExcludeFromCoverageAttribute.cs
 * 
 * Custom attribute to mark code sections that should be excluded from code coverage.
 * Primarily used for logging statements and other non-testable code.
 * 
 * Author: Brent Foster
 * Created: 09-07-2025
 ***********************/

using System;
using System.Diagnostics.CodeAnalysis;

namespace UnifiWebhookEventReceiver.Attributes
{
    /// <summary>
    /// Marks code that should be excluded from code coverage calculations.
    /// This is typically used for logging statements, error handling, and other
    /// code that doesn't contribute to business logic coverage.
    /// </summary>
    [ExcludeFromCodeCoverage]
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Constructor)]
    public sealed class ExcludeFromCoverageAttribute : Attribute
    {
        /// <summary>
        /// Optional reason for excluding this code from coverage.
        /// </summary>
        public string? Reason { get; set; }

        /// <summary>
        /// Initializes a new instance of the ExcludeFromCoverageAttribute.
        /// </summary>
        public ExcludeFromCoverageAttribute()
        {
        }

        /// <summary>
        /// Initializes a new instance of the ExcludeFromCoverageAttribute with a reason.
        /// </summary>
        /// <param name="reason">The reason for excluding this code from coverage.</param>
        public ExcludeFromCoverageAttribute(string reason)
        {
            Reason = reason;
        }
    }
}
