namespace FI.Domain.Ingestion;

public enum IntegrationEventType
{
    ApiCall,
    WebhookIn,
    WebhookOut
}

/// <summary>
/// Immutable, append-only. Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 16.3.
/// Category, ingestion sırasında değil, ClassifyJob (M3) tarafından sonradan doldurulur.
/// </summary>
public class IntegrationEvent
{
    public Guid Id { get; private set; }
    public Guid IntegrationId { get; private set; }
    public IntegrationEventType EventType { get; private set; }
    public int StatusCode { get; private set; }
    public string? Category { get; private set; }
    public string? RequestRedacted { get; private set; }
    public string? ResponseRedacted { get; private set; }
    public int? LatencyMs { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public Guid? ApiKeyId { get; private set; }
    public bool? IsSignatureVerified { get; private set; }
    public int PayloadSizeBytes { get; private set; }
    public bool IsTruncated { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }

    private IntegrationEvent() { }

    public static IntegrationEvent Create(
        Guid integrationId,
        IntegrationEventType eventType,
        int statusCode,
        string? requestRedacted,
        string? responseRedacted,
        int? latencyMs,
        Guid correlationId,
        string? idempotencyKey,
        Guid? apiKeyId,
        bool? isSignatureVerified,
        int payloadSizeBytes,
        bool isTruncated,
        DateTimeOffset occurredAt)
    {
        if (statusCode is < 100 or > 599)
            throw new ArgumentOutOfRangeException(nameof(statusCode), "statusCode 100-599 aralığında olmalıdır.");

        return new IntegrationEvent
        {
            Id = Guid.NewGuid(),
            IntegrationId = integrationId,
            EventType = eventType,
            StatusCode = statusCode,
            RequestRedacted = requestRedacted,
            ResponseRedacted = responseRedacted,
            LatencyMs = latencyMs,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            ApiKeyId = apiKeyId,
            IsSignatureVerified = isSignatureVerified,
            PayloadSizeBytes = payloadSizeBytes,
            IsTruncated = isTruncated,
            OccurredAt = occurredAt,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>ClassifyJobHandler tarafından, deterministik rule engine sonucu ile bir kez set edilir.</summary>
    public void SetCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Category zorunludur.", nameof(category));
        Category = category;
    }
}
