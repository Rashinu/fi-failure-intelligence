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

    /// <summary>Bkz. Bölüm 26.3/26.4 — bu versiyonun en son golden dataset koşusunun sonucu (promote sırasında cache'lenir, sonraki karşılaştırmalarda baseline olarak yeniden kullanılır).</summary>
    public double? EvalOverallAverage { get; private set; }
    public string? EvalPerDimensionJson { get; private set; }
    public DateTimeOffset? EvaluatedAt { get; private set; }

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

    public static PromptVersion CreateDraft(string versionLabel, string systemPromptTemplate)
    {
        if (string.IsNullOrWhiteSpace(versionLabel)) throw new ArgumentException("VersionLabel zorunludur.", nameof(versionLabel));
        if (string.IsNullOrWhiteSpace(systemPromptTemplate)) throw new ArgumentException("SystemPromptTemplate zorunludur.", nameof(systemPromptTemplate));

        return new PromptVersion
        {
            Id = Guid.NewGuid(),
            VersionLabel = versionLabel,
            SystemPromptTemplate = systemPromptTemplate,
            Status = PromptVersionStatus.Draft,
            RolloutPercentage = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void RecordEvalResult(double overallAverage, string perDimensionJson)
    {
        EvalOverallAverage = overallAverage;
        EvalPerDimensionJson = perDimensionJson;
        EvaluatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Bölüm 26.3 — gate onayı çağıran katmanda (<c>PromptPromotionGate</c>) verilir; bu yalnızca durum geçişini uygular.</summary>
    public void Activate()
    {
        Status = PromptVersionStatus.Active;
        RolloutPercentage = 100;
    }

    public void Deprecate()
    {
        Status = PromptVersionStatus.Deprecated;
        RolloutPercentage = 0;
    }
}
