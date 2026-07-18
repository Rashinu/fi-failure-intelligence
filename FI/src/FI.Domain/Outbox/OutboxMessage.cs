namespace FI.Domain.Outbox;

public enum OutboxMessageType
{
    ClassifyJob,
    FingerprintJob,
    EvidenceCollectorJob,
    AiAnalysisJob,
    NotificationJob
}

public enum OutboxMessageStatus
{
    Pending,
    Dispatched,
    Failed
}

/// <summary>
/// Transactional outbox — raw event/deployment yazımıyla AYNI transaction'da yazılır.
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 20.3, Bölüm 16.14.
/// M2'de yalnızca kayıt yazılır; dispatcher (Hangfire recurring job) M3'te eklenir.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; private set; }
    public OutboxMessageType MessageType { get; private set; }
    public string Payload { get; private set; } = default!;
    public OutboxMessageStatus Status { get; private set; } = OutboxMessageStatus.Pending;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create(OutboxMessageType messageType, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("Payload zorunludur.", nameof(payload));

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageType = messageType,
            Payload = payload,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkDispatched()
    {
        Status = OutboxMessageStatus.Dispatched;
        DispatchedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed() => Status = OutboxMessageStatus.Failed;
}
