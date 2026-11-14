namespace Oficina.Execution.Contracts;

public sealed record IntegrationEnvelope<T>(Guid EventId, string EventType, int SchemaVersion,
    Guid CorrelationId, Guid CausationId, Guid WorkOrderId, string? BranchId,
    DateTime OccurredAtUtc, T Payload);
public sealed record ReserveStockRequestedPayload(Guid ReservationId, int OrderVersion,
    IReadOnlyCollection<CatalogItemRequest> Parts, DateTime ExpiresAtUtc);
public sealed record StockReservationResultPayload(Guid ReservationId, int OrderVersion, string? Reason);
