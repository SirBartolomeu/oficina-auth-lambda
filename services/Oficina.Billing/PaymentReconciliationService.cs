namespace Oficina.Billing;

public sealed class PaymentReconciliationService(BillingRepository repository,
    MercadoPagoQrClient mercadoPago)
{
    public async Task<bool> ReconcileAsync(string providerOrderId, CancellationToken cancellationToken)
    {
        var order = await mercadoPago.GetAsync(providerOrderId, cancellationToken);
        if (!order.ExternalReference.StartsWith("pay_", StringComparison.Ordinal) ||
            !Guid.TryParseExact(order.ExternalReference[4..], "N", out var paymentAttemptId))
            throw new InvalidOperationException("Referência externa da order inválida.");
        var payment = await repository.GetPaymentRequestAsync(paymentAttemptId, cancellationToken)
            ?? throw new InvalidOperationException("Tentativa da order não encontrada.");
        var attempt = await repository.GetPaymentAttemptAsync(paymentAttemptId, cancellationToken)
            ?? throw new InvalidOperationException("Estado da tentativa não encontrado.");
        if (attempt.ProviderOrderId != order.Id || payment.TotalCents != order.TotalCents ||
            order.Currency != "BRL")
            throw new InvalidOperationException("Order não corresponde à tentativa persistida.");
        if (order.Status == "created") return false;
        if (order.Status == "processed" && order.PaymentStatus == "processed" &&
            order.PaidCents == payment.TotalCents)
            return await repository.ConfirmPaymentAsync(payment, order.Id, cancellationToken);
        if (order.Status is "canceled" or "expired" or "refunded" ||
            order.Status == "processed")
        {
            await repository.MarkCreatedPaymentManualReviewAsync(paymentAttemptId, cancellationToken);
            return false;
        }
        throw new InvalidOperationException("Status de order não reconhecido.");
    }
}
