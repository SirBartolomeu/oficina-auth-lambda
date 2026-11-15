using System.Diagnostics;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using OficinaMvp.Infrastructure.Messaging;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class SqsTraceContextTests
{
    [Fact]
    public async Task OutboxPublisherCarriesW3cParentThroughSqsAttribute()
    {
        var endpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = endpoint });
        var queueUrl = (await sqs.CreateQueueAsync("trace-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        try
        {
            using var parent = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
            var sender = new SqsIntegrationMessageSender(sqs, queueUrl);
            await sender.SendAsync(Guid.NewGuid(), "WorkOrderOpened", "{}", CancellationToken.None);

            var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl, WaitTimeSeconds = 2,
                MessageAttributeNames = ["traceparent"]
            });
            var message = Assert.Single(received.Messages);
            var traceparent = message.MessageAttributes["traceparent"].StringValue;
            Assert.True(ActivityContext.TryParse(traceparent, null, out var context));
            Assert.Equal(parent.TraceId, context.TraceId);
            Assert.NotEqual(parent.SpanId, context.SpanId);
        }
        finally
        {
            await sqs.DeleteQueueAsync(queueUrl);
        }
    }
}
