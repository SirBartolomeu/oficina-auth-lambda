using System.Diagnostics;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace OficinaMvp.Infrastructure.Messaging;

public sealed class SqsIntegrationMessageSender : IIntegrationMessageSender
{
    private readonly IAmazonSQS _sqs;
    private readonly IReadOnlyDictionary<string, string> _queueUrls;

    public SqsIntegrationMessageSender(IAmazonSQS sqs, string billingQueueUrl)
        : this(sqs, new Dictionary<string, string> { ["WorkOrderOpened"] = billingQueueUrl }) { }

    public SqsIntegrationMessageSender(IAmazonSQS sqs, IReadOnlyDictionary<string, string> queueUrls)
    {
        _sqs = sqs;
        _queueUrls = queueUrls;
    }

    public async Task SendAsync(Guid eventId, string eventType, string payloadJson, CancellationToken cancellationToken)
    {
        if (!_queueUrls.TryGetValue(eventType, out var queueUrl) || string.IsNullOrWhiteSpace(queueUrl))
            throw new InvalidOperationException($"Fila SQS não configurada para {eventType}.");
        using var activity = new Activity("SQS publish").SetIdFormat(ActivityIdFormat.W3C);
        var storedParent = SqsTraceContext.Read(payloadJson);
        if (storedParent is not null) activity.SetParentId(storedParent);
        activity.Start();
        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = payloadJson,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["eventType"] = new() { DataType = "String", StringValue = eventType },
                ["traceparent"] = new() { DataType = "String", StringValue = activity.Id }
            }
        };
        if (queueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(payloadJson);
            request.MessageGroupId = document.RootElement.GetProperty("workOrderId").GetString();
            request.MessageDeduplicationId = eventId.ToString("N");
        }
        await _sqs.SendMessageAsync(request, cancellationToken);
    }
}
