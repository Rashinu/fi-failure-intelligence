namespace FI.Domain.AiAnalysis.Eval;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 26.4 — bir golden senaryonun
/// "doğru cevap" beklentisi. Rubric bu beklentiye göre modelin/prompt'un ürettiği çıktıyı puanlar.
/// </summary>
public sealed record ScenarioExpectation(
    IReadOnlyList<string> RootCauseKeywords,
    bool ExpectNeedsHumanReview,
    double MinConfidence,
    double MaxConfidence);
