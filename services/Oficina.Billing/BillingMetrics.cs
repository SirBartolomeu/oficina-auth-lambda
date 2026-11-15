using System.Diagnostics.Metrics;

namespace Oficina.Billing;

internal static class BillingMetrics
{
    public const string MeterName = "Oficina.Billing.Business";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> BudgetsCreated = Meter.CreateCounter<long>("billing_budgets_created", "{budget}");
    private static readonly Counter<long> BudgetsDecided = Meter.CreateCounter<long>("billing_budgets_decided", "{budget}");
    private static readonly Counter<long> AcceptedCommands = Meter.CreateCounter<long>("billing_commands_accepted", "{command}");

    public static void RecordBudgetCreated() => BudgetsCreated.Add(1);
    public static void RecordBudgetDecided(bool approved) =>
        BudgetsDecided.Add(1, new KeyValuePair<string, object?>("decision", approved ? "approved" : "refused"));
    public static void RecordAcceptedCommand(string eventType) =>
        AcceptedCommands.Add(1, new KeyValuePair<string, object?>("event.type", eventType));
}
