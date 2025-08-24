/************************
 * Unifi Webhook Event Receiver
 * ServiceFactory.cs
 * 
 * Factory class for creating and configuring service dependencies.
 * Handles the dependency injection setup and resolves circular dependencies.
 * 
 * Author: Brent Foster
 * Created: 08-18-2025
 ***********************/

using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SimpleEmail;
using Amazon.CloudWatchLogs;
using Amazon.SQS;
using UnifiWebhookEventReceiver.Configuration;
using UnifiWebhookEventReceiver.Services;
using UnifiWebhookEventReceiver.Services.Implementations;

namespace UnifiWebhookEventReceiver.Infrastructure
{
    /// <summary>
    /// Factory for creating and configuring service dependencies.
    /// </summary>
    public static class ServiceFactory
    {
        /// <summary>
        /// Creates a complete set of configured services for the application.
        /// </summary>
        /// <param name="logger">Lambda logger instance</param>
        /// <returns>Tuple containing all configured services</returns>
        public static (
            IRequestRouter RequestRouter,
            ISqsService SqsService,
            IAlarmProcessingService AlarmProcessingService,
            IS3StorageService S3StorageService,
            IUnifiProtectService UnifiProtectService,
            ICredentialsService CredentialsService,
            IEmailService EmailService,
            IResponseHelper ResponseHelper
        ) CreateServices(ILambdaLogger logger)
        {
            // Create AWS clients
            var s3Client = new AmazonS3Client(AppConfiguration.AwsRegion);
            var sqsClient = new AmazonSQSClient(AppConfiguration.AwsRegion);
            var secretsClient = new AmazonSecretsManagerClient(AppConfiguration.AwsRegion);
            var sesClient = new AmazonSimpleEmailServiceClient(AppConfiguration.AwsRegion);
            var cloudWatchLogsClient = new AmazonCloudWatchLogsClient(AppConfiguration.AwsRegion);

            // Create foundational services
            var responseHelper = new ResponseHelper();
            var credentialsService = new CredentialsService(secretsClient, logger);
            var s3StorageService = new S3StorageService(s3Client, responseHelper, logger);
            var emailService = new EmailService(sesClient, cloudWatchLogsClient, logger, s3StorageService);
            var unifiProtectService = new UnifiProtectService(logger, s3StorageService, credentialsService);

            // Create alarm processing service 
            var alarmProcessingService = new AlarmProcessingService(
                s3StorageService, 
                unifiProtectService, 
                credentialsService, 
                responseHelper, 
                logger);

            // Create SQS service
            var sqsService = new SqsService(sqsClient, alarmProcessingService, emailService, responseHelper, logger);

            // Create request router
            var requestRouter = new RequestRouter(sqsService, s3StorageService, unifiProtectService, responseHelper, logger);

            return (
                RequestRouter: requestRouter,
                SqsService: sqsService,
                AlarmProcessingService: alarmProcessingService,
                S3StorageService: s3StorageService,
                UnifiProtectService: unifiProtectService,
                CredentialsService: credentialsService,
                EmailService: emailService,
                ResponseHelper: responseHelper
            );
        }
    }
}