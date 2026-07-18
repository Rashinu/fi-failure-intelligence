namespace FI.Domain.AiAnalysis;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 16.8. Append-only, versiyonlu,
/// business-facing sonuc. Bir incident icin en guncel analiz IsLatest=true olandir; reanalyze
/// yeni bir satir ekler, eskisini IsLatest=false yapar (Bolum 18.6).
/// </summary>
public class AiIncidentAnalysis
{
    public Guid Id { get; private set; }
    public Guid IncidentId { get; private set; }
    public Guid PromptVersionId { get; private set; }
    public string ModelVersion { get; private set; } = default!;
    public string IncidentTitle { get; private set; } = default!;
    public string ProbableRootCause { get; private set; } = default!;
    public string EvidenceJson { get; private set; } = default!;
    public string EvidenceRefsJson { get; private set; } = default!;
    public string RecommendedActionsJson { get; private set; } = default!;
    public double Confidence { get; private set; }
    public bool NeedsHumanReview { get; private set; }
    public bool OutOfEvidenceClaimsDetected { get; private set; }
    public bool IsLatest { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AiIncidentAnalysis() { }

    public static AiIncidentAnalysis Create(
        Guid incidentId,
        Guid promptVersionId,
        string modelVersion,
        string incidentTitle,
        string probableRootCause,
        string evidenceJson,
        string evidenceRefsJson,
        string recommendedActionsJson,
        double confidence,
        bool needsHumanReview,
        bool outOfEvidenceClaimsDetected)
    {
        return new AiIncidentAnalysis
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            PromptVersionId = promptVersionId,
            ModelVersion = modelVersion,
            IncidentTitle = incidentTitle,
            ProbableRootCause = probableRootCause,
            EvidenceJson = evidenceJson,
            EvidenceRefsJson = evidenceRefsJson,
            RecommendedActionsJson = recommendedActionsJson,
            Confidence = confidence,
            NeedsHumanReview = needsHumanReview,
            OutOfEvidenceClaimsDetected = outOfEvidenceClaimsDetected,
            IsLatest = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkSuperseded() => IsLatest = false;
}
