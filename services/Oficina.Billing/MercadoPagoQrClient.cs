using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Oficina.Billing;

public sealed record MercadoPagoQrOptions(string AccessToken, string ExternalPosId);
public sealed record MercadoPagoQrOrder(string Id, string QrData, string Status);
public sealed record MercadoPagoOrderStatus(string Id, string ExternalReference, string Status,
    string Currency, long TotalCents, long PaidCents, string? PaymentStatus);

public sealed class MercadoPagoQrClient(HttpClient http, MercadoPagoQrOptions options)
{
    public async Task<MercadoPagoQrOrder> CreateAsync(PaymentRequest payment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.AccessToken) || string.IsNullOrWhiteSpace(options.ExternalPosId))
            throw new InvalidOperationException("Credenciais e caixa Mercado Pago são obrigatórios.");
        if (payment.Id == Guid.Empty || payment.TotalCents <= 0)
            throw new ArgumentException("Tentativa de pagamento inválida.");

        var amount = (payment.TotalCents / 100m).ToString("F2", CultureInfo.InvariantCulture);
        var reference = $"pay_{payment.Id:N}";
        var body = new
        {
            type = "qr",
            total_amount = amount,
            description = "Ordem de servico",
            external_reference = reference,
            expiration_time = "PT15M",
            config = new { qr = new { external_pos_id = options.ExternalPosId, mode = "dynamic" } },
            transactions = new { payments = new[] { new { amount } } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/orders")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        request.Headers.TryAddWithoutValidation("X-Idempotency-Key", payment.Id.ToString("D"));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Mercado Pago recusou a criação da order.", null, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = result.RootElement;
        var id = root.GetProperty("id").GetString();
        var status = root.GetProperty("status").GetString();
        var returnedReference = root.GetProperty("external_reference").GetString();
        var returnedAmount = root.GetProperty("total_amount").GetString();
        var qrData = root.GetProperty("type_response").GetProperty("qr_data").GetString();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(qrData) ||
            returnedReference != reference || status != "created" ||
            !decimal.TryParse(returnedAmount, NumberStyles.Number, CultureInfo.InvariantCulture,
                out var parsedAmount) || parsedAmount != payment.TotalCents / 100m)
            throw new InvalidOperationException("Resposta da order não corresponde à tentativa de pagamento.");
        return new MercadoPagoQrOrder(id, qrData, status);
    }

    public async Task<MercadoPagoOrderStatus> GetAsync(string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId) || orderId.Length > 64 ||
            !orderId.StartsWith("ORD", StringComparison.Ordinal) ||
            orderId.Any(ch => !char.IsLetterOrDigit(ch)))
            throw new ArgumentException("Identificador da order inválido.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"v1/orders/{Uri.EscapeDataString(orderId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Falha ao consultar order no Mercado Pago.", null,
                response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = result.RootElement;
        var returnedId = root.GetProperty("id").GetString();
        var reference = root.GetProperty("external_reference").GetString();
        var status = root.GetProperty("status").GetString();
        var currency = root.GetProperty("currency").GetString();
        var total = ParseCents(root.GetProperty("total_amount").GetString());
        var payments = root.GetProperty("transactions").GetProperty("payments");
        if (returnedId != orderId || string.IsNullOrWhiteSpace(reference) ||
            string.IsNullOrWhiteSpace(status) || currency != "BRL" ||
            payments.ValueKind != JsonValueKind.Array || payments.GetArrayLength() != 1)
            throw new InvalidOperationException("Resposta da consulta de order inválida.");
        var payment = payments[0];
        var paymentStatus = payment.GetProperty("status").GetString();
        var paidCents = payment.TryGetProperty("paid_amount", out var paid)
            ? ParseCents(paid.GetString()) : 0;
        return new MercadoPagoOrderStatus(returnedId, reference, status, currency,
            total, paidCents, paymentStatus);
    }

    private static long ParseCents(string? value)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ||
            amount < 0 || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("Valor monetário inválido na resposta da order.");
        return checked((long)(amount * 100m));
    }
}
