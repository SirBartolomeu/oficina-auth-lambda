using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Oficina.Execution.Persistence;

namespace Oficina.Execution;

public sealed class ExecutionCommandsWorker(IAmazonSQS sqs, IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<ExecutionCommandsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = configuration["Messaging:CommandsQueueUrl"]
            ?? throw new InvalidOperationException("Messaging:CommandsQueueUrl é obrigatório.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl, MaxNumberOfMessages = 10, WaitTimeSeconds = 10
                }, stoppingToken);
                foreach (var message in response.Messages ?? [])
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        using var envelope = JsonDocument.Parse(message.Body);
                        var eventType = envelope.RootElement.GetProperty("eventType").GetString();
                        if (eventType == "ReserveStockRequested")
                            await scope.ServiceProvider.GetRequiredService<ReserveStockRequestedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "ReleaseStockRequested")
                            await scope.ServiceProvider.GetRequiredService<ReleaseStockRequestedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "ConsumeStockRequested")
                            await scope.ServiceProvider.GetRequiredService<ConsumeStockRequestedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "ExecutionRequested")
                            await scope.ServiceProvider.GetRequiredService<ExecutionRequestedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else
                            throw new InvalidOperationException($"Comando não suportado: {eventType}");
                        await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        logger.LogWarning(error, "Falha ao consumir comando {MessageId} na Execução", message.MessageId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogError(error, "Falha ao receber comandos de Execução");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

public sealed class ExecutionOutboxWorker(IAmazonSQS sqs, IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<ExecutionOutboxWorker> logger) : BackgroundService
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
                var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
                var dispatcher = new ExecutionOutboxDispatcher(db, async (item, ct) =>
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogError(error, "Falha ao processar Outbox da Execução");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
