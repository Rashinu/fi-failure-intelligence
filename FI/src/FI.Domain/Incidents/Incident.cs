using FI.Domain.Classification;

namespace FI.Domain.Incidents;

/// <summary>
/// Aggregate root. Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 16.5, Bölüm 22.
/// Upsert mantığı (aynı fingerprint -> aynı incident) ClassifyJobHandler'da yönetilir; bu sınıf
/// yalnızca geçerli durum geçişlerini garanti eder.
/// </summary>
public class Incident
{
    public static readonly TimeSpan ReopenCooldown = TimeSpan.FromMinutes(30);

    public Guid Id { get; private set; }
    public Guid IntegrationId { get; private set; }
    public string Fingerprint { get; private set; } = default!;
    public int FingerprintAlgorithmVersion { get; private set; } = FingerprintCalculator.AlgorithmVersion;
    public EventCategory Category { get; private set; }
    public IncidentSeverity Severity { get; private set; }
    public IncidentStatus Status { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public DateTimeOffset FirstSeen { get; private set; }
    public DateTimeOffset LastSeen { get; private set; }
    public int EventCount { get; private set; }
    public int ReopenCount { get; private set; }
    public string? ResolutionSource { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>
    /// Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-B. <see cref="ResolutionSource"/>'dan
    /// KASITLI olarak ayrı - o, mimari dokümanda zaten tanımlı kategorik bir alan
    /// (HUMAN_MANUAL | AUTO_SILENCE | AI_APPROVED, varchar(30)), "kim/neden" değil "hangi
    /// mekanizma" sorusuna cevap verir. ResolvedBy (serbest metin aktör etiketi) ve
    /// ResolutionNote (serbest metin not) burada YENİ, ayrı kavramlardır.
    /// </summary>
    public string? ResolvedBy { get; private set; }
    public string? ResolutionNote { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Incident() { }

    public static Incident Open(Guid integrationId, string fingerprint, EventCategory category, IncidentSeverity severity, DateTimeOffset occurredAt)
    {
        var now = DateTimeOffset.UtcNow;
        return new Incident
        {
            Id = Guid.NewGuid(),
            IntegrationId = integrationId,
            Fingerprint = fingerprint,
            Category = category,
            Severity = severity,
            Status = IncidentStatus.Open,
            FirstSeen = occurredAt,
            LastSeen = occurredAt,
            EventCount = 1,
            ReopenCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Var olan, aktif (Open/Investigating/AiAnalyzed/NeedsHumanReview) bir incident'a yeni event bağlanır.</summary>
    public void RecordNewEvent(DateTimeOffset occurredAt, IncidentSeverity recalculatedSeverity)
    {
        EventCount++;
        if (occurredAt > LastSeen) LastSeen = occurredAt;
        Severity = recalculatedSeverity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Resolved/Ignored bir incident, cooldown içinde aynı fingerprint ile tekrar tetiklenirse.</summary>
    public void Reopen(DateTimeOffset occurredAt, IncidentSeverity recalculatedSeverity)
    {
        Status = IncidentStatus.Reopened;
        ReopenCount++;
        if (occurredAt > LastSeen) LastSeen = occurredAt;
        Severity = recalculatedSeverity;
        ResolvedAt = null;
        // Bkz. M19 P0-B - bir önceki resolve'un "kim/not"u, reopen sonrası artık geçerli değil;
        // ResolvedAt ile aynı anda temizlenir (ResolutionSource kategorik alanı, önceden de
        // Reopen'da temizlenmediği için burada da dokunulmadı - mevcut davranış korunuyor).
        ResolvedBy = null;
        ResolutionNote = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Resolved/Ignored bir incident, cooldown SÜRESİ GEÇTİKTEN sonra aynı fingerprint ile
    /// tekrar tetiklenirse. Not: Bölüm 22, bu durumda "yeni incident" açılmasını tarif eder, ancak
    /// Bölüm 16.5'teki UNIQUE(integration_id, fingerprint, fingerprint_algorithm_version) kısıtı
    /// aynı anahtarla ikinci bir satır açılmasını engeller. Bu, aynı satırı "yeni bir oluşum" gibi
    /// sıfırlayarak çözülür — pratikte "yeni incident" niyetiyle aynı sonucu (event_count=1'den
    /// başlayan, sıfırlanmış first_seen) verir. Bkz. ADR-014.
    /// </summary>
    public void ResetAsNewOccurrence(DateTimeOffset occurredAt, IncidentSeverity recalculatedSeverity)
    {
        Status = IncidentStatus.Open;
        FirstSeen = occurredAt;
        LastSeen = occurredAt;
        EventCount = 1;
        Severity = recalculatedSeverity;
        ResolvedAt = null;
        ResolvedBy = null;
        ResolutionNote = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-B - Resolved durumu artık gerçekten
    /// ulaşılabilir; önceden IncidentStatus.Resolved hiçbir kod yolunda set edilmiyordu. Yalnızca
    /// aktif (<see cref="IsActive"/>) bir incident resolve edilebilir - zaten Resolved veya Ignored
    /// bir incident'ı tekrar resolve etmeye çalışmak geçersiz bir geçiş sayılır (çağıran katman,
    /// ör. API'de bunu 409 Conflict'e çevirir). <see cref="Reopen"/> zaten ResolvedAt'i null'a
    /// çekiyor ve <see cref="IsWithinReopenCooldown"/> ResolvedAt'e bakıyor - yani 14:20'de resolve
    /// edilip 14:22'de eşleşen bir failure gelirse, ClassifyJobHandler'ın MEVCUT
    /// existingIncident.IsWithinReopenCooldown(now) dalı otomatik olarak Reopen()'a yönlendirir;
    /// burada paralel bir recovery lifecycle icat edilmedi, mevcut aggregate state machine tek
    /// gerçek kaynak olarak kaldı.
    /// </summary>
    public void Resolve(string? resolvedBy, string? resolutionNote)
    {
        if (!IsActive)
            throw new InvalidOperationException($"'{Status}' durumundaki bir incident resolve edilemez.");

        Status = IncidentStatus.Resolved;
        ResolvedAt = DateTimeOffset.UtcNow;
        ResolutionSource = "HUMAN_MANUAL";
        ResolvedBy = resolvedBy;
        ResolutionNote = resolutionNote;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsActive => Status is IncidentStatus.Open or IncidentStatus.Investigating or IncidentStatus.AiAnalyzed or IncidentStatus.NeedsHumanReview or IncidentStatus.Reopened;

    public bool IsWithinReopenCooldown(DateTimeOffset now) =>
        ResolvedAt is not null && now - ResolvedAt.Value < ReopenCooldown;

    /// <summary>
    /// Bkz. Bolum 19.2 - evidence collector calisirken incident INVESTIGATING durumuna gecer.
    /// Yalnizca Open/Reopened durumundan cagrilir; zaten daha ileri bir durumdaysa (AiAnalyzed,
    /// NeedsHumanReview) veya toplama daha once yapildiysa tekrar geri sarilmaz.
    /// </summary>
    public void StartInvestigating()
    {
        if (Status is IncidentStatus.Open or IncidentStatus.Reopened)
        {
            Status = IncidentStatus.Investigating;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Bolum 24.2 adim 6 - AI validasyon zincirini basariyla gecti.</summary>
    public void MarkAiAnalyzed()
    {
        Status = IncidentStatus.AiAnalyzed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Bolum 24.2/26.2 - evidence bos, AI cagrisi/parse basarisiz, sema-echo uyumsuzlugu,
    /// confidence esigin altinda veya evidence-disi iddia tespit edildi.
    /// </summary>
    public void MarkNeedsHumanReview()
    {
        Status = IncidentStatus.NeedsHumanReview;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
