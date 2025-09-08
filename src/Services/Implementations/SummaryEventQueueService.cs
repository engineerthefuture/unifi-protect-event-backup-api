/************************
 * Unifi Webhook Event Receiver
 * SummaryEventQueueService.cs
 * 
 * Service for queuing summary events to SQS.
 * Author: Brent Foster
 * Created: 09-08-2025
 ***********************/

using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver.Models;
using UnifiWebhookEventReceiver.Configuration;
using Amazon.Lambda.Core;

namespace UnifiWebhookEventReceiver.Services.Implementations
{
    public class SummaryEventQueueService : ISummaryEventQueueService
    {
        private readonly AmazonSQSClient _sqsClient;
        private readonly ILambdaLogger _logger;

        public SummaryEventQueueService(AmazonSQSClient sqsClient, ILambdaLogger logger)
        {
            _sqsClient = sqsClient;
            _logger = logger;
        }

        public async Task<string> SendSummaryEventAsync(SummaryEvent summaryEvent)
        {
            if (string.IsNullOrEmpty(AppConfiguration.SummaryEventQueueUrl))
            {
                _logger.LogLine("SummaryEventQueueUrl environment variable is not configured");
                throw new System.InvalidOperationException("SummaryEventQueueUrl environment variable is not configured");
            }

            string messageBody = JsonConvert.SerializeObject(summaryEvent);

            var sendMessageRequest = new SendMessageRequest
            {
                QueueUrl = AppConfiguration.SummaryEventQueueUrl,
                MessageBody = messageBody
            };

            var result = await _sqsClient.SendMessageAsync(sendMessageRequest);
            _logger.LogLine($"Successfully queued summary event. MessageId: {result.MessageId}");
            return result.MessageId;
        }
    }
}
