namespace FI.Domain.AiAnalysis;

/// <summary>Bkz. Bolum 25.1 - AI Input Contract (Evidence-Only). deterministicClassification degistirilemez girdidir.</summary>
public sealed record DeterministicClassificationInput(
    string Category,
    string Severity,
    string AffectedIntegration,
    int AffectedRequests,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int StatusCodeSample,
    string ErrorSignature);

public sealed record EvidenceInput(string SourceType, string Summary, DateTimeOffset CollectedAt);

public sealed record AiAnalysisInput(
    Guid IncidentId,
    DeterministicClassificationInput DeterministicClassification,
    IReadOnlyList<EvidenceInput> Evidence);
