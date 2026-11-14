using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Oficina.Billing;
using OficinaMvp.Api.Messaging;
using OficinaMvp.Domain.Entities;
using OsEvents = OficinaMvp.Application.Contracts;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class OpenedEventFlowTests
{
    [Fact]
    public async Task OutboxRoundTripCreatesBudgetAndMovesOrderToApproval()
    {
        var sqsEndpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        var dynamoEndpoint = Environment.GetEnvironmentVariable("BILLING_DYNAMO_ENDPOINT")
            ?? throw new InvalidOperationException("Defina BILLING_DYNAMO_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = sqsEndpoint });
        using var dynamo = new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = dynamoEndpoint });
        var queueUrl = (await sqs.CreateQueueAsync("billing-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var osQueueUrl = (await sqs.CreateQueueAsync("os-events-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var table = "billing_flow_test_" + Guid.NewGuid().ToString("N");
        await dynamo.CreateTableAsync(new CreateTableRequest
        {
            TableName = table,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions = [new("pk", ScalarAttributeType.S), new("sk", ScalarAttributeType.S)],
            KeySchema = [new("pk", KeyType.HASH), new("sk", KeyType.RANGE)]
        });

        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var serviceId = Guid.NewGuid();
        var order = new WorkOrder(customer.Id, vehicle.Id,
            [new WorkOrderServiceLine(serviceId, "Revisão", 1, 125m)],
            [], null, DateTime.UtcNow, "sp-centro");
        var orderId = order.Id;
        var eventId = Guid.NewGuid();
        var envelope = new OsEvents.IntegrationEnvelope<OsEvents.WorkOrderOpenedPayload>(eventId, "WorkOrderOpened", 1,
            orderId, Guid.NewGuid(), orderId, "sp-centro", DateTime.UtcNow,
            new OsEvents.WorkOrderOpenedPayload(1,
                [new OsEvents.WorkOrderOpenedLine("Service", serviceId, "Revisão", 1, 12500)],
                12500, "BRL", DateTime.UtcNow));
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await using var sqlite = new SqliteConnection("Data Source=:memory:");
        await sqlite.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<WorkshopDbContext>().UseSqlite(sqlite).Options;
        await using var db = new WorkshopDbContext(dbOptions);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(customer, vehicle, order);
        db.OutboxMessages.Add(new OutboxMessage(eventId, "WorkOrderOpened", json, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var dispatcher = new OutboxDispatcher(db, new SqsIntegrationMessageSender(sqs, queueUrl),
            NullLogger<OutboxDispatcher>.Instance);
        Assert.Equal(1, await dispatcher.DispatchBatchAsync(CancellationToken.None));
        Assert.NotNull((await db.OutboxMessages.SingleAsync()).PublishedAtUtc);
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DynamoDb:Table"] = table,
                ["Messaging:WorkOrdersQueueUrl"] = queueUrl,
                ["Messaging:OsEventsQueueUrl"] = osQueueUrl
            }).Build();
        var repository = new BillingRepository(dynamo, settings);
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(settings)
            .AddSingleton<IAmazonDynamoDB>(dynamo)
            .AddScoped<BillingRepository>()
            .AddScoped<IBudgetEventStore>(provider => provider.GetRequiredService<BillingRepository>())
            .AddScoped<WorkOrderOpenedHandler>()
            .AddScoped<PaymentRequestHandler>()
            .BuildServiceProvider();
        using var worker = new BillingSqsWorker(sqs, services.GetRequiredService<IServiceScopeFactory>(),
            settings, NullLogger<BillingSqsWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        Budget? budget = null;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            while (budget is null && !timeout.IsCancellationRequested)
            {
                budget = await repository.GetAsync(orderId, 1, CancellationToken.None);
                if (budget is null) await Task.Delay(100, timeout.Token);
            }
        }
        Assert.NotNull(budget);
        var duplicate = await new WorkOrderOpenedHandler(repository).HandleAsync(json, CancellationToken.None);
        var ready = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));

        using var osServices = new ServiceCollection()
            .AddScoped(_ => new WorkshopDbContext(dbOptions))
            .AddScoped<BudgetReadyHandler>()
            .AddScoped<BudgetDecisionHandler>()
            .AddScoped<PaymentConfirmedHandler>()
            .BuildServiceProvider();
        using var billingOutbox = new BillingOutboxWorker(sqs,
            services.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<BillingOutboxWorker>.Instance);
        using var osWorker = new OsEventsSqsWorker(sqs,
            osServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<OsEventsSqsWorker>.Instance);
        await billingOutbox.StartAsync(CancellationToken.None);
        await osWorker.StartAsync(CancellationToken.None);
        WorkOrderStatus status = WorkOrderStatus.Received;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            while (status != WorkOrderStatus.AwaitingApproval && !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(dbOptions);
                status = (await verify.WorkOrders.SingleAsync(item => item.Id == orderId)).Status;
                if (status != WorkOrderStatus.AwaitingApproval)
                    await Task.Delay(100, timeout.Token);
            }
        }
        Assert.Equal(12500, budget.TotalCents);
        Assert.Equal(budget.WorkOrderId, duplicate.WorkOrderId);
        Assert.NotNull(await repository.GetAsync(orderId, 1, CancellationToken.None));
        Assert.Equal(WorkOrderStatus.AwaitingApproval, status);
        await using var finalDb = new WorkshopDbContext(dbOptions);
        Assert.Equal(budget.Id, (await finalDb.WorkOrders.SingleAsync(item => item.Id == orderId)).BudgetId);
        Assert.Single(await finalDb.InboxMessages.ToListAsync());
        Assert.False(await new BudgetReadyHandler(finalDb).HandleAsync(ready.Body, CancellationToken.None));
        Assert.Empty(await repository.GetPendingOutboxAsync(CancellationToken.None));

        var approved = await repository.DecideAsync(orderId, 1, true, CancellationToken.None);
        Assert.Equal(BudgetStatus.Approved, approved!.Status);
        Assert.Equal(BudgetStatus.Approved,
            (await repository.DecideAsync(orderId, 1, true, CancellationToken.None))!.Status);
        var decision = Assert.Single(await repository.GetPendingOutboxAsync(CancellationToken.None));
        WorkOrderProcessState decisionState = WorkOrderProcessState.AwaitingApproval;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            while (decisionState == WorkOrderProcessState.AwaitingApproval && !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(dbOptions);
                decisionState = (await verify.WorkOrders.SingleAsync(item => item.Id == orderId)).ProcessState;
                if (decisionState == WorkOrderProcessState.AwaitingApproval)
                    await Task.Delay(100);
            }
        }
        await using var decisionDb = new WorkshopDbContext(dbOptions);
        Assert.False(await new BudgetDecisionHandler(decisionDb).HandleAsync(decision.Body, CancellationToken.None));
        var decidedOrder = await decisionDb.WorkOrders.SingleAsync(item => item.Id == orderId);
        Assert.Equal(WorkOrderProcessState.AwaitingStockReservation, decisionState);
        Assert.Equal(decisionState, decidedOrder.ProcessState);
        Assert.Equal(2, await decisionDb.InboxMessages.CountAsync());
        var reserveCommand = Assert.Single(await decisionDb.OutboxMessages
            .Where(item => item.EventType == "ReserveStockRequested").ToListAsync());
        reserveCommand.MarkPublished(DateTime.UtcNow);
        await decisionDb.SaveChangesAsync();
        Assert.Empty(await repository.GetPendingOutboxAsync(CancellationToken.None));

        var reservedOrder = await decisionDb.WorkOrders.AsNoTracking()
            .SingleAsync(item => item.Id == orderId);
        var reserved = new OsEvents.IntegrationEnvelope<OsEvents.StockReservationResultPayload>(
            Guid.NewGuid(), "StockReserved", 1, orderId, decision.EventId,
            orderId, "sp-centro", DateTime.UtcNow,
            new OsEvents.StockReservationResultPayload(reservedOrder.ReservationId!.Value, 1, null));
        await using (var reservationDb = new WorkshopDbContext(dbOptions))
            Assert.True(await new StockReservationResultHandler(reservationDb).HandleAsync(
                JsonSerializer.Serialize(reserved, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                CancellationToken.None));
        var paymentAttemptId = (await decisionDb.WorkOrders.AsNoTracking()
            .SingleAsync(item => item.Id == orderId)).PaymentAttemptId!.Value;
        await using (var dispatchDb = new WorkshopDbContext(dbOptions))
        {
            var sender = new SqsIntegrationMessageSender(sqs,
                new Dictionary<string, string> { ["CreatePaymentRequested"] = queueUrl });
            Assert.Equal(1, await new OutboxDispatcher(dispatchDb, sender,
                NullLogger<OutboxDispatcher>.Instance).DispatchBatchAsync(CancellationToken.None));
        }
        PaymentRequest? payment = null;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            while (payment is null && !timeout.IsCancellationRequested)
            {
                payment = await repository.GetPaymentRequestAsync(paymentAttemptId, CancellationToken.None);
                if (payment is null) await Task.Delay(100);
            }
        }
        await worker.StopAsync(CancellationToken.None);
        Assert.NotNull(payment);
        Assert.Equal(12500, payment.TotalCents);
        Assert.Equal(budget.Id, payment.BudgetId);
        Assert.True(await repository.CompletePaymentCreationAsync(paymentAttemptId,
            new MercadoPagoQrOrder("ORDTEST123", "qr-test", "created"), CancellationToken.None));
        Assert.True(await repository.ConfirmPaymentAsync(payment, "ORDTEST123", CancellationToken.None));
        Assert.False(await repository.ConfirmPaymentAsync(payment, "ORDTEST123", CancellationToken.None));
        WorkOrderProcessState paymentState = WorkOrderProcessState.AwaitingPayment;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            while (paymentState == WorkOrderProcessState.AwaitingPayment && !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(dbOptions);
                paymentState = (await verify.WorkOrders.SingleAsync(item => item.Id == orderId)).ProcessState;
                if (paymentState == WorkOrderProcessState.AwaitingPayment)
                    await Task.Delay(100);
            }
        }
        await billingOutbox.StopAsync(CancellationToken.None);
        await osWorker.StopAsync(CancellationToken.None);
        Assert.Equal(WorkOrderProcessState.ReadyForExecution, paymentState);
        await using var paymentDb = new WorkshopDbContext(dbOptions);
        Assert.Equal(4, await paymentDb.InboxMessages.CountAsync());
    }

}
