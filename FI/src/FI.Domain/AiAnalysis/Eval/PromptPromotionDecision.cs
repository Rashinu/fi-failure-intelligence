namespace FI.Domain.AiAnalysis.Eval;

/// <summary>Bkz. Bölüm 26.3 — bir DRAFT prompt versiyonunun ACTIVE olup olamayacağı kararı.</summary>
public sealed record PromptPromotionDecision(bool Approved, IReadOnlyList<string> Reasons);
