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

    // --- M19 P0-C: Grounding diagnosis (bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md).
    // Bu bloktaki testler, CheckGrounding'in DÜZELTİLMİŞ (normalize edilmiş) davranışını doğrular.
    // Düzeltmeden ÖNCE bu iki senaryo gerçek bir false-positive üretiyordu - bkz. M19 dokümanının
    // "Grounding Teşhisi" bölümü, aynı iki case'in eski (substring-only) validator'a karşı
    // çalıştırılıp gözlemlenen ham sonuçlarının kaydı için. ---

    private static readonly DeterministicClassificationInput EntityExpected = new(
        "AuthenticationError", "Critical", "Stripe Payments (Prod)", 43,
        DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, 401, "401_invalid_api_key");

    private static readonly IReadOnlyList<EvidenceInput> EntityEvidence = new List<EvidenceInput>
    {
        new("ConfigChange", "Deployment to integration 'Stripe Payments (Prod)' changed config key API_KEY 5 minutes before first failure", DateTimeOffset.UtcNow)
    };

    private static string EntityResponseJson(string rootCause) => JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0",
        incidentTitle = "Auth failures",
        category = EntityExpected.Category,
        severity = EntityExpected.Severity,
        affectedIntegration = EntityExpected.AffectedIntegration,
        affectedRequests = EntityExpected.AffectedRequests,
        probableRootCause = rootCause,
        evidence = new[] { "config change" },
        evidenceRefs = new[] { "ConfigChange" },
        recommendedActions = new[] { "Verify the credential" },
        confidence = 0.9,
        needsHumanReview = false,
        outOfEvidenceClaimsDetected = false
    });

    /// <summary>
    /// TEŞHİS (düzeltmeden önce false positive üretiyordu): evidence "Stripe Payments (Prod)"
    /// yazıyor (boşluk+parantezli); model bunu "StripePaymentsProd" gibi tek bir camelCase token
    /// olarak paraphrase ederse, eski (saf substring) kontrol bunu evidence'da BULAMIYORDU çünkü
    /// boşluk/parantez farkı birebir substring eşleşmesini bozuyordu - geçerli bir paraphrase,
    /// desteklenmeyen bir iddia gibi flag'leniyordu.
    /// </summary>
    [Fact]
    public void RootCauseWithReformattedEntityName_IsNotFalselyFlaggedAsUnsupported()
    {
        var response = EntityResponseJson("The StripePaymentsProd integration's API_KEY rotation is the likely cause");

        var (result, _) = AiAnalysisValidator.Validate(response, EntityExpected, EntityEvidence);

        result.OutOfEvidenceClaimsDetected.Should().BeFalse(
            "'StripePaymentsProd', 'Stripe Payments (Prod)'in geçerli bir yeniden biçimlendirmesi - farklı bir iddia değil");
    }

    [Fact]
    public void RootCauseWithDirectSupportedClaim_IsGrounded()
    {
        var response = EntityResponseJson("API_KEY was rotated, which caused the AuthenticationError failures");

        var (result, _) = AiAnalysisValidator.Validate(response, EntityExpected, EntityEvidence);

        result.OutOfEvidenceClaimsDetected.Should().BeFalse();
    }

    [Fact]
    public void RootCauseWithUnsupportedNewEntity_IsFlagged()
    {
        var response = EntityResponseJson("The WebhookProcessor service silently dropped the retry queue");

        var (result, _) = AiAnalysisValidator.Validate(response, EntityExpected, EntityEvidence);

        result.OutOfEvidenceClaimsDetected.Should().BeTrue("'WebhookProcessor' evidence'ın hiçbir yerinde geçmiyor - uydurulmuş bir iddia");
        result.FlaggedClaims.Should().Contain("WebhookProcessor");
    }

    [Fact]
    public void RootCauseWithCommonEnglishStopwords_NotFalselyFlaggedAsUnsupportedEntities()
    {
        // Bkz. docs/reviews/PROMPT_QUALITY_ITERATION.md - gerçek bir Claude Haiku koşusunda somut
        // olarak gözlemlendi: EntityLikeTokenRegex, cümle başına gelen sıradan İngilizce
        // kelimeleri ("Human", "However", "Repeated") gerçek bir entity/sistem adıymış gibi
        // yakalıyordu. CommonEnglishWordStopList bunu düzeltir - bu kelimeler artık asla flag'lenmez.
        var response = EntityResponseJson(
            "API_KEY was rotated. However, the evidence is limited. Human review is recommended " +
            "because of the repeated pattern observed here.");

        var (result, _) = AiAnalysisValidator.Validate(response, EntityExpected, EntityEvidence);

        result.OutOfEvidenceClaimsDetected.Should().BeFalse();
        result.FlaggedClaims.Should().NotContain("However").And.NotContain("Human");
    }

    [Fact]
    public void RootCauseWithGenuineNewEntity_StillFlagged_StopListDoesNotLoosenRealDetection()
    {
        // Stopword filtresi yalnızca listedeki TAM kelimeleri hariç tutar - gerçek, evidence
        // dışı bir entity adı (listede olmayan) hâlâ doğru şekilde yakalanmalı.
        var response = EntityResponseJson("The CustomBillingProxy service intercepted and dropped the request");

        var (result, _) = AiAnalysisValidator.Validate(response, EntityExpected, EntityEvidence);

        result.OutOfEvidenceClaimsDetected.Should().BeTrue();
        result.FlaggedClaims.Should().Contain("CustomBillingProxy");
    }

    [Fact]
    public void RootCauseWithUngroundedNumber_StillFlagged_NormalizationDoesNotLoosenNumericCheck()
    {
        // Bkz. M19 P0-C: normalizasyon yalnızca noktalama/boşluk farklarını gideriyor - sayısal
        // doğrulamayı GEVŞETMİYOR. Evidence'da olmayan bir rakam hâlâ flag'lenmeli.
        var response = EntityResponseJson("Exactly 99999 requests failed due to a config drift");

        var (result, _) = AiAnalysisValidator.Validate(response, EntityExpected, EntityEvidence);

        result.OutOfEvidenceClaimsDetected.Should().BeTrue();
        result.FlaggedClaims.Should().Contain("99999");
    }

    [Fact]
    public void RootCauseWithContradictoryClaim_IsFlagged()
    {
        // Evidence: "config key API_KEY ... changed". Contradiction: model iddia ediyor ki
        // hiçbir config değişikliği OLMADI - bu hem şema-echo'dan bağımsız hem de mevcut basit
        // token-tabanlı kontrolün doğrudan yakalayamayacağı bir tür (contradiction detection,
        // kapsamı dışında - bkz. M19 dokümanı "bilinen sınırlamalar"). Burada yalnızca yeni,
        // evidence-dışı bir iddianın (WEBHOOK_SECRET) hâlâ doğru flag'lendiğini doğruluyoruz.
        var response = EntityResponseJson("No configuration changed; instead WEBHOOK_SECRET expired on its own");

        var (result, _) = AiAnalysisValidator.Validate(response, EntityExpected, EntityEvidence);

        result.OutOfEvidenceClaimsDetected.Should().BeTrue();
        result.FlaggedClaims.Should().Contain("WEBHOOK_SECRET");
    }
}
