namespace FI.Domain.AiAnalysis;

public enum AiAnalysisRejectionReason
{
    None,
    ParseFailed,
    SchemaEchoMismatch,
    LowConfidence,
    OutOfEvidenceClaims
}

/// <summary>Bkz. Bolum 26.2 - validasyon zincirinin sonucu. IsValid=false ise incident NEEDS_HUMAN_REVIEW olur.</summary>
public sealed record AiAnalysisValidationResult(
    bool IsValid,
    AiAnalysisRejectionReason RejectionReason,
    bool NeedsHumanReview,
    bool OutOfEvidenceClaimsDetected,
    IReadOnlyList<string> FlaggedClaims);
