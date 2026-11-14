using System.Net;
using System.Text;
using System.Text.Json;
using Oficina.Billing;
using Xunit;

namespace Oficina.Billing.Tests;

public sealed class MercadoPagoQrClientTests
{
    [Fact]
    public async Task CreateUsesStableIdempotencyAndExactCents()
    {
        var payment = new PaymentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            1, "sp-centro", 12545, DateTime.UtcNow);
        var reference = $"pay_{payment.Id:N}";
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            Assert.Equal(payment.Id.ToString("D"),
                request.Headers.GetValues("X-Idempotency-Key").Single());
            Assert.Equal("/v1/orders", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("125.45", body.RootElement.GetProperty("total_amount").GetString());
            Assert.Equal("125.45", body.RootElement.GetProperty("transactions")
                .GetProperty("payments")[0].GetProperty("amount").GetString());
            Assert.Equal("dynamic", body.RootElement.GetProperty("config")
                .GetProperty("qr").GetProperty("mode").GetString());
            Assert.Equal("test-pos", body.RootElement.GetProperty("config")
                .GetProperty("qr").GetProperty("external_pos_id").GetString());
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    id = "ORDTEST", status = "created", external_reference = reference,
                    total_amount = "125.45", type_response = new { qr_data = "qr-test-data" }
                }), Encoding.UTF8, "application/json")
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var client = new MercadoPagoQrClient(http, new MercadoPagoQrOptions("test-token", "test-pos"));

        var order = await client.CreateAsync(payment, CancellationToken.None);

        Assert.Equal("ORDTEST", order.Id);
        Assert.Equal("qr-test-data", order.QrData);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
