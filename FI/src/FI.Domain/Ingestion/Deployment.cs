namespace FI.Domain.Ingestion;

/// <summary>
/// Immutable, append-only. Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 16.4.
/// changedConfig yalnızca {"key": "...", "changed": true} taşır — değer asla saklanmaz.
/// </summary>
public class Deployment
{
    public Guid Id { get; private set; }
    public Guid? IntegrationId { get; private set; }
    public string Service { get; private set; } = default!;
    public string Environment { get; private set; } = default!;
    public string Commit { get; private set; } = default!;
    public string? ChangedConfig { get; private set; }
    public DateTimeOffset DeployedAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }

    private Deployment() { }

    public static Deployment Create(
        Guid? integrationId,
        string service,
        string environment,
        string commit,
        string? changedConfig,
        DateTimeOffset deployedAt)
    {
        if (string.IsNullOrWhiteSpace(service)) throw new ArgumentException("Service zorunludur.", nameof(service));
        if (string.IsNullOrWhiteSpace(environment)) throw new ArgumentException("Environment zorunludur.", nameof(environment));
        if (string.IsNullOrWhiteSpace(commit)) throw new ArgumentException("Commit zorunludur.", nameof(commit));

        return new Deployment
        {
            Id = Guid.NewGuid(),
            IntegrationId = integrationId,
            Service = service,
            Environment = environment,
            Commit = commit,
            ChangedConfig = changedConfig,
            DeployedAt = deployedAt,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }
}
