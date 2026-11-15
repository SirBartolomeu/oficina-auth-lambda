using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Oficina.Execution;

internal static class SqsTraceContext
{
    public static string Stamp(string envelopeJson)
    {
        var parent = Activity.Current?.Id;
        if (!ActivityContext.TryParse(parent, null, out _)) return envelopeJson;
        var envelope = JsonNode.Parse(envelopeJson)?.AsObject()
            ?? throw new JsonException("Envelope de integração inválido.");
        envelope["traceparent"] = parent;
        return envelope.ToJsonString();
    }

    public static string? Read(string envelopeJson)
    {
        using var document = JsonDocument.Parse(envelopeJson);
        if (!document.RootElement.TryGetProperty("traceparent", out var value) ||
            value.ValueKind != JsonValueKind.String)
            return null;
        var parent = value.GetString();
        return ActivityContext.TryParse(parent, null, out _) ? parent : null;
    }
}
