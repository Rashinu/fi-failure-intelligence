namespace FI.Domain.Audit;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 16.11, 33.6. Append-only — kullanıcı
/// tetikli aksiyonlar için Serilog'dan (yüksek hacim, kısa retention) ayrı, iş/uyumluluk amaçlı
/// bir kayıt. Bu implementasyonda ayrıca <see cref="Incidents.EvidenceSourceType.ConfigChange"/>
/// evidence kaynağının veri kaynağıdır (Bölüm 23).
/// </summary>
public class AuditLog
{
    public Guid Id { get; private set; }
    public AuditActorType ActorType { get; private set; }
    public string? ActorId { get; private set; }
    public string Action { get; private set; } = default!;
    public string EntityType { get; private set; } = default!;
    public Guid? EntityId { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public string? Changes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        AuditActorType actorType,
        string? actorId,
        string action,
        string entityType,
        Guid? entityId,
        Guid? correlationId,
        string? changes)
    {
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action zorunludur.", nameof(action));
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("EntityType zorunludur.", nameof(entityType));

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            CorrelationId = correlationId,
            Changes = changes,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
