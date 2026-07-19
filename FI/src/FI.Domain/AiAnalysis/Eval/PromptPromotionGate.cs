namespace FI.Domain.AiAnalysis.Eval;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 26.3 — "Yeni versiyon, golden dataset
/// skorunda mevcut ACTIVE'i geçmeden... ACTIVE olamaz" ve Bölüm 26.4 — "Regresyon: önceki
/// ACTIVE'e göre herhangi bir boyutta >%10 düşüş → CI kırmızı, deploy engellenir." Saf,
/// framework'ten bağımsız — hem CI'da hem canlı promote endpoint'inde aynı karar kullanılır.
/// Not (kasıtlı sadeleştirme): dokümandaki "son N=200 canlı analizde parse-fail/evidence-dışı
/// iddia oranı kötüleşmeden" ek koşulu bu MVP'de uygulanmadı — bu, prompt kalitesinden bağımsız,
/// genel sistem sağlığı sinyali ve ayrı bir takip konusu (bkz. README Sonraki Adımlar).
/// </summary>
public static class PromptPromotionGate
{
    /// <summary>Bir boyutta baseline'a göre %10'dan fazla düşüş regresyon sayılır.</summary>
    public const double RegressionToleranceRatio = 0.90;

    public static PromptPromotionDecision Evaluate(EvalReport candidate, IReadOnlyDictionary<string, double>? baselinePerDimension)
    {
        var reasons = new List<string>();

        if (!candidate.Passed)
        {
            reasons.Add(
                $"Golden dataset eşiği karşılanmadı: ortalama {candidate.OverallAverage:F3} < {EvalReport.PassThreshold:F2} " +
                $"veya category-echo/format-uyumu boyutlarından birinde kritik FAIL var ({candidate.CriticalFailures.Count} senaryo).");
        }

        if (baselinePerDimension is not null)
        {
            foreach (var (dimension, candidateValue) in candidate.PerDimensionAverages)
            {
                if (!baselinePerDimension.TryGetValue(dimension, out var baselineValue) || baselineValue <= 0)
                    continue; // baseline'da bu boyut için karşılaştırılacak bir referans yok

                if (candidateValue < baselineValue * RegressionToleranceRatio)
                {
                    var dropPercent = (1 - candidateValue / baselineValue) * 100;
                    reasons.Add($"{dimension} boyutunda regresyon: {candidateValue:F3} (baseline {baselineValue:F3}, %{dropPercent:F1} düşüş).");
                }
            }
        }

        return new PromptPromotionDecision(reasons.Count == 0, reasons);
    }
}
