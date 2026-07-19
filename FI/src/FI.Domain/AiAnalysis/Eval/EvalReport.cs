namespace FI.Domain.AiAnalysis.Eval;

/// <summary>
/// Bkz. Bölüm 26.4 — "Eşik: Toplam ortalama ≥ 0.85 VE hiçbir category echo/format uyumu FAIL
/// yoksa → yeni versiyon ACTIVE adayı." Bu eşik burada tek bir yerde (<see cref="Passed"/>)
/// uygulanır. Regresyon karşılaştırması (önceki ACTIVE'e göre boyut bazlı >%10 düşüş, Bölüm 26.3)
/// <see cref="PerDimensionAverages"/> üzerinden <c>PromptPromotionGate</c>'te uygulanır.
/// </summary>
public sealed record EvalReport(IReadOnlyList<ScenarioScore> ScenarioScores)
{
    public const double PassThreshold = 0.85;

    public double OverallAverage => ScenarioScores.Count == 0 ? 0 : ScenarioScores.Average(s => s.Average);

    public IReadOnlyList<ScenarioScore> CriticalFailures => ScenarioScores.Where(s => s.HasCriticalFailure).ToList();

    public bool Passed => OverallAverage >= PassThreshold && CriticalFailures.Count == 0;

    private double DimensionAverage(Func<ScenarioScore, double> selector) =>
        ScenarioScores.Count == 0 ? 0 : ScenarioScores.Average(selector);

    /// <summary>Bölüm 26.3/26.4 regresyon karşılaştırmasında kullanılan, boyut başına ortalamalar.</summary>
    public IReadOnlyDictionary<string, double> PerDimensionAverages => new Dictionary<string, double>
    {
        [nameof(ScenarioScore.CategoryEcho)] = DimensionAverage(s => s.CategoryEcho),
        [nameof(ScenarioScore.RootCauseAccuracy)] = DimensionAverage(s => s.RootCauseAccuracy),
        [nameof(ScenarioScore.Grounding)] = DimensionAverage(s => s.Grounding),
        [nameof(ScenarioScore.Actionability)] = DimensionAverage(s => s.Actionability),
        [nameof(ScenarioScore.ConfidenceCalibration)] = DimensionAverage(s => s.ConfidenceCalibration),
        [nameof(ScenarioScore.NeedsHumanReviewAccuracy)] = DimensionAverage(s => s.NeedsHumanReviewAccuracy),
        [nameof(ScenarioScore.FormatCompliance)] = DimensionAverage(s => s.FormatCompliance)
    };
}
