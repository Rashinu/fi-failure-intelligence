using System.Text.Json;
using FI.Application.Incidents;
using FI.Domain.Classification;
using FI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Controllers;

/// <summary>Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 18.4/18.5. timeline alanı henüz eklenmedi.</summary>
[ApiController]
[Route("api/v1/incidents")]
public class IncidentsController : ControllerBase
{
    private const int MaxPageSize = 100;

    private readonly FiDbContext _db;

    public IncidentsController(FiDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IncidentListResponse>> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? severity,
        [FromQuery] string? category,
        [FromQuery] Guid? integrationId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _db.Incidents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Status.ToString() == status);
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(i => i.Severity.ToString() == severity);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(i => i.Category.ToString() == category);
        if (integrationId is not null) query = query.Where(i => i.IntegrationId == integrationId);

        var totalCount = await query.CountAsync(cancellationToken);

        var incidents = await query
            .OrderByDescending(i => i.LastSeen)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var integrationNames = await _db.Integrations
            .Where(x => incidents.Select(i => i.IntegrationId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var items = incidents.Select(i => new IncidentListItemResponse(
            i.Id,
            integrationNames.GetValueOrDefault(i.IntegrationId, "unknown"),
            i.Category.ToString(),
            i.Severity.ToString(),
            i.Status.ToString(),
            i.FirstSeen,
            i.LastSeen,
            i.EventCount,
            SuggestedActionCatalog.For(i.Category))).ToList();

        return Ok(new IncidentListResponse(items, page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<IncidentDetailResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var incident = await _db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (incident is null) return NotFound();

        var integration = await _db.Integrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == incident.IntegrationId, cancellationToken);

        var evidence = await _db.IncidentEvidence.AsNoTracking()
            .Where(e => e.IncidentId == id)
            .OrderByDescending(e => e.CollectedAt)
            .Select(e => new IncidentEvidenceResponse(e.Id, e.SourceType.ToString(), e.Summary, e.WindowStart, e.WindowEnd, e.CollectedAt))
            .ToListAsync(cancellationToken);

        var latestAnalysisEntity = await _db.AiAnalyses.AsNoTracking()
            .Where(a => a.IncidentId == id && a.IsLatest)
            .FirstOrDefaultAsync(cancellationToken);

        // Bkz. docs/CTO_REVIEW_ANALYSIS.md M18 - "kac musteri etkilendi". Incident'in kendi
        // event'lerine dogrudan bir FK yok (fingerprint uzerinden gruplaniyor); ClassifyJobHandler'in
        // severity hesabinda kullandigi ayni kategori+entegrasyon+zaman-penceresi mantigi burada da
        // kullanilir. Hicbir event customer ref tasimiyorsa (provider desteklemiyorsa) null donulur -
        // "0 musteri" ile "bu veri hic yok" birbirinden ayirt edilir.
        //
        // FirstSeen alt sinir icin bir guvenlik payi ile genisletilir: paralel Hangfire worker'lari
        // ayni fingerprint icin coklu ClassifyJob calistirabildiginden (bkz. ClassifyJobHandler'in
        // basindaki eszamanlilik notu), incident'i "acan" event kronolojik olarak ilk event olmak
        // zorunda degildir - sadece insert yarisini kazanan event'tir. Bu yuzden FirstSeen bazen
        // ayni patlamadaki daha erken event'lerden sonra gelebilir; pay olmadan bu erken event'lerin
        // customer ref'leri sessizce sayima girmez.
        var windowStart = incident.FirstSeen - TimeSpan.FromMinutes(15);
        var distinctCustomerRefs = await _db.IntegrationEvents.AsNoTracking()
            .Where(e => e.IntegrationId == incident.IntegrationId
                        && e.Category == incident.Category.ToString()
                        && e.OccurredAt >= windowStart && e.OccurredAt <= incident.LastSeen
                        && e.AffectedCustomerRef != null)
            .Select(e => e.AffectedCustomerRef)
            .Distinct()
            .CountAsync(cancellationToken);
        int? affectedCustomerCount = distinctCustomerRefs > 0 ? distinctCustomerRefs : null;

        AiAnalysisResponse? latestAnalysis = latestAnalysisEntity is null ? null : new AiAnalysisResponse(
            latestAnalysisEntity.Id,
            latestAnalysisEntity.IncidentTitle,
            latestAnalysisEntity.ProbableRootCause,
            JsonSerializer.Deserialize<List<string>>(latestAnalysisEntity.EvidenceJson) ?? new List<string>(),
            JsonSerializer.Deserialize<List<string>>(latestAnalysisEntity.RecommendedActionsJson) ?? new List<string>(),
            latestAnalysisEntity.Confidence,
            latestAnalysisEntity.NeedsHumanReview,
            latestAnalysisEntity.ModelVersion,
            latestAnalysisEntity.CreatedAt);

        return Ok(new IncidentDetailResponse(
            incident.Id,
            incident.IntegrationId,
            integration?.Name ?? "unknown",
            incident.Category.ToString(),
            incident.Severity.ToString(),
            incident.Status.ToString(),
            incident.FirstSeen,
            incident.LastSeen,
            incident.EventCount,
            incident.ReopenCount,
            incident.Fingerprint,
            SuggestedActionCatalog.For(incident.Category),
            affectedCustomerCount,
            evidence,
            latestAnalysis));
    }
}
