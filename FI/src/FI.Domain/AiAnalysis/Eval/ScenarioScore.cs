namespace FI.Domain.AiAnalysis.Eval;

/// <summary>Bkz. Bölüm 26.4 — bir senaryonun rubric boyutlarına göre 0-1 arası puanları.</summary>
public sealed record ScenarioScore(
    string ScenarioId,
    double CategoryEcho,
    double RootCauseAccuracy,
    double Grounding,
    double Actionability,
    double ConfidenceCalibration,
    double NeedsHumanReviewAccuracy,
    double FormatCompliance)
{
    public double Average => new[]
    {
        CategoryEcho, RootCauseAccuracy, Grounding, Actionability,
        ConfidenceCalibration, NeedsHumanReviewAccuracy, FormatCompliance
    }.Average();

    /// <summary>Bölüm 26.4 eşiği: "hiçbir category echo/format uyumu FAIL yoksa" — FAIL burada 0 puan demektir.</summary>
    public bool HasCriticalFailure => CategoryEcho == 0 || FormatCompliance == 0;
}
