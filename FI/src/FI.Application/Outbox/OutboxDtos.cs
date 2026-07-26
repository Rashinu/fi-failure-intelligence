namespace FI.Application.Outbox;

public sealed record OutboxMessageResponse(
    Guid Id,
    string MessageType,
    string Status,
    int FailureCount,
    DateTimeOffset? LastFailedAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt);
