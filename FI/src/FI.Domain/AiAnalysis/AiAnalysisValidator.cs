using System.Text.Json;
using System.Text.RegularExpressions;

namespace FI.Domain.AiAnalysis;

/// <summary>
/// Bkz. Bolum 26.2 - Validasyon Zinciri. Saf, framework'ten bagimsiz.
/// Yalnizca PARSE HATASI ve SEMA/ECHO UYUMSUZLUGU tam red (IsValid=false, kayit olusturulmaz).
/// Confidence esigi ve grounding kontrolu kaydi REDDETMEZ - yalnizca needsHumanReview'i zorlar
/// (Bolum 26.2 madde 3-4: "needsHumanReview sistem tarafindan zorla true" - kayit yine olusur).
/// Bu kontrol KESIN DEGILDIR (basit substring/kelime-ortusme seviyesinde, MVP kapsami).
/// </summary>
public static class AiAnalysisValidator
{
    public const double ConfidenceRejectThreshold = 0.65;
    public const double ConfidencePriorityReviewThreshold = 0.35;

    private static readonly Regex NumberTokenRegex = new(@"\b\d{2,}\b", RegexOptions.Compiled);
    private static readonly Regex EntityLikeTokenRegex = new(@"\b[A-Z][a-zA-Z0-9_]{3,}\b", RegexOptions.Compiled);
    private static readonly Regex MarkdownFenceRegex = new(@"^```(?:json)?\s*|\s*```$", RegexOptions.Compiled | RegexOptions.Multiline);

    public static (AiAnalysisValidationResult Result, AiAnalysisOutput? Output) Validate(
        string? rawResponseText,
        DeterministicClassificationInput expected,
        IReadOnlyList<EvidenceInput> evidence)
    {
        if (string.IsNullOrWhiteSpace(rawResponseText))
            return (Rejected(AiAnalysisRejectionReason.ParseFailed), null);

        // Modeller "Output ONLY valid JSON" talimatina ragmen siklikla yaniti markdown code
        // fence icine sarar (orn. Claude). Bir round-trip retry harcamadan bu en yaygin
        // gercek-dunya durumunu temizleyerek cozuyoruz (Bolum 26.2'deki retry, bundan sonraki
        // gercek parse hatalari icin ayrica degerlendirilebilir).
        var cleaned = MarkdownFenceRegex.Replace(rawResponseText.Trim(), string.Empty).Trim();

        AiAnalysisOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<AiAnalysisOutput>(cleaned, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            return (Rejected(AiAnalysisRejectionReason.ParseFailed), null);
        }

        if (output is null
            || string.IsNullOrWhiteSpace(output.IncidentTitle)
            || string.IsNullOrWhiteSpace(output.ProbableRootCause)
            || output.Evidence is null or { Count: 0 }
            || output.RecommendedActions is null or { Count: 0 }
            || output.Confidence is null
            || output.NeedsHumanReview is null)
        {
            return (Rejected(AiAnalysisRejectionReason.ParseFailed), output);
        }

        var echoMismatch =
            !string.Equals(output.Category, expected.Category, StringComparison.Ordinal) ||
            !string.Equals(output.Severity, expected.Severity, StringComparison.Ordinal) ||
            !string.Equals(output.AffectedIntegration, expected.AffectedIntegration, StringComparison.Ordinal) ||
            output.AffectedRequests != expected.AffectedRequests;

        if (echoMismatch)
            return (Rejected(AiAnalysisRejectionReason.SchemaEchoMismatch), output);

        var confidence = output.Confidence.Value;
        var (outOfEvidence, flaggedClaims) = CheckGrounding(output, evidence);

        var needsHumanReview = output.NeedsHumanReview.Value;
        if (confidence < ConfidenceRejectThreshold) needsHumanReview = true;
        if (outOfEvidence) needsHumanReview = true;

        // Kayit her zaman olusturulur (IsValid=true); needsHumanReview yalnizca zorlanir.
        return (new AiAnalysisValidationResult(true, AiAnalysisRejectionReason.None, needsHumanReview, outOfEvidence, flaggedClaims), output);
    }

    private static (bool OutOfEvidence, IReadOnlyList<string> FlaggedClaims) CheckGrounding(
        AiAnalysisOutput output, IReadOnlyList<EvidenceInput> evidence)
    {
        var corpus = string.Join(" ", evidence.Select(e => e.Summary));
        var candidateText = output.ProbableRootCause ?? string.Empty;

        var tokens = NumberTokenRegex.Matches(candidateText).Select(m => m.Value)
            .Concat(EntityLikeTokenRegex.Matches(candidateText).Select(m => m.Value))
            .Distinct()
            .ToList();

        var flagged = tokens
            .Where(t => !corpus.Contains(t, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return (flagged.Count > 0, flagged);
    }

    private static AiAnalysisValidationResult Rejected(AiAnalysisRejectionReason reason) =>
        new(false, reason, true, false, Array.Empty<string>());
}
