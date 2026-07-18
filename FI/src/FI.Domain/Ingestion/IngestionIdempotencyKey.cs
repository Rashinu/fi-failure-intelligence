namespace FI.Domain.Ingestion;

public enum IngestionResourceType
{
    Event,
    Deployment
}

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 28.1 — iki katmanlı idempotency'nin
/// client-supplied Idempotency-Key katmanı. Content-hash fallback aynı tabloyu, sentetik bir
/// key (SHA256 imzası) ile kullanır.
/// </summary>
public class IngestionIdempotencyKey
{
    public Guid Id { get; private set; }
    public Guid IntegrationId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string RequestHash { get; private set; } = default!;
    public IngestionResourceType ResourceType { get; private set; }
    public Guid ResourceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private IngestionIdempotencyKey() { }

    public static IngestionIdempotencyKey Create(
        Guid integrationId,
        string idempotencyKey,
        string requestHash,
        IngestionResourceType resourceType,
        Guid resourceId)
    {
        return new IngestionIdempotencyKey
        {
            Id = Guid.NewGuid(),
            IntegrationId = integrationId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            ResourceType = resourceType,
            ResourceId = resourceId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
