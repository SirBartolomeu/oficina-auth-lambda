using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;
using OficinaMvp.Infrastructure.Messaging;

namespace OficinaMvp.Api.Messaging;

public sealed class OsEventsSqsWorker(IAmazonSQS sqs, IServiceScopeFactory scopes,
    IConfiguration configuration, ILogger<OsEventsSqsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueUrl = configuration["Messaging:OsEventsQueueUrl"]
            ?? throw new InvalidOperationException("Messaging:OsEventsQueueUrl é obrigatório.");
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
                        if (eventType == "BudgetReady")
                            await scope.ServiceProvider.GetRequiredService<BudgetReadyHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType is "BudgetApproved" or "BudgetRefused")
                            await scope.ServiceProvider.GetRequiredService<BudgetDecisionHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType is "StockReserved" or "StockReservationRejected")
                            await scope.ServiceProvider.GetRequiredService<StockReservationResultHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else if (eventType == "PaymentConfirmed")
                            await scope.ServiceProvider.GetRequiredService<PaymentConfirmedHandler>()
                                .HandleAsync(message.Body, stoppingToken);
                        else
                            throw new InvalidOperationException($"Evento não suportado: {eventType}");
                        await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning(error, "Falha ao consumir evento {MessageId} na OS", message.MessageId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Falha ao receber eventos SQS na OS");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
