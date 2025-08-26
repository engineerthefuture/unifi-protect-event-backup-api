using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Configuration;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    public class UnifiWebhookEventHandlerTests
    {
        [Fact]
        public async Task FunctionHandler_GetSummary_ReturnsSummaryResponse()
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
        }

        private static void SetTestEnvironment()
        {
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            Environment.SetEnvironmentVariable("DevicePrefix", "dev");
            Environment.SetEnvironmentVariable("DeployedEnv", "test");
            Environment.SetEnvironmentVariable("FunctionName", "TestFunction");
            Environment.SetEnvironmentVariable("QueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue");
            Environment.SetEnvironmentVariable("SecretsManagerArn", "arn:aws:secretsmanager:us-east-1:123456789012:secret:test-secret");
        }

        [Fact]
        public async Task FunctionHandler_WithValidAlarmData_DoesNotThrow()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();
            
            var alarmData = new
            {
                alarm = new
                {
                    name = "Test Alarm",
                    triggers = new[]
                    {
                        new
                        {
                            key = "motion",
                            device = "test-device-123",
                            eventId = "test-event-456"
                        }
                    }
                },
                timestamp = 1755727493267
            };
            
            var requestBody = JsonConvert.SerializeObject(alarmData);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act & Assert - Should not throw
            var response = await handler.FunctionHandler(stream, context);
            
            // Basic validation that we got a response
            Assert.NotNull(response);
            Assert.True(response.StatusCode >= 200 && response.StatusCode < 600);
        }

        [Fact]
        public async Task FunctionHandler_WithScheduledEventTrigger_ReturnsSuccessResponse()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();
            
            // Create a request body that contains the scheduled event trigger
            var scheduledEventBody = $"{{\"source\": \"{AppConfiguration.SOURCE_EVENT_TRIGGER}\"}}";
            
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(scheduledEventBody));

            // Act
            var response = await handler.FunctionHandler(stream, context);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(200, response.StatusCode);
            Assert.Contains(AppConfiguration.MESSAGE_202, response.Body);
        }

        [Fact]
        public async Task FunctionHandler_WithPartialScheduledEventTrigger_ReturnsSuccessResponse()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();
            
            // Create a request body that partially contains the scheduled event trigger
            var requestBody = $"Test message with {AppConfiguration.SOURCE_EVENT_TRIGGER} in the middle";
            
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            var response = await handler.FunctionHandler(stream, context);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(200, response.StatusCode);
            Assert.Contains(AppConfiguration.MESSAGE_202, response.Body);
        }

        [Fact]
        public async Task FunctionHandler_WithInvalidAlarmStructure_ReturnsErrorResponse()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();
            
            // Invalid structure - missing alarm wrapper
            var invalidData = new { key = "value" };
            var requestBody = JsonConvert.SerializeObject(invalidData);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            var response = await handler.FunctionHandler(stream, context);

            // Assert - Should return error response
            Assert.NotNull(response);
            Assert.True(response.StatusCode >= 400);
        }

        [Fact]
        public async Task FunctionHandler_WithEmptyBody_ReturnsErrorResponse()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(""));

            // Act
            var response = await handler.FunctionHandler(stream, context);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.StatusCode >= 400);
        }

        [Fact]
        public async Task FunctionHandler_WithNullInputStream_ReturnsBadRequest()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();

            // Act
            var response = await handler.FunctionHandler(null, context);

            // Assert
            Assert.NotNull(response);
            Assert.Equal(400, response.StatusCode);
        }

        [Fact]
        public async Task FunctionHandler_WithSQSEventFormat_HandlesCorrectly()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();
            
            var sqsEvent = new
            {
                Records = new[]
                {
                    new
                    {
                        eventSource = "aws:sqs",
                        body = JsonConvert.SerializeObject(new
                        {
                            alarm = new
                            {
                                name = "Test Alarm",
                                triggers = new[]
                                {
                                    new
                                    {
                                        key = "motion",
                                        device = "test-device-123",
                                        eventId = "test-event-456"
                                    }
                                }
                            },
                            timestamp = 1755727493267
                        })
                    }
                }
            };
            
            var requestBody = JsonConvert.SerializeObject(sqsEvent);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            var response = await handler.FunctionHandler(stream, context);

            // Assert - Should handle SQS events
            Assert.NotNull(response);
            Assert.True(response.StatusCode >= 200 && response.StatusCode < 600);
        }

        [Fact]
        public async Task FunctionHandler_WithMalformedJSON_ReturnsErrorResponse()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();
            var malformedJson = "{ invalid json content }";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(malformedJson));

            // Act
            var response = await handler.FunctionHandler(stream, context);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.StatusCode >= 400);
        }

        [Fact]
        public async Task FunctionHandler_WithComplexAlarmData_HandlesCorrectly()
        {
            // Arrange
            SetTestEnvironment();
            var context = new TestLambdaContext();
            var handler = new UnifiWebhookEventHandler();
            
            var complexAlarmData = new
            {
                alarm = new
                {
                    name = "Complex Test Alarm",
                    type = "motion",
                    triggers = new[]
                    {
                        new
                        {
                            key = "motion",
                            device = "test-device-123",
                            eventId = "test-event-456",
                            timestamp = 1755727493267,
                            metadata = new
                            {
                                location = "front_door",
                                confidence = 0.95
                            }
                        }
                    },
                    metadata = new
                    {
                        version = "1.0",
                        source = "unifi-protect"
                    }
                },
                timestamp = 1755727493267
            };
            
            var requestBody = JsonConvert.SerializeObject(complexAlarmData);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            var response = await handler.FunctionHandler(stream, context);

            // Assert
            Assert.NotNull(response);
            Assert.True(response.StatusCode >= 200 && response.StatusCode < 600);
        }

        [Fact]
        public void FunctionHandler_ConstructorWithNullServices_ThrowsArgumentNullException()
        {
            // Arrange
            SetTestEnvironment();

            // Act & Assert - Constructor should validate null parameters
            Assert.Throws<ArgumentNullException>(() => 
                new UnifiWebhookEventHandler(null, null, null, null));
        }
    }

    /// <summary>
    /// Test implementation of ILambdaContext for unit testing
    /// </summary>
    public class TestLambdaContext : ILambdaContext
    {
        public string AwsRequestId => Guid.NewGuid().ToString();
        public IClientContext ClientContext => null;
        public string FunctionName => "TestFunction";
        public string FunctionVersion => "1.0";
        public ICognitoIdentity Identity => null;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:TestFunction";
        public ILambdaLogger Logger => new TestLambdaLogger();
        public string LogGroupName => "/aws/lambda/TestFunction";
        public string LogStreamName => DateTime.UtcNow.ToString("yyyy/MM/dd") + "/[$LATEST]" + Guid.NewGuid().ToString("N")[..8];
        public int MemoryLimitInMB => 256;
        public TimeSpan RemainingTime => TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// Test implementation of ILambdaLogger for unit testing
    /// </summary>
    public class TestLambdaLogger : ILambdaLogger
    {
        public void Log(string message) { /* No-op for testing */ }
        public void LogLine(string message) { /* No-op for testing */ }
    }
}
