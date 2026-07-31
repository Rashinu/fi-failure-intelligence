using System.Text.Json;
using FI.Api.Middleware;
using FI.Application.Incidents;
using FI.Domain.Audit;
using FI.Domain.Classification;
using FI.Domain.Incidents;
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

        // Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-A - "kac musteri/operasyon etkilendi"
        // ve "operation coverage" tek bir gruplu sorguda hesaplanir (IncidentId FK'sina gore,
        // bkz. TD3 - zaman penceresi tahmini yok). Hem musteri hem operasyon icin ayni durustluk
        // ilkesi uygulanir: hicbir event alani tasimiyorsa null/"None" donulur ("bilinmiyor"),
        // "sifir" degil; yalnizca BAZI event'ler alani tasiyorsa "Partial" - bilinen sayi TOPLAM
        // etki degil, bilinen alt kumedir.
        var impact = await _db.IntegrationEvents.AsNoTracking()
            .Where(e => e.IncidentId == incident.Id)
            .GroupBy(e => 1)
            .Select(g => new
            {
                TotalEvents = g.Count(),
                EventsWithCustomer = g.Count(e => e.AffectedCustomerRef != null),
                DistinctCustomers = g.Select(e => e.AffectedCustomerRef).Where(r => r != null).Distinct().Count(),
                EventsWithOperation = g.Count(e => e.OperationRef != null),
                DistinctOperations = g.Select(e => e.OperationRef).Where(r => r != null).Distinct().Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var totalEvents = impact?.TotalEvents ?? 0;
        var customerCoverage = ComputeCoverage(totalEvents, impact?.EventsWithCustomer ?? 0);
        int? affectedCustomerCount = customerCoverage == "None" ? null : impact!.DistinctCustomers;
        var operationCoverage = ComputeCoverage(totalEvents, impact?.EventsWithOperation ?? 0);
        int? knownOperationCount = operationCoverage == "None" ? null : impact!.DistinctOperations;

        ResolutionResponse? resolution = incident.ResolvedAt is null
            ? null
            : new ResolutionResponse(incident.ResolvedAt.Value, incident.ResolvedBy, incident.ResolutionNote);

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
            customerCoverage,
            knownOperationCount,
            operationCoverage,
            resolution,
            evidence,
            latestAnalysis));
    }

    /// <summary>Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-B.</summary>
    [HttpPost("{id:guid}/resolve")]
    public async Task<ActionResult<IncidentDetailResponse>> Resolve(Guid id, [FromBody] ResolveIncidentRequest? request, CancellationToken cancellationToken)
    {
        var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (incident is null) return NotFound();

        try
        {
            incident.Resolve(request?.ResolvedBy, request?.Note);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        _db.AuditLogs.Add(AuditLog.Create(
            AuditActorType.User, request?.ResolvedBy, AuditActions.IncidentResolved, AuditEntityTypes.Incident,
            incident.Id, HttpContext.GetCorrelationId(),
            changes: JsonSerializer.Serialize(new { note = request?.Note })));

        await _db.SaveChangesAsync(cancellationToken);

        return await GetById(id, cancellationToken);
    }

    private static string ComputeCoverage(int totalEvents, int eventsWithField) =>
        eventsWithField == 0 ? "None" :
        eventsWithField == totalEvents ? "Complete" : "Partial";
}
