extern alias execution;

using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ExecutionCommandsWorker = execution::Oficina.Execution.ExecutionCommandsWorker;
using ExecutionOutboxWorker = execution::Oficina.Execution.ExecutionOutboxWorker;
using ExecutionService = execution::Oficina.Execution.ExecutionService;
using ReserveStockRequestedHandler = execution::Oficina.Execution.ReserveStockRequestedHandler;
using ExecutionDbContext = execution::Oficina.Execution.Persistence.ExecutionDbContext;
using ExecutionPartSupply = execution::OficinaMvp.Domain.Entities.PartSupply;
using OficinaMvp.Api.Messaging;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;
using Xunit;

namespace Oficina.Messaging.Local.Tests;

public sealed class StockReservationFlowTests
{
    [Theory]
    [InlineData(2, 3, WorkOrderProcessState.AwaitingPayment)]
    [InlineData(4, 1, WorkOrderProcessState.Canceled)]
    public async Task ReservationResultUpdatesOrderWithoutOverselling(
        int requested, int expectedStock, WorkOrderProcessState expectedState)
    {
        var endpoint = Environment.GetEnvironmentVariable("MESSAGING_SQS_ENDPOINT")
            ?? throw new InvalidOperationException("Defina MESSAGING_SQS_ENDPOINT para o teste local.");
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("local", "local"),
            new AmazonSQSConfig { ServiceURL = endpoint });
        var commandsQueue = (await sqs.CreateQueueAsync("execution-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var osQueue = (await sqs.CreateQueueAsync("os-reserve-test-" + Guid.NewGuid().ToString("N"))).QueueUrl;
        var osConnectionString = $"Data Source=os-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var executionConnectionString = $"Data Source=execution-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var osAnchor = new SqliteConnection(osConnectionString);
        await using var executionAnchor = new SqliteConnection(executionConnectionString);
        await osAnchor.OpenAsync();
        await executionAnchor.OpenAsync();
        var osOptions = new DbContextOptionsBuilder<WorkshopDbContext>().UseSqlite(osConnectionString).Options;
        var executionOptions = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(executionConnectionString).Options;
        var part = new ExecutionPartSupply("Filtro", 10m, expectedStock);
        await using (var executionSetup = new ExecutionDbContext(executionOptions))
        {
            await executionSetup.Database.EnsureCreatedAsync();
            executionSetup.PartSupplies.Add(part);
            await executionSetup.SaveChangesAsync();
        }
        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var order = new WorkOrder(customer.Id, vehicle.Id, [],
            [new WorkOrderPartLine(part.Id, part.Name, requested, part.UnitPrice)],
            null, DateTime.UtcNow, "sp-centro");
        order.AcceptBudgetReady(Guid.NewGuid(), 1, DateTime.UtcNow);
        var reservationId = Guid.NewGuid();
        order.ApproveBudgetForPayment(DateTime.UtcNow, reservationId);
        var requestEvent = new IntegrationEnvelope<ReserveStockRequestedPayload>(
            Guid.NewGuid(), "ReserveStockRequested", 1, order.Id, Guid.NewGuid(),
            order.Id, order.BranchId, DateTime.UtcNow,
            new ReserveStockRequestedPayload(reservationId, 1,
                [new StockItem(part.Id, requested)], DateTime.UtcNow.AddMinutes(15)));
        await using (var osSetup = new WorkshopDbContext(osOptions))
        {
            await osSetup.Database.EnsureCreatedAsync();
            osSetup.AddRange(customer, vehicle, order);
            osSetup.OutboxMessages.Add(new OutboxMessage(requestEvent.EventId,
                requestEvent.EventType, JsonSerializer.Serialize(requestEvent,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)), DateTime.UtcNow));
            await osSetup.SaveChangesAsync();
            var sender = new SqsIntegrationMessageSender(sqs,
                new Dictionary<string, string> { ["ReserveStockRequested"] = commandsQueue });
            await new OutboxDispatcher(osSetup, sender, NullLogger<OutboxDispatcher>.Instance)
                .DispatchBatchAsync(CancellationToken.None);
        }

        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:CommandsQueueUrl"] = commandsQueue,
            ["Messaging:OsEventsQueueUrl"] = osQueue
        }).Build();
        using var executionServices = new ServiceCollection()
            .AddScoped(_ => new ExecutionDbContext(executionOptions))
            .AddScoped<ExecutionService>()
            .AddScoped<ReserveStockRequestedHandler>()
            .BuildServiceProvider();
        using var osServices = new ServiceCollection()
            .AddScoped(_ => new WorkshopDbContext(osOptions))
            .AddScoped<BudgetReadyHandler>()
            .AddScoped<StockReservationResultHandler>()
            .BuildServiceProvider();
        using var commandsWorker = new ExecutionCommandsWorker(sqs,
            executionServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<ExecutionCommandsWorker>.Instance);
        using var executionOutbox = new ExecutionOutboxWorker(sqs,
            executionServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<ExecutionOutboxWorker>.Instance);
        using var osWorker = new OsEventsSqsWorker(sqs,
            osServices.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<OsEventsSqsWorker>.Instance);
        await commandsWorker.StartAsync(CancellationToken.None);
        await executionOutbox.StartAsync(CancellationToken.None);
        await osWorker.StartAsync(CancellationToken.None);
        WorkOrderProcessState state = WorkOrderProcessState.AwaitingStockReservation;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            while (state == WorkOrderProcessState.AwaitingStockReservation && !timeout.IsCancellationRequested)
            {
                await using var verify = new WorkshopDbContext(osOptions);
                state = (await verify.WorkOrders.SingleAsync(item => item.Id == order.Id)).ProcessState;
                if (state == WorkOrderProcessState.AwaitingStockReservation)
                    await Task.Delay(100, timeout.Token);
            }
        }
        await commandsWorker.StopAsync(CancellationToken.None);
        await executionOutbox.StopAsync(CancellationToken.None);
        await osWorker.StopAsync(CancellationToken.None);

        await using var finalOs = new WorkshopDbContext(osOptions);
        await using var finalExecution = new ExecutionDbContext(executionOptions);
        Assert.Equal(expectedState, state);
        Assert.Equal(expectedStock - (expectedState == WorkOrderProcessState.AwaitingPayment ? requested : 0),
            (await finalExecution.PartSupplies.SingleAsync(item => item.Id == part.Id)).StockQuantity);
        Assert.Single(await finalOs.InboxMessages.ToListAsync());
        Assert.Single(await finalExecution.InboxMessages.ToListAsync());
        Assert.Single(await finalExecution.OutboxMessages.ToListAsync());
    }
}

