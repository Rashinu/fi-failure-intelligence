using System.Text.Json;
using FI.Domain.AiAnalysis;
using FI.Domain.AiAnalysis.Eval;
using FI.Infrastructure.Ai;
using FI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FI.Infrastructure.Eval;

public sealed record PromptPromotionOutcome(bool DraftFound, bool ValidState, PromptPromotionDecision? Decision, EvalReport? CandidateReport);

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 26.3 — DRAFT bir prompt versiyonunu
/// golden dataset'e (Bölüm 26.4) karşı çalıştırır, mevcut ACTIVE versiyonun (henüz
/// değerlendirilmediyse önce onu da çalıştırıp) baseline'ıyla karşılaştırır, ve
/// <see cref="PromptPromotionGate"/> onaylarsa ACTIVE/DEPRECATED durum geçişini uygular.
/// </summary>
public sealed class PromptVersionPromotionService
{
    private readonly FiDbContext _db;
    private readonly IAiAnalysisClient _aiClient;
    private readonly AnthropicOptions _options;

    public PromptVersionPromotionService(FiDbContext db, IAiAnalysisClient aiClient, IOptions<AnthropicOptions> options)
    {
        _db = db;
        _aiClient = aiClient;
        _options = options.Value;
    }

    public async Task<PromptPromotionOutcome> PromoteAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        var draft = await _db.PromptVersions.FirstOrDefaultAsync(p => p.Id == draftId, cancellationToken);
        if (draft is null) return new PromptPromotionOutcome(false, false, null, null);
        if (draft.Status != PromptVersionStatus.Draft) return new PromptPromotionOutcome(true, false, null, null);

        var candidateReport = await EvalHarness.RunAsync(
            _aiClient, draft.SystemPromptTemplate, _options.DefaultModel, GoldenDataset.Scenarios, cancellationToken);
        draft.RecordEvalResult(candidateReport.OverallAverage, JsonSerializer.Serialize(candidateReport.PerDimensionAverages));

        var currentActive = await _db.PromptVersions.FirstOrDefaultAsync(p => p.Status == PromptVersionStatus.Active, cancellationToken);
        IReadOnlyDictionary<string, double>? baseline = null;

        if (currentActive is not null)
        {
            if (currentActive.EvalPerDimensionJson is null)
            {
                // Bkz. Bolum 26.3 - mevcut ACTIVE hic degerlendirilmediyse (ör. M5'te seed edilen
                // ilk versiyon) simdi degerlendirilir ve sonuc cache'lenir; sonraki promote
                // cagrilari bu baseline'i yeniden hesaplamadan kullanir.
                var baselineReport = await EvalHarness.RunAsync(
                    _aiClient, currentActive.SystemPromptTemplate, _options.DefaultModel, GoldenDataset.Scenarios, cancellationToken);
                currentActive.RecordEvalResult(baselineReport.OverallAverage, JsonSerializer.Serialize(baselineReport.PerDimensionAverages));
                baseline = baselineReport.PerDimensionAverages;
            }
            else
            {
                baseline = JsonSerializer.Deserialize<Dictionary<string, double>>(currentActive.EvalPerDimensionJson);
            }
        }

        var decision = PromptPromotionGate.Evaluate(candidateReport, baseline);

        if (decision.Approved)
        {
            currentActive?.Deprecate();
            draft.Activate();
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new PromptPromotionOutcome(true, true, decision, candidateReport);
    }
}
