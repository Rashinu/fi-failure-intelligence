namespace FI.Application.Incidents;

public sealed record IncidentListItemResponse(
    Guid Id,
    string IntegrationName,
    string Category,
    string Severity,
    string Status,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int EventCount,
    string SuggestedAction);

public sealed record IncidentListResponse(
    IReadOnlyList<IncidentListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record IncidentEvidenceResponse(
    Guid Id,
    string SourceType,
    string Summary,
    DateTimeOffset? WindowStart,
    DateTimeOffset? WindowEnd,
    DateTimeOffset CollectedAt);

public sealed record AiAnalysisResponse(
    Guid Id,
    string IncidentTitle,
    string ProbableRootCause,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> RecommendedActions,
    double Confidence,
    bool NeedsHumanReview,
    string ModelVersion,
    DateTimeOffset CreatedAt);

public sealed record IncidentDetailResponse(
    Guid Id,
    Guid IntegrationId,
    string IntegrationName,
    string Category,
    string Severity,
    string Status,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int EventCount,
    int ReopenCount,
    string Fingerprint,
    string SuggestedAction,
    int? AffectedCustomerCount,
    string CustomerCoverage,
    int? KnownOperationCount,
    string OperationCoverage,
    ResolutionResponse? Resolution,
    IReadOnlyList<IncidentEvidenceResponse> Evidence,
    AiAnalysisResponse? LatestAnalysis);

/// <summary>
/// Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-B. Yalnızca incident Resolved durumundayken
/// dolu döner (null = henüz resolve edilmemiş).
/// </summary>
public sealed record ResolutionResponse(
    DateTimeOffset ResolvedAt,
    string? ResolvedBy,
    string? ResolutionNote);

public sealed record ResolveIncidentRequest(string? ResolvedBy, string? Note);
