using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Contracts;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;
using OficinaMvp.Infrastructure.Messaging;
using OficinaMvp.Infrastructure.Persistence;

namespace OficinaMvp.Integration.Tests;

public sealed class ExecutionRejectionTests
{
    [Fact]
    public async Task RejectionMovesPaidOrderToManualReviewWithoutFinalizing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new WorkshopDbContext(
            new DbContextOptionsBuilder<WorkshopDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var customer = new Customer("Cliente Teste", "52998224725", null, null);
        var vehicle = new Vehicle(customer.Id, "BRA2E19", "VW", "Gol", 2020);
        var order = new WorkOrder(customer.Id, vehicle.Id,
            [new WorkOrderServiceLine(Guid.NewGuid(), "Revisão", 1, 125m)],
            [], null, DateTime.UtcNow, "sp-centro");
        var reservationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        order.AcceptBudgetReady(Guid.NewGuid(), 1, DateTime.UtcNow);
        order.ApproveBudgetForPayment(DateTime.UtcNow, reservationId);
        order.ConfirmStockReservation(DateTime.UtcNow, paymentId);
        order.ConfirmPayment(DateTime.UtcNow);
        order.StartExecutionAfterPayment(DateTime.UtcNow);
        db.AddRange(customer, vehicle, order);
        await db.SaveChangesAsync();
        var rejected = new IntegrationEnvelope<ExecutionRejectedPayload>(Guid.NewGuid(),
            "ExecutionRejected", 1, order.Id, Guid.NewGuid(), order.Id, order.BranchId,
            DateTime.UtcNow, new ExecutionRejectedPayload(order.Id, reservationId,
                paymentId, "Peça incompatível"));
        var body = JsonSerializer.Serialize(rejected,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new ExecutionRejectedHandler(db);

        Assert.True(await handler.HandleAsync(body, CancellationToken.None));
        Assert.False(await handler.HandleAsync(body, CancellationToken.None));
        Assert.Equal(WorkOrderProcessState.ManualReview, order.ProcessState);
        Assert.Equal(WorkOrderStatus.InExecution, order.Status);
        Assert.Equal(WorkOrderProcessState.ManualReview,
            order.ToDetailResponse().ProcessState);
        Assert.Equal(WorkOrderProcessState.ManualReview,
            order.ToStatusResponse().ProcessState);
        Assert.Null(order.FinalizedAtUtc);
        Assert.Throws<DomainException>(() => order.Finalize(DateTime.UtcNow));
        Assert.Single(await db.InboxMessages.ToListAsync());
    }
}
