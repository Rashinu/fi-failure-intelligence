namespace FI.Domain.AiAnalysis.Eval;

/// <summary>
/// Bkz. Bölüm 26.4 — saf, framework'ten bağımsız rubric. <see cref="AiAnalysisValidator"/>'ın
/// çıktısı (parse/echo/confidence/grounding zaten uygulanmış) üzerinden 7 boyutta puanlar.
/// Bu sınıf modelin/promptun KALİTESİNİ ölçer; <c>AiAnalysisValidator</c> ise sistemin
/// GÜVENLİ DAVRANIŞINI garanti eder — ikisi farklı sorumluluklardır ve karıştırılmaz.
/// </summary>
public static class RubricScorer
{
    public static ScenarioScore Score(GoldenScenario scenario, AiAnalysisValidationResult validation, AiAnalysisOutput? output)
    {
        var formatCompliance = validation.RejectionReason == AiAnalysisRejectionReason.None ? 1.0 : 0.0;

        if (output is null || formatCompliance == 0.0)
        {
            return new ScenarioScore(scenario.Id, CategoryEcho: 0, RootCauseAccuracy: 0, Grounding: 0,
                Actionability: 0, ConfidenceCalibration: 0, NeedsHumanReviewAccuracy: 0, FormatCompliance: formatCompliance);
        }

        var categoryEcho = ScoreCategoryEcho(scenario.Deterministic, output);
        var rootCauseAccuracy = ScoreRootCauseAccuracy(scenario.Expectation, output);
        var grounding = validation.OutOfEvidenceClaimsDetected ? 0.0 : 1.0;
        var actionability = ScoreActionability(output);
        var confidenceCalibration = ScoreConfidenceCalibration(scenario.Expectation, output);
        var needsReviewAccuracy = validation.NeedsHumanReview == scenario.Expectation.ExpectNeedsHumanReview ? 1.0 : 0.0;

        return new ScenarioScore(
            scenario.Id, categoryEcho, rootCauseAccuracy, grounding, actionability,
            confidenceCalibration, needsReviewAccuracy, formatCompliance);
    }

    private static double ScoreCategoryEcho(DeterministicClassificationInput expected, AiAnalysisOutput output)
    {
        var matches =
            output.Category == expected.Category &&
            output.Severity == expected.Severity &&
            output.AffectedIntegration == expected.AffectedIntegration &&
            output.AffectedRequests == expected.AffectedRequests;
        return matches ? 1.0 : 0.0;
    }

    private static double ScoreRootCauseAccuracy(ScenarioExpectation expectation, AiAnalysisOutput output)
    {
        if (expectation.RootCauseKeywords.Count == 0) return 1.0;

        var text = output.ProbableRootCause ?? string.Empty;
        var found = expectation.RootCauseKeywords.Count(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        return (double)found / expectation.RootCauseKeywords.Count;
    }

    private static double ScoreActionability(AiAnalysisOutput output)
    {
        var count = output.RecommendedActions?.Count(a => !string.IsNullOrWhiteSpace(a)) ?? 0;
        return count is >= 1 and <= 5 ? 1.0 : 0.0;
    }

    private static double ScoreConfidenceCalibration(ScenarioExpectation expectation, AiAnalysisOutput output)
    {
        var confidence = output.Confidence ?? 0.0;
        if (confidence >= expectation.MinConfidence && confidence <= expectation.MaxConfidence) return 1.0;

        var distance = Math.Min(Math.Abs(confidence - expectation.MinConfidence), Math.Abs(confidence - expectation.MaxConfidence));
        return Math.Clamp(1 - distance * 2, 0.0, 1.0);
    }
}
