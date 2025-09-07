using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    public class SummaryEndpointEnhancedTests
    {
        /// <summary>
        /// Helper method to set environment variables for testing
        /// </summary>
        private static void SetTestEnvironment()
        {
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            Environment.SetEnvironmentVariable("FunctionName", "test-function");
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", "arn:aws:secretsmanager:us-west-2:123456789012:secret:test-secret");
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", "https://sqs.us-west-2.amazonaws.com/123456789012/test-queue");
            Environment.SetEnvironmentVariable("AlarmProcessingDlqUrl", "https://sqs.us-west-2.amazonaws.com/123456789012/test-dlq");
            Environment.SetEnvironmentVariable("SummaryEventQueueUrl", "https://sqs.us-west-2.amazonaws.com/123456789012/test-summary-queue");
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", "120");
            Environment.SetEnvironmentVariable("SupportEmail", "test@example.com");
            Environment.SetEnvironmentVariable("EnvPrefix", "test");
        }

        [Fact]
        public async Task GetSummaryAsync_NoSummaryFiles_ReturnsEmptyResponse()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();
            var apiRequest = new Amazon.Lambda.APIGatewayEvents.APIGatewayProxyRequest
            {
                HttpMethod = "GET",
                Path = "/summary",
                Headers = new System.Collections.Generic.Dictionary<string, string> { { "X-API-Key", "test-key" } }
            };
            var requestBody = JsonConvert.SerializeObject(apiRequest);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            var response = await handler.FunctionHandler(stream, context);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(200, response.StatusCode);
            Assert.NotNull(response.Body);
            
            var body = Newtonsoft.Json.Linq.JObject.Parse(response.Body);
            Assert.True(body["cameras"] != null);
            Assert.True(body["totalCount"] != null);
            Assert.True(body["summaryMessage"] != null);
            
            // Verify it includes summary date information
            Assert.True(body["summaryDate"] != null);
            
            // Since no summary files exist in test environment, should be empty or fallback message
            var summaryMessage = body["summaryMessage"]?.ToString();
            Assert.Contains("0 total events", summaryMessage);
        }
    }
}
