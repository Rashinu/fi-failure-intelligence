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
    public int FailureCount { get; private set; }
    public DateTimeOffset? LastFailedAt { get; private set; }
    public string? LastError { get; private set; }

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

    /// <summary>
    /// Bkz. docs/CTO_REVIEW_ANALYSIS.md Due Diligence D4 - önceden bu yalnızca Status'u Failed
    /// yapıyordu, ne zaman/kaç kez/neden başarısız olduğuna dair hiçbir iz bırakmıyordu ve
    /// dispatcher'ın sorgu filtresi (Status==Pending) bu satırı bir daha asla görmüyordu.
    /// FailureCount/LastFailedAt/LastError artık admin-görünür bir uç noktada (bkz.
    /// OutboxController) gözlemlenebilir hale getiriyor.
    /// </summary>
    public void MarkFailed(string error)
    {
        Status = OutboxMessageStatus.Failed;
        FailureCount++;
        LastFailedAt = DateTimeOffset.UtcNow;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}
