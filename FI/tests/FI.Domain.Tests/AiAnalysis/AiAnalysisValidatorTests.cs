using System.Text.Json;
using FI.Domain.AiAnalysis;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.AiAnalysis;

public class AiAnalysisValidatorTests
{
    private static readonly DeterministicClassificationInput Expected = new(
        "AuthenticationError", "High", "Stripe Payments", 83,
        DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, 401, "401_invalid_api_key");

    private static readonly IReadOnlyList<EvidenceInput> Evidence = new List<EvidenceInput>
    {
        new("ConfigChange", "API key for integration rotated 42 minutes before first failure", DateTimeOffset.UtcNow)
    };

    private static string ValidResponseJson(double confidence = 0.9, bool needsHumanReview = false, string? rootCause = null) =>
        JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            incidentTitle = "Stripe authentication failures after deployment",
            category = Expected.Category,
            severity = Expected.Severity,
            affectedIntegration = Expected.AffectedIntegration,
            affectedRequests = Expected.AffectedRequests,
            probableRootCause = rootCause ?? "The API key was rotated shortly before the first failure",
            evidence = new[] { "API key rotated 42 minutes before first failure" },
            evidenceRefs = new[] { "ConfigChange" },
            recommendedActions = new[] { "Verify the current production secret" },
            confidence,
            needsHumanReview,
            outOfEvidenceClaimsDetected = false
        });

    [Fact]
    public void ValidResponse_HighConfidence_IsValidAndDoesNotNeedReview()
    {
        var (result, output) = AiAnalysisValidator.Validate(ValidResponseJson(), Expected, Evidence);

        result.IsValid.Should().BeTrue();
        result.NeedsHumanReview.Should().BeFalse();
        output.Should().NotBeNull();
    }

    [Fact]
    public void NullResponse_IsRejectedAsParseFailed()
    {
        var (result, output) = AiAnalysisValidator.Validate(null, Expected, Evidence);

        result.IsValid.Should().BeFalse();
        result.RejectionReason.Should().Be(AiAnalysisRejectionReason.ParseFailed);
        result.NeedsHumanReview.Should().BeTrue();
        output.Should().BeNull();
    }

    [Fact]
    public void MalformedJson_IsRejectedAsParseFailed()
    {
        var (result, output) = AiAnalysisValidator.Validate("not valid json {{{", Expected, Evidence);

        result.IsValid.Should().BeFalse();
        result.RejectionReason.Should().Be(AiAnalysisRejectionReason.ParseFailed);
    }

    [Fact]
    public void MissingRequiredField_IsRejectedAsParseFailed()
    {
        var incomplete = JsonSerializer.Serialize(new { schemaVersion = "1.0", incidentTitle = "Title only" });

        var (result, _) = AiAnalysisValidator.Validate(incomplete, Expected, Evidence);

        result.IsValid.Should().BeFalse();
        result.RejectionReason.Should().Be(AiAnalysisRejectionReason.ParseFailed);
    }

    [Fact]
    public void CategoryEchoMismatch_IsRejectedAsSchemaEchoMismatch()
    {
        var mismatched = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            incidentTitle = "Title",
            category = "ProviderError",
            severity = Expected.Severity,
            affectedIntegration = Expected.AffectedIntegration,
            affectedRequests = Expected.AffectedRequests,
            probableRootCause = "Some cause",
            evidence = new[] { "evidence text" },
            evidenceRefs = new[] { "ConfigChange" },
            recommendedActions = new[] { "Do something" },
            confidence = 0.9,
            needsHumanReview = false,
            outOfEvidenceClaimsDetected = false
        });

        var (result, _) = AiAnalysisValidator.Validate(mismatched, Expected, Evidence);

        result.IsValid.Should().BeFalse();
        result.RejectionReason.Should().Be(AiAnalysisRejectionReason.SchemaEchoMismatch);
    }

    [Fact]
    public void AffectedRequestsEchoMismatch_IsRejectedAsSchemaEchoMismatch()
    {
        var mismatched = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            incidentTitle = "Title",
            category = Expected.Category,
            severity = Expected.Severity,
            affectedIntegration = Expected.AffectedIntegration,
            affectedRequests = 999,
            probableRootCause = "Some cause",
            evidence = new[] { "evidence text" },
            evidenceRefs = new[] { "ConfigChange" },
            recommendedActions = new[] { "Do something" },
            confidence = 0.9,
            needsHumanReview = false,
            outOfEvidenceClaimsDetected = false
        });

        var (result, _) = AiAnalysisValidator.Validate(mismatched, Expected, Evidence);

        result.IsValid.Should().BeFalse();
        result.RejectionReason.Should().Be(AiAnalysisRejectionReason.SchemaEchoMismatch);
    }

    [Fact]
    public void LowConfidence_IsStillValid_ButForcesNeedsHumanReview()
    {
        var (result, output) = AiAnalysisValidator.Validate(ValidResponseJson(confidence: 0.2, needsHumanReview: false), Expected, Evidence);

        result.IsValid.Should().BeTrue();
        result.NeedsHumanReview.Should().BeTrue();
        output.Should().NotBeNull();
    }

    [Fact]
    public void ConfidenceBelowRejectThreshold_ForcesNeedsHumanReviewEvenIfModelSaidFalse()
    {
        var (result, _) = AiAnalysisValidator.Validate(
            ValidResponseJson(confidence: AiAnalysisValidator.ConfidenceRejectThreshold - 0.01, needsHumanReview: false),
            Expected, Evidence);

        result.NeedsHumanReview.Should().BeTrue();
    }

    [Fact]
    public void RootCauseWithUngroundedNumber_ForcesOutOfEvidenceClaimsDetected()
    {
        var response = ValidResponseJson(rootCause: "Exactly 47281 requests failed due to a config drift");

        var (result, _) = AiAnalysisValidator.Validate(response, Expected, Evidence);

        result.IsValid.Should().BeTrue();
        result.OutOfEvidenceClaimsDetected.Should().BeTrue();
        result.NeedsHumanReview.Should().BeTrue();
        result.FlaggedClaims.Should().Contain("47281");
    }

    [Fact]
    public void RootCauseGroundedInEvidence_DoesNotFlagOutOfEvidenceClaims()
    {
        var response = ValidResponseJson(rootCause: "The API key rotation is the likely cause of the failures");

        var (result, _) = AiAnalysisValidator.Validate(response, Expected, Evidence);

        result.OutOfEvidenceClaimsDetected.Should().BeFalse();
    }
}
