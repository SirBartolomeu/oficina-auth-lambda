using System.Diagnostics.Metrics;

namespace OficinaMvp.Application.Observability;

public static class WorkOrderMetrics
{
    public const string MeterName = "Oficina.WorkOrders";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Opened = Meter.CreateCounter<long>("work_orders_opened", "{order}");
    private static readonly Counter<long> SagaTransitions = Meter.CreateCounter<long>("work_order_saga_transitions", "{transition}");

    public static void RecordOpened() => Opened.Add(1);

    public static void RecordSagaTransition(string eventType) =>
        SagaTransitions.Add(1, new KeyValuePair<string, object?>("event.type", eventType));
}
