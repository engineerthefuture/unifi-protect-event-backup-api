using System;
using System.Threading.Tasks;
using Xunit;
using UnifiWebhookEventReceiver;

namespace UnifiWebhookEventReceiver.Tests
{
    /// <summary>
    /// Tests to verify that the "Cannot access a disposed object" error fix is working correctly.
    /// This error occurred when event handlers were not properly cleaned up before browser disposal.
    /// </summary>
    public class BrowserDisposalTests
    {
        [Fact]
        public async Task GetVideoFromLocalUnifiProtectViaHeadlessClient_ShouldNotThrowDisposedObjectException()
        {
            // This test verifies that the fix for "Cannot access a disposed object" error works
            // The original issue was that event handlers were not being properly cleaned up before browser disposal
            
            // Arrange
            var eventLocalLink = "https://test.local/protect/events/event123";
            var deviceName = "TestDevice";
            var credentials = new UnifiWebhookEventReceiver.UnifiWebhookEventReceiver.UnifiCredentials 
            { 
                username = "test", 
                password = "test", 
                hostname = "test.local" 
            };

            // Act & Assert
            // This method should fail gracefully with a meaningful error, not with "Cannot access a disposed object"
            var exception = await Assert.ThrowsAsync<Exception>(async () =>
                await UnifiWebhookEventReceiver.UnifiWebhookEventReceiver.GetVideoFromLocalUnifiProtectViaHeadlessClient(eventLocalLink, deviceName, credentials));

            // The error should be about video download failure, not disposed object access
            Assert.DoesNotContain("Cannot access a disposed object", exception.Message);
            Assert.DoesNotContain("disposed", exception.Message.ToLower());
        }

        [Fact]
        public async Task GetVideoFromLocalUnifiProtectViaHeadlessClient_MultipleCallsShouldNotCauseDisposalIssues()
        {
            // This test verifies that multiple calls don't accumulate disposal issues
            
            // Arrange
            var eventLocalLink = "https://test.local/protect/events/event123";
            var deviceName = "TestDevice";
            var credentials = new UnifiWebhookEventReceiver.UnifiWebhookEventReceiver.UnifiCredentials 
            { 
                username = "test", 
                password = "test", 
                hostname = "test.local" 
            };

            // Act & Assert - multiple calls should not cause disposal issues
            for (int i = 0; i < 3; i++)
            {
                var exception = await Assert.ThrowsAsync<Exception>(async () =>
                    await UnifiWebhookEventReceiver.UnifiWebhookEventReceiver.GetVideoFromLocalUnifiProtectViaHeadlessClient(
                        eventLocalLink, 
                        deviceName, 
                        credentials));

                // Each call should fail gracefully without disposal errors
                Assert.DoesNotContain("Cannot access a disposed object", exception.Message);
                Assert.DoesNotContain("disposed", exception.Message.ToLower());
            }
        }
    }
}
