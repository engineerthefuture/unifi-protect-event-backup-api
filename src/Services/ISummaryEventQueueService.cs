using System.Threading.Tasks;
using UnifiWebhookEventReceiver.Models;

namespace UnifiWebhookEventReceiver.Services
{
    public interface ISummaryEventQueueService
    {
        Task<string> SendSummaryEventAsync(SummaryEvent summaryEvent);
    }
}
