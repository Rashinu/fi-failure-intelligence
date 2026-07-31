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

    /// <summary>
    /// Bkz. docs/CTO_REVIEW_ANALYSIS.md M18 (Incident Intelligence) — "kaç müşteri etkilendi"
    /// sorusuna cevap verebilmek için, provider'ın kendi müşteri/kullanıcı kimliği (ör. Stripe
    /// customer id). Opsiyoneldir — her provider/event bunu taşımaz; PII değildir (opak bir
    /// referans kimliği, ad/e-posta değil), bu yüzden Bölüm 33.3 redaction'ının kapsamı dışındadır.
    /// </summary>
    public string? AffectedCustomerRef { get; private set; }

    /// <summary>
    /// Bkz. docs/CTO_REVIEW_ANALYSIS.md Teknik Borç TD3 - önceden "bu event'ler bu incident'a mı
    /// ait" sorusu, üç ayrı çağrı noktasında (IncidentsController, AiAnalysisJobHandler,
    /// Detail.cshtml.cs) bağımsızca IntegrationId+Category-string+zaman-penceresi sorgusuyla
    /// yeniden türetiliyordu - hem bir bakım/sapma riski hem de (D2'nin sabit-kodlanmış 15
    /// dakikalık payı yüzünden) hâlâ eksik-sayım ihtimali taşıyordu. ClassifyJobHandler artık bu
    /// FK'yı doğrudan sınıflandırma anında set ediyor; sorgu değil, gerçek bir ilişki.
    /// </summary>
    public Guid? IncidentId { get; private set; }

    /// <summary>
    /// Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md - P0-A (Business Operation Identity).
    /// FI önceden yalnızca IntegrationEvent → Incident modelliyordu; ama ürün sorusu "kaç teknik
    /// event" değil "kaç iş operasyonu" idi. Bir tekil PaymentSync operasyonu (retry + webhook
    /// callback dahil) birden fazla teknik event üretebilir - bunlar hâlâ TEK bir başarısız iş
    /// operasyonunu temsil eder. Opsiyoneldir - her provider/event bunu taşımaz; null "bilinmiyor"
    /// anlamına gelir, "sıfır operasyon" değil (bkz. IncidentsController'daki OperationCoverage).
    /// </summary>
    public string? OperationRef { get; private set; }

    /// <summary>İş operasyonunun türü (ör. "PaymentSync"). Yalnızca görüntüleme amaçlı; sayım
    /// mantığı yalnızca <see cref="OperationRef"/>'e dayanır.</summary>
    public string? OperationType { get; private set; }

    /// <summary>İlişkili iş kaydının opak referansı (ör. "subscription-18372"). AffectedCustomerRef
    /// gibi PII değildir, redaction kapsamı dışındadır.</summary>
    public string? BusinessRecordRef { get; private set; }

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
        DateTimeOffset occurredAt,
        string? affectedCustomerRef = null,
        string? operationRef = null,
        string? operationType = null,
        string? businessRecordRef = null)
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
            ReceivedAt = DateTimeOffset.UtcNow,
            AffectedCustomerRef = affectedCustomerRef,
            OperationRef = operationRef,
            OperationType = operationType,
            BusinessRecordRef = businessRecordRef
        };
    }

    /// <summary>ClassifyJobHandler tarafından, deterministik rule engine sonucu ile bir kez set edilir.</summary>
    public void SetCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Category zorunludur.", nameof(category));
        Category = category;
    }

    /// <summary>Bkz. TD3 - ClassifyJobHandler tarafından, event hangi incident'a bağlandıysa (yeni
    /// açılan veya var olan) o incident'ın Id'siyle bir kez set edilir.</summary>
    public void AssignToIncident(Guid incidentId) => IncidentId = incidentId;
}
