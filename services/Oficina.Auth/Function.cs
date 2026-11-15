using System.Text;
using System.Text.Json;

namespace Oficina.Auth;

// Handler para API Gateway HTTP API (payload v2), sem dependência do SDK AWS.
public sealed class Function
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };
    private readonly CustomerTokenService _service;

    public Function() : this(new CustomerTokenService(SharedHttp, new AuthOptions(
        new Uri(Required("AUTH_LOOKUP_URL")), Required("AUTH_LOOKUP_TOKEN"),
        Required("AUTH_JWT_KEY"), Required("AUTH_JWT_ISSUER"),
        Required("AUTH_JWT_AUDIENCE")))) { }

    public Function(CustomerTokenService service) => _service = service;

    public async Task<Stream> FunctionHandler(Stream input)
    {
        try
        {
            using var gatewayEvent = await JsonDocument.ParseAsync(input);
            var root = gatewayEvent.RootElement;
            var method = root.GetProperty("requestContext")
                .GetProperty("http").GetProperty("method").GetString();
            if (method != "POST") return Response(405, new { message = "Método não permitido." });
            var body = root.GetProperty("body").GetString();
            if (body is null || body.Length > 1024)
                return Response(400, new { message = "Requisição inválida." });
            if (root.TryGetProperty("isBase64Encoded", out var encoded) && encoded.GetBoolean())
                body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
            if (body.Length > 1024)
                return Response(400, new { message = "Requisição inválida." });
            using var payload = JsonDocument.Parse(body);
            var cpf = payload.RootElement.TryGetProperty("cpf", out var value) &&
                      value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var token = await _service.AuthenticateAsync(cpf, CancellationToken.None);
            return token is null
                ? Response(401, new { message = "Credenciais inválidas." })
                : Response(200, token);
        }
        catch (Exception error) when (error is JsonException or FormatException or KeyNotFoundException)
        {
            return Response(400, new { message = "Requisição inválida." });
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException
            or InvalidOperationException)
        {
            return Response(503, new { message = "Autenticação indisponível." });
        }
    }

    private static Stream Response(int statusCode, object payload)
    {
        var body = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var response = JsonSerializer.Serialize(new
        {
            statusCode,
            headers = new Dictionary<string, string>
            {
                ["content-type"] = "application/json",
                ["cache-control"] = "no-store"
            },
            body
        });
        return new MemoryStream(Encoding.UTF8.GetBytes(response));
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} não configurado.");
}
