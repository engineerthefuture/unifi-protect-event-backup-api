/************************
 * Unifi Webhook Event Receiver
 * CredentialsService.cs
 * 
 * Service for managing Unifi Protect credentials from AWS Secrets Manager.
 * Provides caching and retrieval of authentication credentials.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using System.Diagnostics.CodeAnalysis;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    /// <summary>
    /// Service for managing Unifi Protect credentials from AWS Secrets Manager.
    /// </summary>
    public class CredentialsService : ICredentialsService
    {
        private readonly AmazonSecretsManagerClient _secretsClient;
        private readonly ILambdaLogger _logger;
        private UnifiCredentials? _cachedCredentials;

        /// <summary>
        /// Initializes a new instance of the CredentialsService.
        /// </summary>
        /// <param name="secretsClient">AWS Secrets Manager client</param>
        /// <param name="logger">Lambda logger instance</param>
        public CredentialsService(AmazonSecretsManagerClient secretsClient, ILambdaLogger logger)
        {
            _secretsClient = secretsClient ?? throw new ArgumentNullException(nameof(secretsClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves Unifi Protect credentials from AWS Secrets Manager.
        /// </summary>
        /// <returns>UnifiCredentials object containing hostname, username, and password</returns>
        [ExcludeFromCodeCoverage] // Requires AWS Secrets Manager connectivity
        public async Task<UnifiCredentials> GetUnifiCredentialsAsync()
        {
            if (_cachedCredentials != null)
            {
                return _cachedCredentials;
            }

            if (string.IsNullOrEmpty(AppConfiguration.UnifiCredentialsSecretArn))
            {
                throw new InvalidOperationException("UnifiCredentialsSecretArn environment variable is not set");
            }

            try
            {
                var request = new GetSecretValueRequest
                {
                    SecretId = AppConfiguration.UnifiCredentialsSecretArn
                };

                var response = await _secretsClient.GetSecretValueAsync(request);
                var credentials = JsonConvert.DeserializeObject<UnifiCredentials>(response.SecretString);

                if (credentials == null)
                {
                    throw new InvalidOperationException("Failed to deserialize Unifi credentials from Secrets Manager");
                }

                // Validate required fields
                if (string.IsNullOrEmpty(credentials.hostname))
                {
                    throw new InvalidOperationException("Hostname is required in Unifi credentials");
                }

                if (string.IsNullOrEmpty(credentials.username))
                {
                    throw new InvalidOperationException("Username is required in Unifi credentials");
                }

                if (string.IsNullOrEmpty(credentials.password))
                {
                    throw new InvalidOperationException("Password is required in Unifi credentials");
                }

                _cachedCredentials = credentials;
                return credentials;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving Unifi credentials from Secrets Manager: {ex.Message}");
                throw;
            }
        }
    }
}
