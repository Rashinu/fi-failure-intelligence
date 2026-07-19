using FI.Application.AiAnalysis;
using FI.Domain.AiAnalysis;
using FI.Infrastructure.Eval;
using FI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Controllers;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 26.3 — prompt version yaşam döngüsü
/// (DRAFT → ACTIVE → DEPRECATED). Yalnızca bir versiyon her zaman ACTIVE olabilir; geçiş
/// yalnızca golden dataset gate'i (Bölüm 26.4) onayladığında gerçekleşir.
/// </summary>
[ApiController]
[Route("api/v1/prompt-versions")]
public class PromptVersionsController : ControllerBase
{
    private readonly FiDbContext _db;
    private readonly PromptVersionPromotionService _promotionService;

    public PromptVersionsController(FiDbContext db, PromptVersionPromotionService promotionService)
    {
        _db = db;
        _promotionService = promotionService;
    }

    [HttpPost]
    public async Task<ActionResult<PromptVersionResponse>> CreateDraft(
        [FromBody] CreatePromptVersionRequest request, CancellationToken cancellationToken)
    {
        var draft = PromptVersion.CreateDraft(request.VersionLabel, request.SystemPromptTemplate);
        _db.PromptVersions.Add(draft);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = draft.Id }, ToResponse(draft));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PromptVersionResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var versions = await _db.PromptVersions.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        return Ok(versions.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PromptVersionResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var version = await _db.PromptVersions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return version is null ? NotFound() : Ok(ToResponse(version));
    }

    /// <summary>
    /// Golden dataset'e (Bölüm 26.4) karşı çalıştırır ve mevcut ACTIVE'e göre regresyon kontrolü
    /// yapar (Bölüm 26.3). Onaylanırsa bu versiyon ACTIVE, önceki ACTIVE DEPRECATED olur;
    /// onaylanmazsa hiçbir durum değişmez ama değerlendirme sonucu bu versiyona cache'lenir.
    /// </summary>
    [HttpPost("{id:guid}/promote")]
    public async Task<ActionResult<PromotePromptVersionResponse>> Promote(Guid id, CancellationToken cancellationToken)
    {
        var outcome = await _promotionService.PromoteAsync(id, cancellationToken);

        if (!outcome.DraftFound) return NotFound(new { error = "Prompt version bulunamadı." });
        if (!outcome.ValidState) return Conflict(new { error = "Yalnızca DRAFT durumundaki bir versiyon promote edilebilir." });

        return Ok(new PromotePromptVersionResponse(
            outcome.Decision!.Approved,
            outcome.Decision.Reasons,
            outcome.CandidateReport!.OverallAverage,
            outcome.CandidateReport.PerDimensionAverages));
    }

    private static PromptVersionResponse ToResponse(PromptVersion version) => new(
        version.Id, version.VersionLabel, version.Status.ToString(), version.RolloutPercentage,
        version.EvalOverallAverage, version.EvaluatedAt, version.CreatedAt);
}
