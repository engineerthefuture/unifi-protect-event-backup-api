/************************
 * Unifi Webhook Event Receiver
 * UnifiCredentials.cs
 * 
 * Data model for Unifi Protect credentials retrieved from AWS Secrets Manager.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

namespace UnifiWebhookEventReceiver
{
    /// <summary>
    /// Class to hold Unifi Protect credentials retrieved from Secrets Manager.
    /// </summary>
    public class UnifiCredentials
    {
        /// <summary>Hostname or IP address of the Unifi Protect system</summary>
        public string hostname { get; set; } = string.Empty;

        /// <summary>Username for Unifi Protect authentication</summary>
        public string username { get; set; } = string.Empty;

        /// <summary>Password for Unifi Protect authentication</summary>
        public string password { get; set; } = string.Empty;

        /// <summary>API key for Unifi Protect metadata access</summary>
        public string apikey { get; set; } = string.Empty;
    }
}
