using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Oficina.Billing;

public sealed class BillingOutboxWorker(IAmazonSQS sqs, IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<BillingOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = configuration["Messaging:OsEventsQueueUrl"]
            ?? throw new InvalidOperationException("Messaging:OsEventsQueueUrl é obrigatório.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<BillingRepository>();
                var dispatcher = new BillingOutboxDispatcher(repository, async (item, ct) =>
                {
                    var request = new SendMessageRequest { QueueUrl = queueUrl, MessageBody = item.Body };
                    if (queueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase))
                    {
                        using var body = JsonDocument.Parse(item.Body);
                        request.MessageGroupId = body.RootElement.GetProperty("workOrderId").GetString();
                        request.MessageDeduplicationId = item.EventId.ToString("N");
                    }
                    await sqs.SendMessageAsync(request, ct);
                }, logger);
                await dispatcher.DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Falha ao processar Outbox do Billing");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
