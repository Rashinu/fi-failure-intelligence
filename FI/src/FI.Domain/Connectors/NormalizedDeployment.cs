namespace FI.Domain.Connectors;

public sealed record ChangedConfigField(string Key, bool Changed);

/// <summary>Bölüm 34 — deployment webhook'undan türetilen kanonik model.</summary>
public sealed record NormalizedDeployment(
    string Service,
    string Environment,
    string Commit,
    DateTimeOffset DeployedAt,
    IReadOnlyList<ChangedConfigField>? ChangedConfig);
