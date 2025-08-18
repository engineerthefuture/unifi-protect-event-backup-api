/************************
 * Unifi Webhook Event Receiver
 * ICredentialsService.cs
 * 
 * Interface for credentials management operations.
 * Defines the contract for retrieving and managing Unifi Protect credentials.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

namespace UnifiWebhookEventReceiver.Services
{
    /// <summary>
    /// Interface for managing Unifi Protect credentials.
    /// </summary>
    public interface ICredentialsService
    {
        /// <summary>
        /// Retrieves Unifi Protect credentials from AWS Secrets Manager.
        /// </summary>
        /// <returns>UnifiCredentials object containing hostname, username, and password</returns>
        Task<UnifiCredentials> GetUnifiCredentialsAsync();
    }
}
