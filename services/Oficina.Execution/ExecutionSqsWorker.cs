using System.Diagnostics;
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
                    QueueUrl = queueUrl, MaxNumberOfMessages = 10, WaitTimeSeconds = 10,
                    MessageAttributeNames = ["traceparent"]
                }, stoppingToken);
                foreach (var message in response.Messages ?? [])
                {
                    string? eventId = null;
                    string? correlationId = null;
                    try
                    {
                        using var scope = scopes.CreateScope();
                        using var envelope = JsonDocument.Parse(message.Body);
                        var root = envelope.RootElement;
                        var eventType = root.GetProperty("eventType").GetString();
                        eventId = root.GetProperty("eventId").GetString();
                        correlationId = root.GetProperty("correlationId").GetString();
                        using var activity = new Activity("SQS consume").SetIdFormat(ActivityIdFormat.W3C);
                        if (message.MessageAttributes?.TryGetValue("traceparent", out var traceAttribute) == true &&
                            ActivityContext.TryParse(traceAttribute.StringValue, null, out _))
                            activity.SetParentId(traceAttribute.StringValue);
                        activity.Start();
                        using var logScope = logger.BeginScope(new Dictionary<string, object?>
                        {
                            ["eventId"] = eventId,
                            ["correlationId"] = correlationId,
                            ["workOrderId"] = root.GetProperty("workOrderId").GetString()
                        });
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
                        logger.LogInformation("Comando {EventType} processado na Execução", eventType);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        logger.LogWarning(error,
                            "Falha ao consumir comando {MessageId} {EventId} {CorrelationId} na Execução",
                            message.MessageId, eventId, correlationId);
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
                    using var activity = new Activity("SQS publish").SetIdFormat(ActivityIdFormat.W3C);
                    var storedParent = SqsTraceContext.Read(item.Body);
                    if (storedParent is not null) activity.SetParentId(storedParent);
                    activity.Start();
                    var request = new SendMessageRequest
                    {
                        QueueUrl = queueUrl, MessageBody = item.Body,
                        MessageAttributes = new Dictionary<string, MessageAttributeValue>
                        {
                            ["traceparent"] = new() { DataType = "String", StringValue = activity.Id }
                        }
                    };
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
