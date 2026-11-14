namespace OficinaMvp.Application.Contracts;

public sealed record PaymentConfirmedPayload(Guid PaymentAttemptId, string ProviderOrderId,
    long TotalCents, string Currency);
public sealed record PaymentRejectedPayload(Guid PaymentAttemptId, string ProviderOrderId,
    long TotalCents, string Currency, string Reason);
public sealed record ReleaseStockRequestedPayload(Guid ReservationId, string Reason);
public sealed record StockReleasedPayload(Guid ReservationId);
public sealed record ConsumeStockRequestedPayload(Guid ReservationId);
public sealed record StockConsumedPayload(Guid ReservationId);
