namespace FI.Domain.Incidents;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 16.6, Bolum 23.
/// summary alani DETERMINISTIK template ile uretilir (AI tarafindan degil) - evidence katmaninin
/// kendisinin de halusinasyona acik olmamasi icin.
/// </summary>
public class IncidentEvidence
{
    public Guid Id { get; private set; }
    public Guid IncidentId { get; private set; }
    public EvidenceSourceType SourceType { get; private set; }
    public Guid? SourceId { get; private set; }
    public string Summary { get; private set; } = default!;
    public string? StructuredData { get; private set; }
    public DateTimeOffset? WindowStart { get; private set; }
    public DateTimeOffset? WindowEnd { get; private set; }
    public DateTimeOffset CollectedAt { get; private set; }

    private IncidentEvidence() { }

    public static IncidentEvidence Create(
        Guid incidentId,
        EvidenceSourceType sourceType,
        Guid? sourceId,
        string summary,
        string? structuredData,
        DateTimeOffset? windowStart,
        DateTimeOffset? windowEnd)
    {
        if (string.IsNullOrWhiteSpace(summary)) throw new ArgumentException("Summary zorunludur.", nameof(summary));

        return new IncidentEvidence
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            SourceType = sourceType,
            SourceId = sourceId,
            Summary = summary,
            StructuredData = structuredData,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            CollectedAt = DateTimeOffset.UtcNow
        };
    }
}
