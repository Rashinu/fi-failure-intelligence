namespace FI.Application.Incidents;

public sealed record IncidentListItemResponse(
    Guid Id,
    string IntegrationName,
    string Category,
    string Severity,
    string Status,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int EventCount);

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
    IReadOnlyList<IncidentEvidenceResponse> Evidence);
