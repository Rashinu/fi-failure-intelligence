namespace FI.Domain.AiAnalysis;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 16.7, Bolum 26.3.
/// M5'te tek bir ACTIVE versiyon (v1) seed edilir; A/B rollout altyapisi (rollout_percentage)
/// alan olarak var ama M5'te kullanilmiyor (tek versiyon = %100).
/// </summary>
public class PromptVersion
{
    public Guid Id { get; private set; }
    public string VersionLabel { get; private set; } = default!;
    public string SystemPromptTemplate { get; private set; } = default!;
    public PromptVersionStatus Status { get; private set; }
    public int RolloutPercentage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PromptVersion() { }

    public static PromptVersion CreateActive(string versionLabel, string systemPromptTemplate)
    {
        if (string.IsNullOrWhiteSpace(versionLabel)) throw new ArgumentException("VersionLabel zorunludur.", nameof(versionLabel));
        if (string.IsNullOrWhiteSpace(systemPromptTemplate)) throw new ArgumentException("SystemPromptTemplate zorunludur.", nameof(systemPromptTemplate));

        return new PromptVersion
        {
            Id = Guid.NewGuid(),
            VersionLabel = versionLabel,
            SystemPromptTemplate = systemPromptTemplate,
            Status = PromptVersionStatus.Active,
            RolloutPercentage = 100,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
