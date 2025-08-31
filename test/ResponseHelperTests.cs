/************************
 * UnifiWebhookEventReceiver Tests
 * ResponseHelperTests.cs
 * 
 * Comprehensive unit tests for the ResponseHelper service.
 * Tests all HTTP response generation methods and CORS header functionality.
 * 
 * Author: GitHub Copilot
 * Created: 08-20-2025
 ***********************/

#nullable enable

using System;
using System.Net;
using Amazon.Lambda.APIGatewayEvents;
using Newtonsoft.Json;
using UnifiWebhookEventReceiver;
using UnifiWebhookEventReceiver.Services.Implementations;
using Xunit;

namespace UnifiWebhookEventReceiverTests
{
    public class ResponseHelperTests
    {
        private readonly ResponseHelper _responseHelper;

        public ResponseHelperTests()
        {
            // Set up environment variables needed for testing
            Environment.SetEnvironmentVariable("FunctionName", "TestFunction");
            
            _responseHelper = new ResponseHelper();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_CreatesValidInstance()
        {
            // Act
            var responseHelper = new ResponseHelper();

            // Assert
            Assert.NotNull(responseHelper);
        }

        #endregion

        #region Additional Edge Case Tests

        [Fact]
        public void CreateSuccessResponse_WithAllNullTriggerAndZeroTimestamp_DoesNotThrow()
        {
            // Arrange
            var trigger = new Trigger
            {
                key = string.Empty,
                device = string.Empty,
                eventId = string.Empty,
                deviceName = null,
                eventKey = null
            };
            long timestamp = 0;

            // Act
            var result = _responseHelper.CreateSuccessResponse(trigger, timestamp);

            // Assert
            Assert.Equal(200, result.StatusCode);
            Assert.Contains("occurred at", result.Body);
        }

        [Fact]
        public void CreateSuccessResponse_WithEmptyTriggerAndNegativeTimestamp_DoesNotThrow()
        {
            // Arrange
            var trigger = new Trigger
            {
                key = string.Empty,
                device = string.Empty,
                eventId = string.Empty,
                deviceName = string.Empty,
                eventKey = string.Empty
            };
            long timestamp = -1;

            // Act
            var result = _responseHelper.CreateSuccessResponse(trigger, timestamp);

            // Assert
            Assert.Equal(200, result.StatusCode);
            Assert.Contains("occurred at", result.Body);
        }

        #endregion

        #region CreateErrorResponse Tests

        [Fact]
        public void CreateErrorResponse_WithValidParameters_ReturnsCorrectResponse()
        {
            // Arrange
            var statusCode = HttpStatusCode.BadRequest;
            var message = "Invalid request parameters";

            // Act
            var result = _responseHelper.CreateErrorResponse(statusCode, message);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(400, result.StatusCode);
            Assert.NotNull(result.Body);
            Assert.NotNull(result.Headers);
            
            // Verify the body contains the error message
            var body = JsonConvert.DeserializeObject<dynamic>(result.Body!);
            Assert.Equal(message, (string)body!.msg);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, "Bad request error")]
        [InlineData(HttpStatusCode.Unauthorized, "Authentication failed")]
        [InlineData(HttpStatusCode.Forbidden, "Access denied")]
        [InlineData(HttpStatusCode.NotFound, "Resource not found")]
        [InlineData(HttpStatusCode.InternalServerError, "Internal server error")]
        public void CreateErrorResponse_WithDifferentStatusCodes_ReturnsCorrectStatusCode(HttpStatusCode statusCode, string message)
        {
            // Act
            var result = _responseHelper.CreateErrorResponse(statusCode, message);

            // Assert
            Assert.Equal((int)statusCode, result.StatusCode);
            
            var body = JsonConvert.DeserializeObject<dynamic>(result.Body!);
            Assert.Equal(message, (string)body!.msg);
        }

        [Fact]
        public void CreateErrorResponse_WithEmptyMessage_ReturnsResponseWithEmptyMessage()
        {
            // Arrange
            var statusCode = HttpStatusCode.BadRequest;
            var message = "";

            // Act
            var result = _responseHelper.CreateErrorResponse(statusCode, message);

            // Assert
            Assert.Equal(400, result.StatusCode);
            var body = JsonConvert.DeserializeObject<dynamic>(result.Body!);
            Assert.Equal("", (string)body!.msg);
        }

        [Fact]
        public void CreateErrorResponse_WithNullMessage_ReturnsResponseWithNullMessage()
        {
            // Arrange
            var statusCode = HttpStatusCode.InternalServerError;
            string? message = null;

            // Act
            var result = _responseHelper.CreateErrorResponse(statusCode, message!);

            // Assert
            Assert.Equal(500, result.StatusCode);
            var body = JsonConvert.DeserializeObject<dynamic>(result.Body!);
            Assert.Null((string?)body!.msg);
        }

        #endregion

        #region CreateSuccessResponse(object) Tests

        [Fact]
        public void CreateSuccessResponse_WithObjectBody_ReturnsCorrectResponse()
        {
            // Arrange
            var body = new { message = "Success", data = "test data" };

            // Act
            var result = _responseHelper.CreateSuccessResponse(body);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(200, result.StatusCode);
            Assert.NotNull(result.Body);
            Assert.NotNull(result.Headers);
            
            var responseBody = JsonConvert.DeserializeObject<dynamic>(result.Body!);
            Assert.Equal("Success", (string)responseBody!.message);
            Assert.Equal("test data", (string)responseBody.data);
        }

        [Fact]
        public void CreateSuccessResponse_WithStringBody_ReturnsCorrectResponse()
        {
            // Arrange
            var body = "Simple string response";

            // Act
            var result = _responseHelper.CreateSuccessResponse(body);

            // Assert
            Assert.Equal(200, result.StatusCode);
            Assert.Equal("\"Simple string response\"", result.Body); // JSON serialized string
        }

        [Fact]
        public void CreateSuccessResponse_WithNullBody_ReturnsResponseWithNull()
        {
            // Arrange
            object? body = null;

            // Act
            var result = _responseHelper.CreateSuccessResponse(body!);

            // Assert
            Assert.Equal(200, result.StatusCode);
            Assert.Equal("null", result.Body);
        }

        [Fact]
        public void CreateSuccessResponse_WithComplexObject_SerializesCorrectly()
        {
            // Arrange
            var body = new
            {
                id = 123,
                name = "Test Event",
                timestamp = 1640995200000L,
                metadata = new { device = "camera-1", type = "motion" }
            };

            // Act
            var result = _responseHelper.CreateSuccessResponse(body);

            // Assert
            Assert.Equal(200, result.StatusCode);
            
            var responseBody = JsonConvert.DeserializeObject<dynamic>(result.Body!);
            Assert.Equal(123, (int)responseBody!.id);
            Assert.Equal("Test Event", (string)responseBody.name);
            Assert.Equal(1640995200000L, (long)responseBody.timestamp);
            Assert.Equal("camera-1", (string)responseBody.metadata.device);
            Assert.Equal("motion", (string)responseBody.metadata.type);
        }

        #endregion

        #region CreateSuccessResponse(Trigger, long) Tests

        [Fact]
        public void CreateSuccessResponse_WithMinimalTrigger_HandlesNullFields()
        {
            // Arrange
            var trigger = new Trigger
            {
                key = "alarm",
                device = "sensor-456",
                eventId = "event-456",
                deviceName = null, // Null device name
                eventKey = "test-event.json"
            };
            var timestamp = 1640995200000L;

            // Act
            var result = _responseHelper.CreateSuccessResponse(trigger, timestamp);

            // Assert
            Assert.Equal(200, result.StatusCode);
            
            var responseBody = JsonConvert.DeserializeObject<dynamic>(result.Body);
            var message = (string)responseBody!.msg;
            
            Assert.Contains("test-event.json", message);
            // Should handle null deviceName gracefully
            Assert.DoesNotContain("null", message);
        }

        #endregion

        #region GetStandardHeaders Tests

        [Fact]
        public void GetStandardHeaders_ReturnsCorrectHeaders()
        {
            // Act
            var headers = _responseHelper.GetStandardHeaders();

            // Assert
            Assert.NotNull(headers);
            Assert.Equal(2, headers.Count);
            Assert.True(headers.ContainsKey("Content-Type"));
            Assert.True(headers.ContainsKey("Access-Control-Allow-Origin"));
            Assert.Equal("application/json", headers["Content-Type"]);
            Assert.Equal("*", headers["Access-Control-Allow-Origin"]);
        }

        [Fact]
        public void GetStandardHeaders_ReturnsSameHeadersOnMultipleCalls()
        {
            // Act
            var headers1 = _responseHelper.GetStandardHeaders();
            var headers2 = _responseHelper.GetStandardHeaders();

            // Assert
            Assert.Equal(headers1.Count, headers2.Count);
            Assert.Equal(headers1["Content-Type"], headers2["Content-Type"]);
            Assert.Equal(headers1["Access-Control-Allow-Origin"], headers2["Access-Control-Allow-Origin"]);
        }

        #endregion

        #region Headers Integration Tests

        [Fact]
        public void CreateErrorResponse_IncludesStandardHeaders()
        {
            // Act
            var result = _responseHelper.CreateErrorResponse(HttpStatusCode.BadRequest, "Test error");

            // Assert
            Assert.NotNull(result.Headers);
            Assert.True(result.Headers.ContainsKey("Content-Type"));
            Assert.True(result.Headers.ContainsKey("Access-Control-Allow-Origin"));
            Assert.Equal("application/json", result.Headers["Content-Type"]);
            Assert.Equal("*", result.Headers["Access-Control-Allow-Origin"]);
        }

        [Fact]
        public void CreateSuccessResponse_IncludesStandardHeaders()
        {
            // Act
            var result = _responseHelper.CreateSuccessResponse(new { test = "data" });

            // Assert
            Assert.NotNull(result.Headers);
            Assert.True(result.Headers.ContainsKey("Content-Type"));
            Assert.True(result.Headers.ContainsKey("Access-Control-Allow-Origin"));
            Assert.Equal("application/json", result.Headers["Content-Type"]);
            Assert.Equal("*", result.Headers["Access-Control-Allow-Origin"]);
        }

        #endregion
    }
}