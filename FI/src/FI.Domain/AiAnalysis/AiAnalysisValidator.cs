using System.Text.Json;
using System.Text.RegularExpressions;

namespace FI.Domain.AiAnalysis;

/// <summary>
/// Bkz. Bolum 26.2 - Validasyon Zinciri. Saf, framework'ten bagimsiz.
/// Yalnizca PARSE HATASI ve SEMA/ECHO UYUMSUZLUGU tam red (IsValid=false, kayit olusturulmaz).
/// Confidence esigi ve grounding kontrolu kaydi REDDETMEZ - yalnizca needsHumanReview'i zorlar
/// (Bolum 26.2 madde 3-4: "needsHumanReview sistem tarafindan zorla true" - kayit yine olusur).
/// Bu kontrol KESIN DEGILDIR (basit substring/kelime-ortusme seviyesinde, MVP kapsami).
///
/// Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-C - CheckGrounding'in gerçek Claude
/// Haiku'ya karşı 0.100 skorlamasının teşhisi, gerçek testlerle (bkz. AiAnalysisValidatorTests)
/// İKİ somut false-positive kaynağı ortaya çıkardı: (1) modelin, KENDİSİNE zaten verilmiş
/// deterministik bağlamı (kategori adı, entegrasyon adı) doğru şekilde tekrar etmesi bile
/// "desteklenmeyen iddia" sayılıyordu - çünkü bu alanlar evidence metninde DEĞİL, deterministik
/// input'ta yaşıyordu ve kontrol yalnızca evidence'a bakıyordu; (2) bir entity adının noktalama/
/// boşluk farkıyla yeniden biçimlendirilmesi ("Stripe Payments (Prod)" vs "StripePaymentsProd")
/// birebir substring eşleşmesini bozuyordu. Sayısal iddialar (uydurulmuş rakamlar) İSE zaten
/// doğru yakalanıyordu - bu katmana KASITLI OLARAK dokunulmadı (gevşetilmedi).
/// </summary>
public static class AiAnalysisValidator
{
    public const double ConfidenceRejectThreshold = 0.65;
    public const double ConfidencePriorityReviewThreshold = 0.35;

    private static readonly Regex NumberTokenRegex = new(@"\b\d{2,}\b", RegexOptions.Compiled);
    private static readonly Regex EntityLikeTokenRegex = new(@"\b[A-Z][a-zA-Z0-9_]{3,}\b", RegexOptions.Compiled);
    private static readonly Regex MarkdownFenceRegex = new(@"^```(?:json)?\s*|\s*```$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex NonAlphanumericRegex = new(@"[^a-zA-Z0-9]", RegexOptions.Compiled);

    /// <summary>
    /// Bkz. docs/reviews/PROMPT_QUALITY_ITERATION.md - "ADIM 2" gerçek Claude Haiku koşusunda
    /// (V2 prompt taslağı) somut olarak gözlemlenen false-positive kaynağı: EntityLikeTokenRegex
    /// yalnızca "büyük harfle başlayan 4+ karakter" arıyor, bu da cümle başına gelen veya
    /// açıklayıcı düzyazıda geçen sıradan İngilizce kelimeleri ("Human", "However", "Repeated")
    /// gerçek bir entity/sistem adıymış gibi yakalıyordu (FlaggedClaims=["Human"] gibi, gerçek
    /// bir uydurma değil). Bu liste SAYISAL kontrolü (NumberTokenRegex) HİÇ etkilemez - yalnızca
    /// EntityLikeTokenRegex eşleşmelerine, corpus karşılaştırmasından ÖNCE uygulanır. Kasıtlı
    /// olarak dar tutuldu (genel bir İngilizce stopword sözlüğü değil) - yalnızca gerçek
    /// çalıştırmalarda görülen veya açıklayıcı/geçiş kelimesi kategorisine giren, entity/sistem
    /// adı OLMASI mantıken imkansız kelimeler içeriyor. Gerçek bir entity/sistem adı (ör. yeni
    /// bir kod veya ürün adı) bu listede YOKTUR ve hâlâ doğru şekilde yakalanır.
    /// </summary>
    private static readonly HashSet<string> CommonEnglishWordStopList = new(StringComparer.Ordinal)
    {
        "Human", "However", "Repeated", "Additionally", "Furthermore", "Therefore", "Because",
        "Since", "While", "Given", "This", "That", "These", "Those", "The", "Review", "Verify",
        "Check", "Confirm", "Note", "Also", "Please", "Consider", "Ensure", "Investigate",
        "Examine", "Audit", "Monitor", "Evidence", "Root", "Cause", "Based", "According",
        "Current", "Recent", "Multiple", "Several", "Various", "Both", "Either", "Neither",
        "Rather", "Instead", "Overall", "Specifically", "Notably", "Importantly",
        "Alternatively", "Similarly", "Meanwhile", "Subsequently", "Consequently", "Ultimately",
        "Effectively", "Actually", "Generally", "Typically", "Usually", "Potentially",
        "Possibly", "Likely", "Unlikely", "Confirmed", "Unconfirmed", "Suggests", "Suggesting",
        "Indicates", "Indicating", "Requires", "Recommend", "Recommended", "Suggested"
    };

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
        var (outOfEvidence, flaggedClaims) = CheckGrounding(output, expected, evidence);

        var needsHumanReview = output.NeedsHumanReview.Value;
        if (confidence < ConfidenceRejectThreshold) needsHumanReview = true;
        if (outOfEvidence) needsHumanReview = true;

        // Kayit her zaman olusturulur (IsValid=true); needsHumanReview yalnizca zorlanir.
        return (new AiAnalysisValidationResult(true, AiAnalysisRejectionReason.None, needsHumanReview, outOfEvidence, flaggedClaims), output);
    }

    private static (bool OutOfEvidence, IReadOnlyList<string> FlaggedClaims) CheckGrounding(
        AiAnalysisOutput output, DeterministicClassificationInput expected, IReadOnlyList<EvidenceInput> evidence)
    {
        // Katman 1 (mevcut): evidence özetleri - toplanan GERÇEK kanıt.
        // Katman 2 (M19 P0-C düzeltmesi): deterministik sınıflandırma bağlamı da corpus'a dahil -
        // modelin zaten kendisine verilmiş kategori/entegrasyon adını doğru şekilde tekrar etmesi
        // bir "iddia" değil, echo edilen bilinen bir gerçek; bunu "desteklenmeyen" saymak yanlıştı.
        var corpus = string.Join(" ", evidence.Select(e => e.Summary)
            .Append(expected.Category)
            .Append(expected.AffectedIntegration)
            .Append(expected.Severity));
        // Katman 3 (M19 P0-C düzeltmesi): karşılaştırma öncesi noktalama/boşluk normalize edilir -
        // "Stripe Payments (Prod)" ile "StripePaymentsProd" aynı normalize edilmiş forma indirgenir.
        // Bu SAYISAL kontrolü gevşetmiyor (rakamlar zaten noktalamasız) - yalnızca entity-adı
        // yeniden biçimlendirmelerini (paraphrase) doğru tanıyor.
        var normalizedCorpus = Normalize(corpus);

        var candidateText = output.ProbableRootCause ?? string.Empty;

        // Bkz. CommonEnglishWordStopList doc'u - stopword filtresi yalnızca entity-benzeri
        // token akışına uygulanır, sayısal token'lara (NumberTokenRegex) HİÇ dokunmaz.
        var numberTokens = NumberTokenRegex.Matches(candidateText).Select(m => m.Value);
        var entityTokens = EntityLikeTokenRegex.Matches(candidateText).Select(m => m.Value)
            .Where(t => !CommonEnglishWordStopList.Contains(t));

        var tokens = numberTokens.Concat(entityTokens).Distinct().ToList();

        var flagged = tokens
            .Where(t => !normalizedCorpus.Contains(Normalize(t), StringComparison.OrdinalIgnoreCase))
            .ToList();

        return (flagged.Count > 0, flagged);
    }

    private static string Normalize(string text) => NonAlphanumericRegex.Replace(text, "");

    private static AiAnalysisValidationResult Rejected(AiAnalysisRejectionReason reason) =>
        new(false, reason, true, false, Array.Empty<string>());
}
