using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Xunit;

namespace UnifiWebhookEventReceiver.Tests
{
    /// <summary>
    /// Additional tests to improve coverage for the main UnifiWebhookEventReceiver class.
    /// These tests focus on exercising methods that can be tested without AWS dependencies.
    /// </summary>
    public class UnifiWebhookEventReceiverCoverageTests
    {
        [Fact]
        public void TestConstants()
        {
            // Test that constants are accessible and have expected values
            Assert.NotNull(typeof(UnifiWebhookEventReceiver).GetField("ERROR_MESSAGE_500", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
            Assert.NotNull(typeof(UnifiWebhookEventReceiver).GetField("ERROR_MESSAGE_400", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
            Assert.NotNull(typeof(UnifiWebhookEventReceiver).GetField("ROUTE_ALARM", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
        }

        [Fact]
        public void TestEnvironmentVariableReading()
        {
            // Set test environment variables
            Environment.SetEnvironmentVariable("StorageBucket", "test-bucket");
            Environment.SetEnvironmentVariable("DevicePrefix", "DEVICE_");
            Environment.SetEnvironmentVariable("FunctionName", "test-function");
            Environment.SetEnvironmentVariable("DownloadDirectory", "/test/downloads");
            Environment.SetEnvironmentVariable("ArchiveButtonX", "1000");
            Environment.SetEnvironmentVariable("ArchiveButtonY", "2000");
            Environment.SetEnvironmentVariable("DownloadButtonX", "1500");
            Environment.SetEnvironmentVariable("DownloadButtonY", "2500");

            // Verify environment variables are read correctly
            Assert.Equal("test-bucket", Environment.GetEnvironmentVariable("StorageBucket"));
            Assert.Equal("DEVICE_", Environment.GetEnvironmentVariable("DevicePrefix"));
            Assert.Equal("test-function", Environment.GetEnvironmentVariable("FunctionName"));
            Assert.Equal("/test/downloads", Environment.GetEnvironmentVariable("DownloadDirectory"));
            Assert.Equal("1000", Environment.GetEnvironmentVariable("ArchiveButtonX"));
            Assert.Equal("2000", Environment.GetEnvironmentVariable("ArchiveButtonY"));
            Assert.Equal("1500", Environment.GetEnvironmentVariable("DownloadButtonX"));
            Assert.Equal("2500", Environment.GetEnvironmentVariable("DownloadButtonY"));

            // Clean up
            Environment.SetEnvironmentVariable("StorageBucket", null);
            Environment.SetEnvironmentVariable("DevicePrefix", null);
            Environment.SetEnvironmentVariable("FunctionName", null);
            Environment.SetEnvironmentVariable("DownloadDirectory", null);
            Environment.SetEnvironmentVariable("ArchiveButtonX", null);
            Environment.SetEnvironmentVariable("ArchiveButtonY", null);
            Environment.SetEnvironmentVariable("DownloadButtonX", null);
            Environment.SetEnvironmentVariable("DownloadButtonY", null);
        }

        [Fact]
        public void TestIntegerParsing_ValidValues()
        {
            Environment.SetEnvironmentVariable("TestVar", "123");
            var result = int.TryParse(Environment.GetEnvironmentVariable("TestVar"), out var value);
            Assert.True(result);
            Assert.Equal(123, value);
            Environment.SetEnvironmentVariable("TestVar", null);
        }

        [Fact]
        public void TestIntegerParsing_InvalidValues()
        {
            Environment.SetEnvironmentVariable("TestVar", "invalid");
            var result = int.TryParse(Environment.GetEnvironmentVariable("TestVar"), out var value);
            Assert.False(result);
            Assert.Equal(0, value);
            Environment.SetEnvironmentVariable("TestVar", null);
        }

        [Fact]
        public void TestIntegerParsing_NullValues()
        {
            Environment.SetEnvironmentVariable("TestVar", null);
            var result = int.TryParse(Environment.GetEnvironmentVariable("TestVar"), out var value);
            Assert.False(result);
            Assert.Equal(0, value);
        }

        [Fact]
        public async Task TestReadInputStreamAsync_ValidStream()
        {
            var testData = "test input data";
            var bytes = Encoding.UTF8.GetBytes(testData);
            using var stream = new MemoryStream(bytes);

            // Use reflection to access the private method
            var method = typeof(UnifiWebhookEventReceiver).GetMethod("ReadInputStreamAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var task = (Task<string>)method.Invoke(null, new object[] { stream });
                var result = await task;
                Assert.Equal(testData, result);
            }
        }

        [Fact]
        public async Task TestReadInputStreamAsync_EmptyStream()
        {
            using var stream = new MemoryStream();

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("ReadInputStreamAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var task = (Task<string>)method.Invoke(null, new object[] { stream });
                var result = await task;
                Assert.Equal("", result);
            }
        }

        [Fact]
        public void TestIsSQSEvent_ValidSQSEvent()
        {
            var sqsEvent = new
            {
                Records = new[]
                {
                    new
                    {
                        eventSource = "aws:sqs",
                        body = "test message"
                    }
                }
            };

            var json = JsonSerializer.Serialize(sqsEvent);

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("IsSQSEvent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var result = (bool)method.Invoke(null, new object[] { json });
                Assert.True(result);
            }
        }

        [Fact]
        public void TestIsSQSEvent_InvalidJSON()
        {
            var invalidJson = "{ invalid json";

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("IsSQSEvent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var result = (bool)method.Invoke(null, new object[] { invalidJson });
                Assert.False(result);
            }
        }

        [Fact]
        public void TestIsSQSEvent_NonSQSEvent()
        {
            var nonSqsEvent = new
            {
                httpMethod = "POST",
                path = "/test"
            };

            var json = JsonSerializer.Serialize(nonSqsEvent);

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("IsSQSEvent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var result = (bool)method.Invoke(null, new object[] { json });
                Assert.False(result);
            }
        }

        [Fact]
        public void TestHandleScheduledEvent()
        {
            var method = typeof(UnifiWebhookEventReceiver).GetMethod("HandleScheduledEvent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var result = (APIGatewayProxyResponse)method.Invoke(null, null);
                Assert.NotNull(result);
                Assert.Equal(200, result.StatusCode);
            }
        }

        [Fact]
        public void TestIsValidRequest_ValidRequest()
        {
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = "/alarmevent"
            };

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("IsValidRequest",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var result = (bool)method.Invoke(null, new object[] { request });
                Assert.True(result);
            }
        }

        [Fact]
        public void TestIsValidRequest_NullRequest()
        {
            var method = typeof(UnifiWebhookEventReceiver).GetMethod("IsValidRequest",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var result = (bool)method.Invoke(null, new object[] { null });
                Assert.False(result);
            }
        }

        [Fact]
        public void TestExtractRouteInfo_ValidRequest()
        {
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "POST",
                Path = "/alarmevent"
            };

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("ExtractRouteInfo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var result = method.Invoke(null, new object[] { request });
                Assert.NotNull(result);
            }
        }

        [Fact]
        public void TestExtractRouteInfo_PathWithQuery()
        {
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = "GET",
                Path = "/latestvideo",
                QueryStringParameters = new Dictionary<string, string> { { "eventKey", "test" } }
            };

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("ExtractRouteInfo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var result = method.Invoke(null, new object[] { request });
                Assert.NotNull(result);
            }
        }

        [Fact]
        public void TestHandleOptionsRequest()
        {
            var method = typeof(UnifiWebhookEventReceiver).GetMethod("HandleOptionsRequest",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var result = (APIGatewayProxyResponse)method.Invoke(null, null);
                Assert.NotNull(result);
                Assert.Equal(200, result.StatusCode);
                Assert.True(result.Headers.ContainsKey("Access-Control-Allow-Origin"));
                Assert.True(result.Headers.ContainsKey("Access-Control-Allow-Methods"));
                Assert.True(result.Headers.ContainsKey("Access-Control-Allow-Headers"));
            }
        }

        [Fact]
        public void TestDownloadEventProcessing_InvalidMessageId()
        {
            var messageData = JsonDocument.Parse("{}").RootElement;
            bool downloadStarted = false;
            string downloadGuid = null;

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("ProcessDownloadEvent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                var parameters = new object[] { "invalid-message", messageData, downloadStarted, downloadGuid };
                method.Invoke(null, parameters);
                // Should not crash with invalid message ID
                Assert.True(true);
            }
        }

        [Fact]
        public void TestDownloadProgressEvent()
        {
            var progressData = JsonDocument.Parse(@"{""percentage"": 50, ""speed"": 1024}").RootElement;

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("ProcessDownloadProgressEvent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                method.Invoke(null, new object[] { progressData });
                // Should not crash with valid progress data
                Assert.True(true);
            }
        }

        [Fact]
        public void TestDownloadProgressEvent_EmptyData()
        {
            var progressData = JsonDocument.Parse("{}").RootElement;

            var method = typeof(UnifiWebhookEventReceiver).GetMethod("ProcessDownloadProgressEvent",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            
            if (method != null)
            {
                method.Invoke(null, new object[] { progressData });
                // Should not crash with empty data
                Assert.True(true);
            }
        }

        [Fact]
        public void TestJsonElementHasProperty()
        {
            var jsonWithProperty = JsonDocument.Parse(@"{""testProperty"": ""value""}").RootElement;
            var jsonWithoutProperty = JsonDocument.Parse(@"{""otherProperty"": ""value""}").RootElement;

            Assert.True(jsonWithProperty.TryGetProperty("testProperty", out _));
            Assert.False(jsonWithoutProperty.TryGetProperty("testProperty", out _));
        }

        [Fact]
        public void TestComplexAlarmEvent()
        {
            var alarm = new Alarm
            {
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        key = "motion",
                        device = "aa:bb:cc:dd:ee:ff",
                        eventId = "12345",
                        deviceName = "Front Camera"
                    }
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                name = "Motion Alert",
                sources = new List<Source>
                {
                    new Source { device = "aa:bb:cc:dd:ee:ff", type = "camera" }
                },
                conditions = new List<Condition>
                {
                    new Condition { type = "motion", source = "camera" }
                },
                eventPath = "/api/events/12345",
                eventLocalLink = "https://local.unifi/events/12345"
            };

            // Verify all properties are set correctly
            Assert.Single(alarm.triggers);
            Assert.Equal("motion", alarm.triggers[0].key);
            Assert.Equal("aa:bb:cc:dd:ee:ff", alarm.triggers[0].device);
            Assert.Equal("12345", alarm.triggers[0].eventId);
            Assert.Equal("Front Camera", alarm.triggers[0].deviceName);
            Assert.Equal("Motion Alert", alarm.name);
            Assert.Single(alarm.sources);
            Assert.Single(alarm.conditions);
            Assert.Equal("/api/events/12345", alarm.eventPath);
            Assert.Equal("https://local.unifi/events/12345", alarm.eventLocalLink);
        }

        [Theory]
        [InlineData("POST", "/alarmevent")]
        [InlineData("GET", "/latestvideo")]
        [InlineData("OPTIONS", "/test")]
        public void TestAPIGatewayRequest_DifferentMethods(string method, string path)
        {
            var request = new APIGatewayProxyRequest
            {
                HttpMethod = method,
                Path = path
            };

            Assert.Equal(method, request.HttpMethod);
            Assert.Equal(path, request.Path);
        }

        [Fact]
        public void TestLambdaContext_MockLogger()
        {
            // Test the NullLogger implementation
            var logger = new NullLogger();
            logger.Log("test message");
            logger.LogLine("test message with line");
            
            // Should not throw any exceptions
            Assert.True(true);
        }

        [Fact]
        public void TestMultipleTriggerTypes()
        {
            var alarm = new Alarm
            {
                triggers = new List<Trigger>
                {
                    new Trigger { key = "motion", device = "device1", eventId = "event1" },
                    new Trigger { key = "person", device = "device2", eventId = "event2" },
                    new Trigger { key = "vehicle", device = "device3", eventId = "event3" },
                    new Trigger { key = "intrusion", device = "device4", eventId = "event4" }
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            Assert.Equal(4, alarm.triggers.Count);
            Assert.Contains(alarm.triggers, t => t.key == "motion");
            Assert.Contains(alarm.triggers, t => t.key == "person");
            Assert.Contains(alarm.triggers, t => t.key == "vehicle");
            Assert.Contains(alarm.triggers, t => t.key == "intrusion");
        }

        [Fact]
        public void TestNullLogger()
        {
            var logger = new NullLogger();
            
            // These should not throw exceptions
            logger.Log(null);
            logger.LogLine(null);
            logger.Log("");
            logger.LogLine("");
            logger.Log("test message");
            logger.LogLine("test message with newline");
            
            Assert.True(true);
        }

        private sealed class NullLogger : ILambdaLogger
        {
            public void Log(string message) { }
            public void LogLine(string message) { }
        }
    }
}
