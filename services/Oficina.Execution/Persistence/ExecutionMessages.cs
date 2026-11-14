namespace Oficina.Execution.Persistence;

public sealed class ExecutionInboxMessage
{
    private ExecutionInboxMessage() { }
    public ExecutionInboxMessage(Guid eventId, string eventType, DateTime receivedAtUtc)
    {
        EventId = eventId;
        EventType = eventType;
        ReceivedAtUtc = receivedAtUtc;
    }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }
}

public sealed class ExecutionOutboxMessage
{
    private ExecutionOutboxMessage() { }
    public ExecutionOutboxMessage(Guid eventId, string eventType, string body, DateTime occurredAtUtc)
    {
        EventId = eventId;
        EventType = eventType;
        Body = body;
        OccurredAtUtc = occurredAtUtc;
    }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public void MarkPublished(DateTime atUtc) => PublishedAtUtc = atUtc;
}
