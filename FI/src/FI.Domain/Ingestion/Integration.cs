namespace FI.Domain.Ingestion;

/// <summary>
/// Aggregate root — kayıtlı, izlenen üçüncü taraf sistem bağlantısı.
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 16.1.
/// </summary>
public class Integration
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string Provider { get; private set; } = default!;
    public string Environment { get; private set; } = default!;
    public string Owner { get; private set; } = default!;
    public string? EndpointUrl { get; private set; }
    public BusinessCriticality BusinessCriticality { get; private set; } = BusinessCriticality.Medium;
    public IntegrationStatus Status { get; private set; } = IntegrationStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<ApiKey> _apiKeys = new();
    public IReadOnlyCollection<ApiKey> ApiKeys => _apiKeys.AsReadOnly();

    private Integration() { }

    public static Integration Create(string name, string provider, string environment, string owner, string? endpointUrl, BusinessCriticality businessCriticality)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name zorunludur.", nameof(name));
        if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("Provider zorunludur.", nameof(provider));
        if (string.IsNullOrWhiteSpace(environment)) throw new ArgumentException("Environment zorunludur.", nameof(environment));
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner zorunludur.", nameof(owner));

        var now = DateTimeOffset.UtcNow;
        return new Integration
        {
            Id = Guid.NewGuid(),
            Name = name,
            Provider = provider,
            Environment = environment,
            Owner = owner,
            EndpointUrl = endpointUrl,
            BusinessCriticality = businessCriticality,
            Status = IntegrationStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string name, string? endpointUrl, string owner, BusinessCriticality businessCriticality)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name zorunludur.", nameof(name));
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner zorunludur.", nameof(owner));

        Name = name;
        EndpointUrl = endpointUrl;
        Owner = owner;
        BusinessCriticality = businessCriticality;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Pause() => SetStatus(IntegrationStatus.Paused);
    public void Reactivate() => SetStatus(IntegrationStatus.Active);
    public void Archive() => SetStatus(IntegrationStatus.Archived);

    private void SetStatus(IntegrationStatus status)
    {
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public ApiKey IssueApiKey(string keyPrefix, string keyHash)
    {
        var apiKey = ApiKey.Create(Id, keyPrefix, keyHash);
        _apiKeys.Add(apiKey);
        return apiKey;
    }
}
