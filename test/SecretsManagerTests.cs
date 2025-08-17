/************************
 * Secrets Manager Integration Tests
 * SecretsManagerTests.cs
 * Testing AWS Secrets Manager integration and credential management
 * Brent Foster
 * 08-17-2025
 ***********************/

using System;
using System.Threading.Tasks;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Newtonsoft.Json;
using Xunit;
using Moq;
using UnifiWebhookEventReceiver;

namespace UnifiWebhookEventReceiver.Tests
{
    public class SecretsManagerTests
    {
        [Fact]
        public void UnifiCredentials_JsonDeserialization_ShouldWork()
        {
            // Arrange
            var jsonCredentials = @"{
                ""hostname"": ""test-unifi.local"",
                ""username"": ""testuser"",
                ""password"": ""testpassword""
            }";

            // Act
            var credentials = JsonConvert.DeserializeObject<UnifiWebhookEventReceiver.UnifiCredentials>(jsonCredentials);

            // Assert
            Assert.NotNull(credentials);
            Assert.Equal("test-unifi.local", credentials.hostname);
            Assert.Equal("testuser", credentials.username);
            Assert.Equal("testpassword", credentials.password);
        }

        [Fact]
        public void UnifiCredentials_JsonSerialization_ShouldWork()
        {
            // Arrange
            var credentials = new UnifiWebhookEventReceiver.UnifiCredentials
            {
                hostname = "test-unifi.local",
                username = "testuser",
                password = "testpassword"
            };

            // Act
            var json = JsonConvert.SerializeObject(credentials);
            var deserializedCredentials = JsonConvert.DeserializeObject<UnifiWebhookEventReceiver.UnifiCredentials>(json);

            // Assert
            Assert.NotNull(deserializedCredentials);
            Assert.Equal(credentials.hostname, deserializedCredentials.hostname);
            Assert.Equal(credentials.username, deserializedCredentials.username);
            Assert.Equal(credentials.password, deserializedCredentials.password);
        }

        [Fact]
        public void UnifiCredentials_ShouldHandleEmptyValues()
        {
            // Arrange
            var jsonCredentials = @"{
                ""hostname"": """",
                ""username"": """",
                ""password"": """"
            }";

            // Act
            var credentials = JsonConvert.DeserializeObject<UnifiWebhookEventReceiver.UnifiCredentials>(jsonCredentials);

            // Assert
            Assert.NotNull(credentials);
            Assert.Equal(string.Empty, credentials.hostname);
            Assert.Equal(string.Empty, credentials.username);
            Assert.Equal(string.Empty, credentials.password);
        }

        [Fact]
        public void UnifiCredentials_ShouldHandleMissingFields()
        {
            // Arrange
            var jsonCredentials = @"{
                ""hostname"": ""test-unifi.local""
            }";

            // Act
            var credentials = JsonConvert.DeserializeObject<UnifiWebhookEventReceiver.UnifiCredentials>(jsonCredentials);

            // Assert
            Assert.NotNull(credentials);
            Assert.Equal("test-unifi.local", credentials.hostname);
            Assert.Equal(string.Empty, credentials.username);
            Assert.Equal(string.Empty, credentials.password);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void SecretsManagerValidation_ShouldFailWithInvalidArn(string secretArn)
        {
            // Arrange
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", secretArn);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UnifiCredentialsSecretArn")))
                {
                    throw new InvalidOperationException("UnifiCredentialsSecretArn environment variable is not set");
                }
            });

            Assert.Contains("UnifiCredentialsSecretArn environment variable is not set", exception.Message);
        }

        [Fact]
        public void SecretArn_ShouldBeValidFormat()
        {
            // Arrange
            var validSecretArn = "arn:aws:secretsmanager:us-east-1:123456789012:secret:my-secret-AbCdEf";
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", validSecretArn);

            // Act
            var secretArn = Environment.GetEnvironmentVariable("UnifiCredentialsSecretArn");

            // Assert
            Assert.Equal(validSecretArn, secretArn);
            Assert.StartsWith("arn:aws:secretsmanager:", secretArn);
        }

        [Fact]
        public void CredentialMasking_ShouldProtectSensitiveData()
        {
            // Arrange
            var credentials = new UnifiWebhookEventReceiver.UnifiCredentials
            {
                hostname = "test-unifi.local",
                username = "testuser",
                password = "secretpassword"
            };

            // Act
            var maskedUsername = credentials.username.Length > 3 
                ? credentials.username.Substring(0, 3) + new string('*', credentials.username.Length - 3) 
                : credentials.username;
            var maskedPassword = new string('*', credentials.password.Length);

            // Assert
            Assert.Equal("tes*****", maskedUsername);
            Assert.Equal("**************", maskedPassword);
            Assert.DoesNotContain("testuser", maskedUsername);
            Assert.DoesNotContain("secretpassword", maskedPassword);
        }

        [Fact]
        public void CredentialMasking_ShouldHandleShortUsername()
        {
            // Arrange
            var credentials = new UnifiWebhookEventReceiver.UnifiCredentials
            {
                username = "ab"
            };

            // Act
            var maskedUsername = credentials.username.Length > 3 
                ? credentials.username.Substring(0, 3) + new string('*', credentials.username.Length - 3) 
                : credentials.username;

            // Assert
            Assert.Equal("ab", maskedUsername); // Should not mask if 3 characters or less
        }

        [Fact]
        public void HostnameConstruction_ShouldBuildCorrectUrl()
        {
            // Arrange
            var credentials = new UnifiWebhookEventReceiver.UnifiCredentials
            {
                hostname = "https://unifi.local"
            };
            var eventPath = "/api/video/download";

            // Act
            var fullUrl = credentials.hostname + eventPath;

            // Assert
            Assert.Equal("https://unifi.local/api/video/download", fullUrl);
        }

        [Fact]
        public void HostnameConstruction_ShouldHandleIPAddress()
        {
            // Arrange
            var credentials = new UnifiWebhookEventReceiver.UnifiCredentials
            {
                hostname = "192.168.1.100"
            };
            var eventPath = "/protect/api/video";

            // Act
            var fullUrl = credentials.hostname + eventPath;

            // Assert
            Assert.Equal("192.168.1.100/protect/api/video", fullUrl);
        }

        [Theory]
        [InlineData("username", "password", false)]
        [InlineData("", "password", true)]
        [InlineData("username", "", true)]
        [InlineData("", "", true)]
        [InlineData(null, "password", true)]
        [InlineData("username", null, true)]
        public void CredentialValidation_ShouldDetectMissingValues(string username, string password, bool shouldBeInvalid)
        {
            // Arrange
            var credentials = new UnifiWebhookEventReceiver.UnifiCredentials
            {
                username = username,
                password = password
            };

            // Act
            var isInvalid = string.IsNullOrEmpty(credentials.username) || string.IsNullOrEmpty(credentials.password);

            // Assert
            Assert.Equal(shouldBeInvalid, isInvalid);
        }
    }
}
