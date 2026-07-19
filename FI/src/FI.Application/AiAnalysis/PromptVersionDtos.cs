namespace FI.Application.AiAnalysis;

public sealed record CreatePromptVersionRequest(string VersionLabel, string SystemPromptTemplate);

public sealed record PromptVersionResponse(
    Guid Id,
    string VersionLabel,
    string Status,
    int RolloutPercentage,
    double? EvalOverallAverage,
    DateTimeOffset? EvaluatedAt,
    DateTimeOffset CreatedAt);

public sealed record PromotePromptVersionResponse(
    bool Approved,
    IReadOnlyList<string> Reasons,
    double CandidateOverallAverage,
    IReadOnlyDictionary<string, double> CandidatePerDimensionAverages);
