namespace FI.Domain.Ingestion;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 16.2.
/// KeyHash her zaman HMAC-SHA256+pepper ile hesaplanır; ham key asla saklanmaz.
/// </summary>
public class ApiKey
{
    public Guid Id { get; private set; }
    public Guid IntegrationId { get; private set; }
    public string KeyPrefix { get; private set; } = default!;
    public string KeyHash { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastRotatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public long UsageCount { get; private set; }

    public bool IsActive => RevokedAt is null;

    private ApiKey() { }

    public static ApiKey Create(Guid integrationId, string keyPrefix, string keyHash)
    {
        if (string.IsNullOrWhiteSpace(keyPrefix)) throw new ArgumentException("KeyPrefix zorunludur.", nameof(keyPrefix));
        if (string.IsNullOrWhiteSpace(keyHash)) throw new ArgumentException("KeyHash zorunludur.", nameof(keyHash));

        return new ApiKey
        {
            Id = Guid.NewGuid(),
            IntegrationId = integrationId,
            KeyPrefix = keyPrefix,
            KeyHash = keyHash,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Revoke() => RevokedAt = DateTimeOffset.UtcNow;

    /// <summary>Bkz. Bölüm 33.4 — rotasyon anında revoke edilmez; grace period sonunda ayrı bir job tarafından revoke edilir.</summary>
    public void MarkRotated(DateTimeOffset rotatedAt) => LastRotatedAt = rotatedAt;

    public void RecordUsage()
    {
        LastUsedAt = DateTimeOffset.UtcNow;
        UsageCount++;
    }
}
