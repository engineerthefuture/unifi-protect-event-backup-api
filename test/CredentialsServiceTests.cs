using System;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Moq;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    public class CredentialsServiceTests
    {
        private readonly Mock<AmazonSecretsManagerClient> _mockSecretsClient;
        private readonly Mock<ILambdaLogger> _mockLogger;
        private readonly CredentialsService _credentialsService;

        public CredentialsServiceTests()
        {
            // Set required environment variables for testing
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", "arn:aws:secretsmanager:us-east-1:123456789012:secret:test-secret");
            
            // Create AWS client mock with region
            _mockSecretsClient = new Mock<AmazonSecretsManagerClient>(Amazon.RegionEndpoint.USEast1);
            _mockLogger = new Mock<ILambdaLogger>();
            _credentialsService = new CredentialsService(_mockSecretsClient.Object, _mockLogger.Object);
        }

        [Fact]
        public void Constructor_WithNullSecretsClient_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new CredentialsService(null, _mockLogger.Object));
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            var mockSecretsClient = new Mock<AmazonSecretsManagerClient>(Amazon.RegionEndpoint.USEast1);
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new CredentialsService(mockSecretsClient.Object, null));
        }

        [Fact]
        public async Task GetUnifiCredentialsAsync_WithMalformedJson_ThrowsInvalidOperationException()
        {
            // Arrange
            var secretResponse = new GetSecretValueResponse
            {
                SecretString = "invalid json"
            };

            _mockSecretsClient.Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ReturnsAsync(secretResponse);

            // Act & Assert
            await Assert.ThrowsAsync<JsonReaderException>(() => 
                _credentialsService.GetUnifiCredentialsAsync());
        }

        [Fact]
        public async Task GetUnifiCredentialsAsync_WithMissingHostname_ThrowsInvalidOperationException()
        {
            // Arrange
            var secretJson = "{\"username\":\"admin\",\"password\":\"secret123\"}";
            var secretResponse = new GetSecretValueResponse
            {
                SecretString = secretJson
            };

            _mockSecretsClient.Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ReturnsAsync(secretResponse);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _credentialsService.GetUnifiCredentialsAsync());
        }

        [Fact]
        public async Task GetUnifiCredentialsAsync_WithMissingUsername_ThrowsInvalidOperationException()
        {
            // Arrange
            var secretJson = "{\"hostname\":\"192.168.1.1\",\"password\":\"secret123\"}";
            var secretResponse = new GetSecretValueResponse
            {
                SecretString = secretJson
            };

            _mockSecretsClient.Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ReturnsAsync(secretResponse);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _credentialsService.GetUnifiCredentialsAsync());
        }

        [Fact]
        public async Task GetUnifiCredentialsAsync_WithMissingPassword_ThrowsInvalidOperationException()
        {
            // Arrange
            var secretJson = "{\"hostname\":\"192.168.1.1\",\"username\":\"admin\"}";
            var secretResponse = new GetSecretValueResponse
            {
                SecretString = secretJson
            };

            _mockSecretsClient.Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ReturnsAsync(secretResponse);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _credentialsService.GetUnifiCredentialsAsync());
        }

        [Fact]
        public async Task GetUnifiCredentialsAsync_WithSecretsManagerException_ThrowsResourceNotFoundException()
        {
            // Arrange
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", "arn:aws:secretsmanager:us-east-1:123456789012:secret:test-secret");
            
            _mockSecretsClient.Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ThrowsAsync(new ResourceNotFoundException("Secret not found"));

            // Act & Assert
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => 
                _credentialsService.GetUnifiCredentialsAsync());
        }

        [Fact]
        public async Task GetUnifiCredentialsAsync_WithEmptySecretString_ThrowsInvalidOperationException()
        {
            // Arrange
            var secretResponse = new GetSecretValueResponse
            {
                SecretString = " "
            };

            _mockSecretsClient.Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ReturnsAsync(secretResponse);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => 
                _credentialsService.GetUnifiCredentialsAsync());
        }

        [Fact]
        public async Task GetUnifiCredentialsAsync_CalledTwice_ReturnsCachedCredentials()
        {
            // Arrange
            var secretJson = "{\"hostname\":\"192.168.1.1\",\"username\":\"admin\",\"password\":\"secret123\"}";
            var secretResponse = new GetSecretValueResponse
            {
                SecretString = secretJson
            };

            _mockSecretsClient.Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ReturnsAsync(secretResponse);

            // Act
            var result1 = await _credentialsService.GetUnifiCredentialsAsync();
            var result2 = await _credentialsService.GetUnifiCredentialsAsync();

            // Assert
            Assert.Equal(result1.hostname, result2.hostname);
            Assert.Equal(result1.username, result2.username);
            Assert.Equal(result1.password, result2.password);
            
            // Verify the secrets manager was only called once due to caching
            _mockSecretsClient.Verify(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default), Times.Once);
        }
    }
}
