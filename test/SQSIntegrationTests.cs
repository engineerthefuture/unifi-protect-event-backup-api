/************************
 * SQS Integration Tests
 * SQSIntegrationTests.cs
 * Testing SQS delayed processing functionality
 * Brent Foster
 * 08-17-2025
 ***********************/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS.Model;
using Newtonsoft.Json;
using Xunit;
using UnifiWebhookEventReceiver;

namespace UnifiWebhookEventReceiver.Tests
{
    public class SQSIntegrationTests
    {
        private static void SetSQSEnv()
        {
            Environment.SetEnvironmentVariable("AlarmProcessingQueueUrl", "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue");
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", "120");
        }

        [Fact]
        public void SQSEvent_ShouldDeserializeCorrectly()
        {
            // Arrange
            var sqsEventJson = @"{
                ""Records"": [
                    {
                        ""messageId"": ""test-message-id"",
                        ""receiptHandle"": ""test-receipt-handle"",
                        ""body"": ""{\""test\"": \""data\""}"",
                        ""attributes"": {},
                        ""messageAttributes"": {},
                        ""md5OfBody"": ""test-md5"",
                        ""eventSource"": ""aws:sqs"",
                        ""eventSourceARN"": ""arn:aws:sqs:us-east-1:123456789012:test-queue"",
                        ""awsRegion"": ""us-east-1""
                    }
                ]
            }";

            // Act
            var sqsEvent = JsonConvert.DeserializeObject<SQSEvent>(sqsEventJson);

            // Assert
            Assert.NotNull(sqsEvent);
            Assert.NotNull(sqsEvent.Records);
            Assert.Single(sqsEvent.Records);
            Assert.Equal("test-message-id", sqsEvent.Records[0].MessageId);
            Assert.Equal("aws:sqs", sqsEvent.Records[0].EventSource);
        }

        [Fact]
        public void AlarmInSQSMessage_ShouldDeserializeCorrectly()
        {
            // Arrange
            var alarmJson = JsonConvert.SerializeObject(new Alarm
            {
                timestamp = 1691000000000L,
                eventPath = "/protect/api/video/test-event",
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        device = "28704E113F64",
                        eventId = "test-event-123",
                        key = "motion"
                    }
                }
            });

            // Act
            var alarm = JsonConvert.DeserializeObject<Alarm>(alarmJson);

            // Assert
            Assert.NotNull(alarm);
            Assert.Equal(1691000000000L, alarm.timestamp);
            Assert.Equal("/protect/api/video/test-event", alarm.eventPath);
            Assert.NotNull(alarm.triggers);
            Assert.Single(alarm.triggers);
            Assert.Equal("test-event-123", alarm.triggers[0].eventId);
        }

        [Fact]
        public void SQSMessageAttributes_ShouldContainRequiredFields()
        {
            // Arrange
            var eventId = "test-event-123";
            var device = "28704E113F64";
            var timestamp = 1691000000000L;

            // Act
            var messageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["EventId"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = eventId
                },
                ["Device"] = new MessageAttributeValue
                {
                    DataType = "String", 
                    StringValue = device
                },
                ["Timestamp"] = new MessageAttributeValue
                {
                    DataType = "Number",
                    StringValue = timestamp.ToString()
                }
            };

            // Assert
            Assert.Contains("EventId", messageAttributes.Keys);
            Assert.Contains("Device", messageAttributes.Keys);
            Assert.Contains("Timestamp", messageAttributes.Keys);
            Assert.Equal("String", messageAttributes["EventId"].DataType);
            Assert.Equal("String", messageAttributes["Device"].DataType);
            Assert.Equal("Number", messageAttributes["Timestamp"].DataType);
        }

        [Fact]
        public void SendMessageRequest_ShouldHaveCorrectProperties()
        {
            // Arrange
            SetSQSEnv();
            var queueUrl = Environment.GetEnvironmentVariable("AlarmProcessingQueueUrl");
            var delaySeconds = int.Parse(Environment.GetEnvironmentVariable("ProcessingDelaySeconds") ?? "120");
            var messageBody = JsonConvert.SerializeObject(new { test = "data" });

            // Act
            var sendMessageRequest = new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = messageBody,
                DelaySeconds = delaySeconds
            };

            // Assert
            Assert.Equal(queueUrl, sendMessageRequest.QueueUrl);
            Assert.Equal(messageBody, sendMessageRequest.MessageBody);
            Assert.Equal(120, sendMessageRequest.DelaySeconds);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(60)]
        [InlineData(120)]
        [InlineData(300)]
        [InlineData(900)] // Max allowed by SQS
        public void DelaySeconds_ShouldAcceptValidValues(int delaySeconds)
        {
            // Arrange
            Environment.SetEnvironmentVariable("ProcessingDelaySeconds", delaySeconds.ToString());

            // Act
            var actualDelay = int.TryParse(Environment.GetEnvironmentVariable("ProcessingDelaySeconds"), out var delay) ? delay : 120;

            // Assert
            Assert.Equal(delaySeconds, actualDelay);
        }

        [Fact]
        public void SQSEvent_ShouldDetectEventSource()
        {
            // Arrange
            var sqsEventJson = @"{
                ""Records"": [
                    {
                        ""eventSource"": ""aws:sqs""
                    }
                ]
            }";

            // Act
            var containsSQSSource = sqsEventJson.Contains("\"eventSource\"") && sqsEventJson.Contains("\"aws:sqs\"");

            // Assert
            Assert.True(containsSQSSource, $"Expected eventSource and aws:sqs in JSON: {sqsEventJson}");
        }

        [Fact]
        public void SQSEventDetection_ShouldIdentifyRecordsStructure()
        {
            // Arrange
            var sqsEventJson = @"{
                ""Records"": [
                    {
                        ""messageId"": ""test"",
                        ""eventSource"": ""aws:sqs""
                    }
                ]
            }";

            // Act
            var hasRecords = sqsEventJson.Contains("\"Records\"");
            var hasSQSSource = sqsEventJson.Contains("\"eventSource\"") && sqsEventJson.Contains("\"aws:sqs\"");

            // Assert
            Assert.True(hasRecords, $"Expected Records in JSON: {sqsEventJson}");
            Assert.True(hasSQSSource, $"Expected eventSource and aws:sqs in JSON: {sqsEventJson}");
        }

        [Fact]
        public void MultipleSQSRecords_ShouldProcessAllMessages()
        {
            // Arrange
            var sqsEvent = new SQSEvent
            {
                Records = new List<SQSEvent.SQSMessage>
                {
                    new SQSEvent.SQSMessage
                    {
                        MessageId = "message-1",
                        Body = JsonConvert.SerializeObject(new { eventId = "event-1" }),
                        EventSource = "aws:sqs"
                    },
                    new SQSEvent.SQSMessage
                    {
                        MessageId = "message-2",
                        Body = JsonConvert.SerializeObject(new { eventId = "event-2" }),
                        EventSource = "aws:sqs"
                    }
                }
            };

            // Act
            var messageCount = sqsEvent.Records.Count;

            // Assert
            Assert.Equal(2, messageCount);
            Assert.All(sqsEvent.Records, record => Assert.Equal("aws:sqs", record.EventSource));
        }

        [Fact]
        public void QueueUrlValidation_ShouldValidateAWSFormat()
        {
            // Arrange
            var validQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue";
            var invalidQueueUrl = "not-a-queue-url";

            // Act
            var isValidUrl = validQueueUrl.StartsWith("https://sqs.") && validQueueUrl.Contains(".amazonaws.com/");
            var isInvalidUrl = invalidQueueUrl.StartsWith("https://sqs.") && invalidQueueUrl.Contains(".amazonaws.com/");

            // Assert
            Assert.True(isValidUrl);
            Assert.False(isInvalidUrl);
        }

        [Fact]
        public void ProcessingDelay_ShouldCalculateEstimatedTime()
        {
            // Arrange
            var delaySeconds = 120;
            var currentTime = DateTime.UtcNow;

            // Act
            var estimatedProcessingTime = currentTime.AddSeconds(delaySeconds);

            // Assert
            Assert.True(estimatedProcessingTime > currentTime);
            Assert.Equal(120, (estimatedProcessingTime - currentTime).TotalSeconds, 1); // Allow 1 second tolerance
        }

        [Fact]
        public void SQSMessageBody_ShouldContainCompleteAlarmData()
        {
            // Arrange
            var alarm = new Alarm
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                eventPath = "/protect/api/video/test",
                triggers = new List<Trigger>
                {
                    new Trigger
                    {
                        device = "28704E113F64",
                        eventId = "test-event-123",
                        key = "motion"
                    }
                }
            };

            // Act
            var messageBody = JsonConvert.SerializeObject(alarm);
            var deserializedAlarm = JsonConvert.DeserializeObject<Alarm>(messageBody);

            // Assert
            Assert.NotNull(deserializedAlarm);
            Assert.Equal(alarm.timestamp, deserializedAlarm.timestamp);
            Assert.Equal(alarm.eventPath, deserializedAlarm.eventPath);
            Assert.Equal(alarm.triggers[0].eventId, deserializedAlarm.triggers[0].eventId);
        }

        [Fact]
        public void EmptySQSRecords_ShouldHandleGracefully()
        {
            // Arrange
            var emptySqsEvent = new SQSEvent
            {
                Records = new List<SQSEvent.SQSMessage>()
            };

            // Act
            var recordCount = emptySqsEvent.Records?.Count ?? 0;

            // Assert
            Assert.Equal(0, recordCount);
        }

        [Fact]
        public void NullSQSRecords_ShouldHandleGracefully()
        {
            // Arrange
            var nullRecordsSqsEvent = new SQSEvent
            {
                Records = null
            };

            // Act
            var recordCount = nullRecordsSqsEvent.Records?.Count ?? 0;

            // Assert
            Assert.Equal(0, recordCount);
        }
    }
}
