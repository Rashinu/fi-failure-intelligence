namespace FI.Domain.AiAnalysis;

/// <summary>Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 16.10, Bolum 18.7.</summary>
public class IncidentReview
{
    public Guid Id { get; private set; }
    public Guid IncidentId { get; private set; }
    public Guid? AiAnalysisId { get; private set; }
    public IncidentReviewDecision Decision { get; private set; }
    public string? FinalContentJson { get; private set; }
    public string? ReviewerNotes { get; private set; }
    public DateTimeOffset ReviewedAt { get; private set; }

    private IncidentReview() { }

    public static IncidentReview Create(
        Guid incidentId,
        Guid? aiAnalysisId,
        IncidentReviewDecision decision,
        string? finalContentJson,
        string? reviewerNotes)
    {
        return new IncidentReview
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            AiAnalysisId = aiAnalysisId,
            Decision = decision,
            FinalContentJson = finalContentJson,
            ReviewerNotes = reviewerNotes,
            ReviewedAt = DateTimeOffset.UtcNow
        };
    }
}
