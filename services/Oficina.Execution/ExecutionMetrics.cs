using System.Diagnostics.Metrics;

namespace Oficina.Execution;

internal static class ExecutionMetrics
{
    public const string MeterName = "Oficina.Execution.Business";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> AppliedCommands = Meter.CreateCounter<long>(
        "execution_commands_applied", "{command}");

    public static void RecordAppliedCommand(string eventType) =>
        AppliedCommands.Add(1, new KeyValuePair<string, object?>("event.type", eventType));
}
