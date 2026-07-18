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
