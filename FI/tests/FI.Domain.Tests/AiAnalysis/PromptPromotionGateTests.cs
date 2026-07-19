using FI.Domain.AiAnalysis.Eval;
using FluentAssertions;

namespace FI.Domain.Tests.AiAnalysis;

/// <summary>Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 26.3/26.4.</summary>
public class PromptPromotionGateTests
{
    private static ScenarioScore PerfectScore(string id) => new(id, 1, 1, 1, 1, 1, 1, 1);

    private static EvalReport ReportWith(params ScenarioScore[] scores) => new(scores);

    [Fact]
    public void Evaluate_PassingCandidate_NoBaseline_IsApproved()
    {
        var candidate = ReportWith(PerfectScore("s1"), PerfectScore("s2"));

        var decision = PromptPromotionGate.Evaluate(candidate, baselinePerDimension: null);

        decision.Approved.Should().BeTrue();
        decision.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_CandidateBelowThreshold_IsRejected()
    {
        var weak = new ScenarioScore("s1", 0, 0, 0, 0, 0, 0, 1); // FormatCompliance=1 avoids the critical-failure shortcut but average is low
        var candidate = ReportWith(weak);

        var decision = PromptPromotionGate.Evaluate(candidate, baselinePerDimension: null);

        decision.Approved.Should().BeFalse();
        decision.Reasons.Should().ContainSingle(r => r.Contains("Golden dataset eşiği"));
    }

    [Fact]
    public void Evaluate_CriticalFailure_IsRejectedEvenIfAverageHigh()
    {
        // CategoryEcho=0 (critical) but every other dimension perfect - average alone would exceed 0.85.
        var scoreWithCriticalFailure = new ScenarioScore("s1", 0, 1, 1, 1, 1, 1, 1);
        var candidate = ReportWith(scoreWithCriticalFailure);

        var decision = PromptPromotionGate.Evaluate(candidate, baselinePerDimension: null);

        decision.Approved.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_RegressionBeyondTolerance_IsRejected()
    {
        var candidate = ReportWith(new ScenarioScore("s1", 1, 1, 1, 1, 1, 0.7, 1)); // NeedsHumanReviewAccuracy dropped
        var baseline = new Dictionary<string, double>
        {
            [nameof(ScenarioScore.CategoryEcho)] = 1,
            [nameof(ScenarioScore.RootCauseAccuracy)] = 1,
            [nameof(ScenarioScore.Grounding)] = 1,
            [nameof(ScenarioScore.Actionability)] = 1,
            [nameof(ScenarioScore.ConfidenceCalibration)] = 1,
            [nameof(ScenarioScore.NeedsHumanReviewAccuracy)] = 1.0, // candidate 0.7 vs baseline 1.0 => 30% drop > 10% tolerance
            [nameof(ScenarioScore.FormatCompliance)] = 1
        };

        var decision = PromptPromotionGate.Evaluate(candidate, baseline);

        decision.Approved.Should().BeFalse();
        decision.Reasons.Should().ContainSingle(r => r.Contains(nameof(ScenarioScore.NeedsHumanReviewAccuracy)));
    }

    [Fact]
    public void Evaluate_RegressionWithinTolerance_IsApproved()
    {
        // 5% drop is within the 10% tolerance.
        var candidate = ReportWith(new ScenarioScore("s1", 1, 1, 1, 1, 1, 0.95, 1));
        var baseline = new Dictionary<string, double>
        {
            [nameof(ScenarioScore.CategoryEcho)] = 1,
            [nameof(ScenarioScore.RootCauseAccuracy)] = 1,
            [nameof(ScenarioScore.Grounding)] = 1,
            [nameof(ScenarioScore.Actionability)] = 1,
            [nameof(ScenarioScore.ConfidenceCalibration)] = 1,
            [nameof(ScenarioScore.NeedsHumanReviewAccuracy)] = 1.0,
            [nameof(ScenarioScore.FormatCompliance)] = 1
        };

        var decision = PromptPromotionGate.Evaluate(candidate, baseline);

        decision.Approved.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_BaselineDimensionMissingOrZero_SkipsRegressionCheckForThatDimension()
    {
        var candidate = ReportWith(PerfectScore("s1"));
        var emptyBaseline = new Dictionary<string, double>();

        var decision = PromptPromotionGate.Evaluate(candidate, emptyBaseline);

        decision.Approved.Should().BeTrue();
    }
}
