using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Oficina.Execution;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Entities;
using Xunit;

namespace Oficina.Execution.Tests;

public sealed class ReserveStockRequestedHandlerTests
{
    [Theory]
    [InlineData(2, 3, "StockReserved")]
    [InlineData(6, 5, "StockReservationRejected")]
    public async Task RequestProducesOneResultAndNeverPartiallyChangesStock(
        int requested, int expectedStock, string resultType)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ExecutionDbContext(
            new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var part = new PartSupply("Filtro", 10m, 5);
        db.PartSupplies.Add(part);
        await db.SaveChangesAsync();
        var command = new IntegrationEnvelope<ReserveStockRequestedPayload>(
            Guid.NewGuid(), "ReserveStockRequested", 1, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "sp-centro", DateTime.UtcNow,
            new ReserveStockRequestedPayload(Guid.NewGuid(), 1,
                [new CatalogItemRequest(part.Id, requested)], DateTime.UtcNow.AddMinutes(15)));
        var body = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new ReserveStockRequestedHandler(db, new ExecutionService(db));

        Assert.True(await handler.HandleAsync(body, CancellationToken.None));
        Assert.False(await handler.HandleAsync(body, CancellationToken.None));

        Assert.Equal(expectedStock, (await db.PartSupplies.SingleAsync()).StockQuantity);
        Assert.Single(await db.InboxMessages.ToListAsync());
        var response = Assert.Single(await db.OutboxMessages.ToListAsync());
        Assert.Equal(resultType, response.EventType);
        Assert.Equal(resultType == "StockReserved" ? 1 : 0,
            await db.StockReservations.CountAsync());
    }

    [Fact]
    public async Task ReleaseCommandReturnsStockOnceAndPublishesConfirmation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ExecutionDbContext(
            new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var part = new PartSupply("Filtro", 10m, 5);
        db.PartSupplies.Add(part);
        await db.SaveChangesAsync();
        var orderId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var service = new ExecutionService(db);
        await service.ReserveAsync(new ReserveStockRequest(reservationId, orderId,
            [new CatalogItemRequest(part.Id, 2)]), CancellationToken.None);
        var command = new IntegrationEnvelope<ReleaseStockRequestedPayload>(
            Guid.NewGuid(), "ReleaseStockRequested", 1, orderId, Guid.NewGuid(),
            orderId, "sp-centro", DateTime.UtcNow,
            new ReleaseStockRequestedPayload(reservationId, "expired"));
        var body = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var handler = new ReleaseStockRequestedHandler(db, service);

        Assert.True(await handler.HandleAsync(body, CancellationToken.None));
        Assert.False(await handler.HandleAsync(body, CancellationToken.None));
        Assert.Equal(5, (await db.PartSupplies.SingleAsync()).StockQuantity);
        Assert.Equal(ReservationStatus.Released,
            (await db.StockReservations.SingleAsync()).Status);
        Assert.Single(await db.OutboxMessages.ToListAsync());
        Assert.Equal("StockReleased", (await db.OutboxMessages.SingleAsync()).EventType);
        Assert.Single(await db.InboxMessages.ToListAsync());
    }
}
