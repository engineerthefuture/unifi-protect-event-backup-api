/************************
 * Unif        {        [Fact]
        public async Task FunctionHandler_ReturnsInternalServerError_WhenJsonIsInvalid()
        {
            // Arrange
            SetEnv();
            var context = new StubContext();
            var json = "invalid json";
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(stream, context); SetEnv();
            var context = new StubContext();
            var json = "invalid json";
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(stream, context);

            // Assert
            Assert.Equal((int)HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("An internal server error has occured", response.Body);
        }t Receiver
 * UnifiWebhookEventReceiverTests.cs
 * Testing for receiving alarm event webhooks from Unifi Dream Machine
 * Brent Foster
 * 12-23-2024
 ***********************/

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Newtonsoft.Json;
using Xunit;
using UnifiWebhookEventReceiver;

namespace UnifiWebhookEventReceiver.Tests
{
    public class UnifiWebhookEventReceiverTests
    {
        private static void SetEnv()
        {
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            Environment.SetEnvironmentVariable("DevicePrefix", "dev");
            Environment.SetEnvironmentVariable("DeployedEnv", "test");
            Environment.SetEnvironmentVariable("FunctionName", "TestFunction");
        }

        [Fact]
        public async Task FunctionHandler_ReturnsInternalServerError_WhenInvalidJson()
        {
            // Arrange
            SetEnv();
            var context = new StubContext();
            var json = "invalid json";
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(stream, context);

            // Assert
            Assert.Equal((int)HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("An internal server error has occured", response.Body);
        }

        [Fact]
        public async Task FunctionHandler_ReturnsBadRequest_WhenInputStreamIsNull()
        {
            // Arrange
            SetEnv();
            var context = new StubContext();

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(null, context);

            // Assert
            Assert.Equal((int)HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("you must have a valid body object in your request", response.Body);
        }

        [Fact]
        public async Task FunctionHandler_ReturnsOptionsResponse_WhenOptionsMethod()
        {
            // Arrange
            SetEnv();
            var context = new StubContext();
            var request = new APIGatewayProxyRequest { Path = "alarmevent", HttpMethod = "OPTIONS" };
            var json = JsonConvert.SerializeObject(request);
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

            // Act
            var response = await UnifiWebhookEventReceiver.FunctionHandler(stream, context);

            // Assert
            Assert.Equal((int)HttpStatusCode.OK, response.StatusCode);
            Assert.Null(response.Body);
            Assert.Contains("Access-Control-Allow-Methods", response.Headers.Keys);
        }

        private sealed class TestLogger : ILambdaLogger { public void Log(string message) { } public void LogLine(string message) { } }
        private sealed class StubContext : ILambdaContext
        {
            public string AwsRequestId => "test";
            public IClientContext ClientContext => null;
            public string FunctionName => "TestFunction";
            public string FunctionVersion => "1";
            public ICognitoIdentity Identity => null;
            public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:TestFunction";
            public ILambdaLogger Logger { get; } = new TestLogger();
            public string LogGroupName => "/aws/lambda/TestFunction";
            public string LogStreamName => "2025/08/11/[$LATEST]test";
            public int MemoryLimitInMB => 128;
            public TimeSpan RemainingTime => TimeSpan.FromMinutes(5);
        }
    }
}


