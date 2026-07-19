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

    /// <summary>
    /// Bölüm 34 — connector webhook imza doğrulaması (HMAC) için paylaşılan sır. API key'den
    /// ayrı saklanır ama kasıtlı olarak hash-DEĞİL: HMAC doğrulaması bir eşitlik karşılaştırması
    /// değil, sırrın kendisiyle imza hesaplamayı gerektirir (API key doğrulamasının aksine, tek
    /// yönlü hash'ten geri hesaplanamaz). Bu yüzden ApiKey'deki "yalnızca hash sakla" prensibi
    /// burada uygulanamaz; MVP'de düz metin saklanır (prod'da KMS/Data Protection ile şifreleme
    /// takip konusu, bkz. README Sonraki Adımlar).
    /// </summary>
    public string? WebhookSecret { get; private set; }

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

    /// <summary>
    /// Bölüm 33.4 basit sadeleştirme: dokümandaki 24 saatlik grace period (rotasyondan sonra
    /// eski key'in bir süre daha geçerli kalması) burada uygulanmadı — eski key(ler) anında
    /// revoke edilir. Grace period, zamanlanmış bir revoke job'u gerektirir (post-MVP takip
    /// konusu); MVP'de "anında rotasyon" daha basit ve öngörülebilir.
    /// </summary>
    public ApiKey RotateApiKey(string newKeyPrefix, string newKeyHash)
    {
        foreach (var key in _apiKeys.Where(k => k.IsActive))
            key.Revoke();

        return IssueApiKey(newKeyPrefix, newKeyHash);
    }

    public void IssueWebhookSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Secret zorunludur.", nameof(secret));
        WebhookSecret = secret;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
