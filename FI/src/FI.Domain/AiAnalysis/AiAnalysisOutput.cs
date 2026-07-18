namespace FI.Domain.AiAnalysis;

/// <summary>
/// Bkz. Bolum 25.2 - AI Output Schema. Modelin dondurdugu ham (henuz validate edilmemis) JSON'un
/// deserialize edilmis hali. promptVersion/modelVersion sistem tarafindan enjekte edilir, modele
/// deserialize ettirilmez (bu yuzden burada yer almiyor - IAiAnalysisClient sonucunda ayrica tutulur).
/// </summary>
public sealed record AiAnalysisOutput(
    string? SchemaVersion,
    string? IncidentTitle,
    string? Category,
    string? Severity,
    string? AiSeveritySuggestion,
    string? AffectedIntegration,
    int? AffectedRequests,
    string? ProbableRootCause,
    IReadOnlyList<string>? Evidence,
    IReadOnlyList<string>? EvidenceRefs,
    IReadOnlyList<string>? RecommendedActions,
    double? Confidence,
    bool? NeedsHumanReview,
    bool? OutOfEvidenceClaimsDetected);
