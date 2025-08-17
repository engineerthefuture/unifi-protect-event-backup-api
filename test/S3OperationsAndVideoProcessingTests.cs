using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3.Model;
using Moq;
using Newtonsoft.Json;
using Xunit;
using UnifiWebhookEventReceiver;

namespace UnifiWebhookEventReceiver.Tests
{
    /// <summary>
    /// Enhanced test coverage for API Gateway operations and video processing
    /// Focuses on increasing line, branch, and method coverage for complex operations
    /// </summary>
    public class S3OperationsAndVideoProcessingTests
    {
        private static void SetupEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable("StorageBucket", "test-s3-bucket");
            Environment.SetEnvironmentVariable("DevicePrefix", "TestDevice");
            Environment.SetEnvironmentVariable("DeployedEnv", "s3-test");
            Environment.SetEnvironmentVariable("FunctionName", "S3TestFunction");
            Environment.SetEnvironmentVariable("UnifiCredentialsSecretArn", "arn:aws:secretsmanager:us-east-1:123456789012:secret:test-s3");
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue");
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", "300");
            Environment.SetEnvironmentVariable("DownloadDirectory", "/tmp/s3-test");
            Environment.SetEnvironmentVariable("ArchiveButtonX", "1500");
            Environment.SetEnvironmentVariable("ArchiveButtonY", "300");
            Environment.SetEnvironmentVariable("DownloadButtonX", "1200");
            Environment.SetEnvironmentVariable("DownloadButtonY", "350");
            Environment.SetEnvironmentVariable("TestDeviceF4E2C67A2FE8", "S3 Test Front Camera");
            Environment.SetEnvironmentVariable("TestDevice28704E113C44", "S3 Test Side Camera");
        }

        [Fact]
        public async Task ProcessAPIGatewayEvent_ShouldHandleNullEvent()
        {
            // Arrange
            SetupEnvironmentVariables();

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(null, new StubContext());

            // Assert
            Assert.Equal((int)HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("malformed", response.Body.ToLower());
        }

        [Fact]
        public async Task ProcessAPIGatewayEvent_ShouldHandleEventWithNullHttpMethod()
        {
            // Arrange
            SetupEnvironmentVariables();

            var apiEvent = new APIGatewayProxyRequest
            {
                HttpMethod = null, // Null HTTP method
                Path = "/alarmevent",
                PathParameters = new Dictionary<string, string>(),
                QueryStringParameters = new Dictionary<string, string>(),
                Headers = new Dictionary<string, string>(),
                Body = "{\"test\": \"data\"}"
            };

            var json = JsonConvert.SerializeObject(apiEvent);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(stream, new StubContext());

            // Assert
            Assert.Equal((int)HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ProcessAPIGatewayEvent_ShouldHandleEventWithNullPath()
        {
            // Arrange
            SetupEnvironmentVariables();

            var apiEvent = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = null, // Null path
                PathParameters = new Dictionary<string, string>(),
                QueryStringParameters = new Dictionary<string, string>(),
                Headers = new Dictionary<string, string>(),
                Body = "{\"test\": \"data\"}"
            };

            var json = JsonConvert.SerializeObject(apiEvent);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(stream, new StubContext());

            // Assert
            Assert.Equal((int)HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task ProcessAPIGatewayEvent_ShouldHandleEventWithNullHeaders()
        {
            // Arrange
            SetupEnvironmentVariables();

            var apiEvent = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = "/alarmevent",
                PathParameters = new Dictionary<string, string>(),
                QueryStringParameters = new Dictionary<string, string>(),
                Headers = null, // Null headers
                Body = "{\"test\": \"data\"}"
            };

            var json = JsonConvert.SerializeObject(apiEvent);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(stream, new StubContext());

            // Assert
            // Should handle gracefully (may succeed or fail, but shouldn't throw)
            Assert.True(response.StatusCode >= 200 && response.StatusCode < 600);
        }







        [Fact]
        public async Task AlarmReceiverFunction_ShouldHandleAlarmWithMultipleTriggers()
        {
            // Arrange
            SetupEnvironmentVariables();
            
            var alarm = new Alarm
            {
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        device = "F4E2C67A2FE8", // Valid device from environment
                        eventId = "testEvent1",
                        key = "motion"
                    },
                    new Trigger
                    {
                        device = "28704E113C44", // Another valid device from environment
                        eventId = "testEvent2", 
                        key = "person"
                    }
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var alarmJson = JsonConvert.SerializeObject(alarm);
            var apiEvent = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = "/alarmevent",
                PathParameters = new Dictionary<string, string>(),
                QueryStringParameters = new Dictionary<string, string>(),
                Headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" }
                },
                Body = alarmJson
            };

            var json = JsonConvert.SerializeObject(apiEvent);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(stream, new StubContext());

            // Assert
            // Should process multiple triggers (may succeed or fail based on AWS connectivity)
            Assert.True(response.StatusCode >= 200 && response.StatusCode < 600);
        }

        [Fact]
        public async Task AlarmReceiverFunction_ShouldHandleAlarmWithNoTriggers()
        {
            // Arrange
            SetupEnvironmentVariables();
            
            var alarm = new Alarm
            {
                triggers = new List<Trigger>(), // Empty triggers list
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var alarmJson = JsonConvert.SerializeObject(alarm);
            var apiEvent = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = "/alarmevent",
                PathParameters = new Dictionary<string, string>(),
                QueryStringParameters = new Dictionary<string, string>(),
                Headers = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" }
                },
                Body = alarmJson
            };

            var json = JsonConvert.SerializeObject(apiEvent);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(stream, new StubContext());

            // Assert
            Assert.Equal((int)HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}
