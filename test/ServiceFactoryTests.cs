/************************
 * ServiceFactory Tests
 * ServiceFactoryTests.cs
 * Testing dependency injection and service creation
 * Brent Foster
 * 08-20-2025
 ***********************/

using System;
using Amazon.Lambda.Core;
using Moq;
using Xunit;
using UnifiWebhookEventReceiver.Infrastructure;
using UnifiWebhookEventReceiver.Services;

namespace UnifiWebhookEventReceiverTests
{
    public class ServiceFactoryTests
    {
        private readonly Mock<ILambdaLogger> _mockLogger;

        public ServiceFactoryTests()
        {
            _mockLogger = new Mock<ILambdaLogger>();
            
            // Set required environment variables for testing
            Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", "arn:aws:secretsmanager:us-east-1:123456789012:secret:test-secret");
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue");
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", "120");
            Environment.SetEnvironmentVariable("FunctionName", "TestFunction");
        }

        [Fact]
        public void CreateServices_WithValidLogger_ReturnsAllServices()
        {
            // Act
            var services = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert
            Assert.NotNull(services.RequestRouter);
            Assert.NotNull(services.SqsService);
            Assert.NotNull(services.AlarmProcessingService);
            Assert.NotNull(services.S3StorageService);
            Assert.NotNull(services.UnifiProtectService);
            Assert.NotNull(services.CredentialsService);
            Assert.NotNull(services.ResponseHelper);
        }

        [Fact]
        public void CreateServices_RequestRouter_IsCorrectType()
        {
            // Act
            var services = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert
            Assert.IsType<UnifiWebhookEventReceiver.Services.Implementations.RequestRouter>(services.RequestRouter);
        }

        [Fact]
        public void CreateServices_SqsService_IsCorrectType()
        {
            // Act
            var services = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert
            Assert.IsType<UnifiWebhookEventReceiver.Services.Implementations.SqsService>(services.SqsService);
        }

        [Fact]
        public void CreateServices_AlarmProcessingService_IsCorrectType()
        {
            // Act
            var services = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert
            Assert.IsType<UnifiWebhookEventReceiver.Services.Implementations.AlarmProcessingService>(services.AlarmProcessingService);
        }

        [Fact]
        public void CreateServices_S3StorageService_IsCorrectType()
        {
            // Act
            var services = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert
            Assert.IsType<UnifiWebhookEventReceiver.Services.Implementations.S3StorageService>(services.S3StorageService);
        }

        [Fact]
        public void CreateServices_UnifiProtectService_IsCorrectType()
        {
            // Act
            var services = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert
            Assert.IsType<UnifiWebhookEventReceiver.Services.Implementations.UnifiProtectService>(services.UnifiProtectService);
        }

        [Fact]
        public void CreateServices_CredentialsService_IsCorrectType()
        {
            // Act
            var services = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert
            Assert.IsType<UnifiWebhookEventReceiver.Services.Implementations.CredentialsService>(services.CredentialsService);
        }

        [Fact]
        public void CreateServices_ResponseHelper_IsCorrectType()
        {
            // Act
            var services = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert
            Assert.IsType<UnifiWebhookEventReceiver.Services.Implementations.ResponseHelper>(services.ResponseHelper);
        }

        [Fact]
        public void CreateServices_ServicesDependenciesAreWiredCorrectly()
        {
            // Act
            var services = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert - Verify that all services are functioning and not throwing null reference exceptions
            Assert.NotNull(services.RequestRouter);
            Assert.NotNull(services.SqsService);
            Assert.NotNull(services.AlarmProcessingService);
            
            // These services should be properly wired with their dependencies
            Assert.IsAssignableFrom<IRequestRouter>(services.RequestRouter);
            Assert.IsAssignableFrom<ISqsService>(services.SqsService);
            Assert.IsAssignableFrom<IAlarmProcessingService>(services.AlarmProcessingService);
            Assert.IsAssignableFrom<IS3StorageService>(services.S3StorageService);
            Assert.IsAssignableFrom<IUnifiProtectService>(services.UnifiProtectService);
            Assert.IsAssignableFrom<ICredentialsService>(services.CredentialsService);
            Assert.IsAssignableFrom<IResponseHelper>(services.ResponseHelper);
        }

        [Fact]
        public void CreateServices_CalledMultipleTimes_ReturnsNewInstances()
        {
            // Act
            var services1 = ServiceFactory.CreateServices(_mockLogger.Object);
            var services2 = ServiceFactory.CreateServices(_mockLogger.Object);

            // Assert - Each call should return new instances
            Assert.NotSame(services1.RequestRouter, services2.RequestRouter);
            Assert.NotSame(services1.SqsService, services2.SqsService);
            Assert.NotSame(services1.AlarmProcessingService, services2.AlarmProcessingService);
            Assert.NotSame(services1.S3StorageService, services2.S3StorageService);
            Assert.NotSame(services1.UnifiProtectService, services2.UnifiProtectService);
            Assert.NotSame(services1.CredentialsService, services2.CredentialsService);
            Assert.NotSame(services1.ResponseHelper, services2.ResponseHelper);
        }
    }
}
