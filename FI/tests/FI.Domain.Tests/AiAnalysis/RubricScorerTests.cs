using FI.Domain.AiAnalysis;
using FI.Domain.AiAnalysis.Eval;
using FluentAssertions;

namespace FI.Domain.Tests.AiAnalysis;

/// <summary>Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 26.4 — rubric boyutlarının saf mantığı.</summary>
public class RubricScorerTests
{
    private static readonly DeterministicClassificationInput Deterministic = new(
        "AuthenticationError", "High", "stripe-prod", 42,
        DateTimeOffset.Parse("2026-07-12T10:00:00Z"), DateTimeOffset.Parse("2026-07-12T10:30:00Z"),
        401, "401_invalid_api_key");

    private static GoldenScenario Scenario(ScenarioExpectation expectation) => new(
        "test-scenario", "desc", Deterministic,
        new[] { new EvidenceInput("DEPLOYMENT", "API key for integration rotated 42 minutes before first failure", DateTimeOffset.UtcNow) },
        expectation);

    private static AiAnalysisOutput GoodOutput(string rootCause, double confidence, bool needsReview) => new(
        "1.0", "Auth failure", Deterministic.Category, Deterministic.Severity, null,
        Deterministic.AffectedIntegration, Deterministic.AffectedRequests,
        rootCause, new[] { "API key rotated" }, new[] { "DEPLOYMENT" },
        new[] { "Verify key rotation", "Roll back if unintended" }, confidence, needsReview, false);

    private static AiAnalysisValidationResult ValidResult(bool needsReview, bool outOfEvidence = false) =>
        new(true, AiAnalysisRejectionReason.None, needsReview, outOfEvidence, Array.Empty<string>());

    [Fact]
    public void FormatCompliance_RejectedOutput_ZerosAllDimensions()
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "rotated" }, false, 0.7, 1.0));
        var rejected = new AiAnalysisValidationResult(false, AiAnalysisRejectionReason.ParseFailed, true, false, Array.Empty<string>());

        var score = RubricScorer.Score(scenario, rejected, output: null);

        score.FormatCompliance.Should().Be(0);
        score.Average.Should().Be(0);
        score.HasCriticalFailure.Should().BeTrue();
    }

    [Fact]
    public void CategoryEcho_MismatchedField_ScoresZero()
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "rotated" }, false, 0.7, 1.0));
        var output = GoodOutput("API key rotated 42 minutes before failure", 0.85, false) with { Severity = "Low" };

        var score = RubricScorer.Score(scenario, ValidResult(false), output);

        score.CategoryEcho.Should().Be(0);
        score.HasCriticalFailure.Should().BeTrue();
    }

    [Fact]
    public void RootCauseAccuracy_AllKeywordsPresent_ScoresOne()
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "API key", "rotated" }, false, 0.7, 1.0));
        var output = GoodOutput("The API key was rotated shortly before failures began.", 0.85, false);

        var score = RubricScorer.Score(scenario, ValidResult(false), output);

        score.RootCauseAccuracy.Should().Be(1.0);
    }

    [Fact]
    public void RootCauseAccuracy_HalfKeywordsPresent_ScoresHalf()
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "API key", "webhook secret" }, false, 0.7, 1.0));
        var output = GoodOutput("The API key configuration changed recently.", 0.85, false);

        var score = RubricScorer.Score(scenario, ValidResult(false), output);

        score.RootCauseAccuracy.Should().Be(0.5);
    }

    [Fact]
    public void Grounding_OutOfEvidenceClaimsDetected_ScoresZero()
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "rotated" }, false, 0.7, 1.0));
        var output = GoodOutput("API key rotated", 0.85, false);

        var score = RubricScorer.Score(scenario, ValidResult(false, outOfEvidence: true), output);

        score.Grounding.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Actionability_OutsideOneToFiveActions_ScoresZero(int actionCount)
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "rotated" }, false, 0.7, 1.0));
        var actions = Enumerable.Range(1, actionCount).Select(i => $"Action {i}").ToArray();
        var output = GoodOutput("API key rotated", 0.85, false) with { RecommendedActions = actions };

        var score = RubricScorer.Score(scenario, ValidResult(false), output);

        score.Actionability.Should().Be(0);
    }

    [Fact]
    public void ConfidenceCalibration_WithinExpectedRange_ScoresOne()
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "rotated" }, false, 0.7, 1.0));
        var output = GoodOutput("API key rotated", 0.9, false);

        var score = RubricScorer.Score(scenario, ValidResult(false), output);

        score.ConfidenceCalibration.Should().Be(1.0);
    }

    [Fact]
    public void ConfidenceCalibration_FarBelowExpectedRange_ScoresZero()
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "rotated" }, false, 0.7, 1.0));
        var output = GoodOutput("API key rotated", 0.0, true);

        var score = RubricScorer.Score(scenario, ValidResult(true), output);

        score.ConfidenceCalibration.Should().Be(0.0);
    }

    [Fact]
    public void NeedsHumanReviewAccuracy_MismatchedExpectation_ScoresZero()
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "rotated" }, true, 0.0, 0.5));
        var output = GoodOutput("API key rotated", 0.3, false);

        var score = RubricScorer.Score(scenario, ValidResult(false), output);

        score.NeedsHumanReviewAccuracy.Should().Be(0);
    }

    [Fact]
    public void PerfectOutput_AllDimensionsScoreOne()
    {
        var scenario = Scenario(new ScenarioExpectation(new[] { "API key", "rotated" }, false, 0.7, 1.0));
        var output = GoodOutput("The API key was rotated shortly before the failures began.", 0.9, false);

        var score = RubricScorer.Score(scenario, ValidResult(false), output);

        score.Average.Should().Be(1.0);
        score.HasCriticalFailure.Should().BeFalse();
    }
}
