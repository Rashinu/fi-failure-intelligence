namespace FI.Application.Integrations;

public sealed record CreateIntegrationRequest(
    string Name,
    string Provider,
    string Environment,
    string Owner,
    string? EndpointUrl,
    string? BusinessCriticality);

public sealed record UpdateIntegrationRequest(
    string Name,
    string Owner,
    string? EndpointUrl,
    string? BusinessCriticality);

public sealed record IntegrationResponse(
    Guid Id,
    string Name,
    string Provider,
    string Environment,
    string Owner,
    string? EndpointUrl,
    string BusinessCriticality,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateIntegrationResponse(
    Guid IntegrationId,
    string ApiKey,
    string WebhookSecret);

public sealed record RotateApiKeyResponse(Guid IntegrationId, string ApiKey);

public sealed record RotateWebhookSecretResponse(Guid IntegrationId, string WebhookSecret);
