namespace FI.Application.Ingestion;

public sealed record IngestEventRequest(
    Guid IntegrationId,
    string Type,
    int StatusCode,
    object? Request,
    object? Response,
    int? Latency,
    DateTimeOffset OccurredAt);

public sealed record IngestEventResponse(Guid EventId, Guid CorrelationId);

public sealed record IngestDeploymentRequest(
    string Service,
    string Environment,
    string Commit,
    DateTimeOffset DeployedAt,
    IReadOnlyList<ChangedConfigEntry>? ChangedConfig);

public sealed record ChangedConfigEntry(string Key, bool Changed);

public sealed record IngestDeploymentResponse(Guid DeploymentEventId);
