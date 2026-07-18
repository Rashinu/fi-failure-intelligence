namespace FI.Domain.AiAnalysis;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 16.9. AiIncidentAnalysis'ten farkli:
/// basarisiz denemeler DAHIL her cagriyi tutar - teknik gozlemlenebilirlik amaclidir.
/// </summary>
public class AiAnalysisLog
{
    public Guid Id { get; private set; }
    public Guid IncidentId { get; private set; }
    public Guid PromptVersionId { get; private set; }
    public string ModelVersion { get; private set; } = default!;
    public bool ParseSuccess { get; private set; }
    public bool SchemaEchoMismatch { get; private set; }
    public double? Confidence { get; private set; }
    public bool OutOfEvidenceClaimsDetected { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public long LatencyMs { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AiAnalysisLog() { }

    public static AiAnalysisLog Create(
        Guid incidentId,
        Guid promptVersionId,
        string modelVersion,
        bool parseSuccess,
        bool schemaEchoMismatch,
        double? confidence,
        bool outOfEvidenceClaimsDetected,
        int? inputTokens,
        int? outputTokens,
        long latencyMs,
        string? errorMessage)
    {
        return new AiAnalysisLog
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            PromptVersionId = promptVersionId,
            ModelVersion = modelVersion,
            ParseSuccess = parseSuccess,
            SchemaEchoMismatch = schemaEchoMismatch,
            Confidence = confidence,
            OutOfEvidenceClaimsDetected = outOfEvidenceClaimsDetected,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            LatencyMs = latencyMs,
            ErrorMessage = errorMessage,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
