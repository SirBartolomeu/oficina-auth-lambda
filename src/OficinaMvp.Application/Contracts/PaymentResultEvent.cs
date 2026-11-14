namespace OficinaMvp.Application.Contracts;

public sealed record PaymentConfirmedPayload(Guid PaymentAttemptId, string ProviderOrderId,
    long TotalCents, string Currency);
