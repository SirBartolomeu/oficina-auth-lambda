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
        if (attempt.Status == "Refunded" && order.Status == "refunded") return false;
        if (order.Status == "processed" && order.PaymentStatus == "processed" &&
            order.PaidCents == payment.TotalCents)
        {
            if (attempt.Status is "RefundRequested" or "RefundSubmitting") return false;
            return await repository.ConfirmPaymentAsync(payment, order.Id, cancellationToken);
        }
        if (order.Status == "refunded" && order.PaymentStatus == "refunded" &&
            attempt.RefundRequestId.HasValue &&
            (attempt.Status is "RefundRequested" or "RefundSubmitting"))
        {
            if (attempt.Status == "RefundRequested")
                await repository.BeginRefundSubmissionAsync(paymentAttemptId,
                    attempt.RefundRequestId.Value, cancellationToken);
            return await repository.ConfirmRefundAsync(payment,
                attempt.RefundRequestId.Value, order.Id, cancellationToken);
        }
        if ((order.Status is "canceled" or "expired") &&
            (order.PaymentStatus is "canceled" or "expired") && order.PaidCents == 0)
            return await repository.RejectPaymentAsync(payment, order.Id, order.Status,
                cancellationToken);
        if (order.Status == "refunded" ||
            order.Status == "processed")
        {
            await repository.MarkCreatedPaymentManualReviewAsync(paymentAttemptId, cancellationToken);
            return false;
        }
        throw new InvalidOperationException("Status de order não reconhecido.");
    }
}
