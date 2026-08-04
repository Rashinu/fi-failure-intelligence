using System.Text.Json;
using FI.Api.Middleware;
using FI.Domain.Audit;
using FI.Domain.Classification;
using FI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Pages.Incidents;

public class DetailModel : PageModel
{
    private readonly FiDbContext _db;

    public DetailModel(FiDbContext db)
    {
        _db = db;
    }

    public bool Found { get; private set; }
    public Guid Id { get; private set; }
    public string IntegrationName { get; private set; } = "";
    public string Category { get; private set; } = "";
    public string Severity { get; private set; } = "";
    public string Status { get; private set; } = "";
    public DateTimeOffset FirstSeen { get; private set; }
    public DateTimeOffset LastSeen { get; private set; }
    public int EventCount { get; private set; }
    public int ReopenCount { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string SuggestedAction { get; private set; } = "";
    public int? AffectedCustomerCount { get; private set; }
    public string CustomerCoverage { get; private set; } = "None";
    public int? KnownOperationCount { get; private set; }
    public string OperationCoverage { get; private set; } = "None";
    public bool CanResolve { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }
    public string? ResolutionNote { get; private set; }
    public List<EvidenceRow> Evidence { get; private set; } = new();
    public AiAnalysisRow? LatestAnalysis { get; private set; }
    public List<TimelineEntry> Timeline { get; private set; } = new();

    [BindProperty]
    public string? ResolvedByInput { get; set; }

    [BindProperty]
    public string? ResolutionNoteInput { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var incident = await _db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (incident is null) return NotFound();

        var integration = await _db.Integrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == incident.IntegrationId, cancellationToken);

        var evidence = await _db.IncidentEvidence.AsNoTracking()
            .Where(e => e.IncidentId == id)
            .OrderByDescending(e => e.CollectedAt)
            .ToListAsync(cancellationToken);

        var latestAnalysisEntity = await _db.AiAnalyses.AsNoTracking()
            .Where(a => a.IncidentId == id && a.IsLatest)
            .FirstOrDefaultAsync(cancellationToken);

        // Bkz. IncidentsController.GetById / docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-A -
        // aynı tek-sorgu, IncidentId FK'sına göre müşteri+operasyon impact/coverage hesabı.
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

        Found = true;
        Id = incident.Id;
        IntegrationName = integration?.Name ?? "unknown";
        Category = incident.Category.ToString();
        Severity = incident.Severity.ToString();
        Status = incident.Status.ToString();
        FirstSeen = incident.FirstSeen;
        LastSeen = incident.LastSeen;
        EventCount = incident.EventCount;
        ReopenCount = incident.ReopenCount;
        Fingerprint = incident.Fingerprint;
        SuggestedAction = SuggestedActionCatalog.For(incident.Category);

        var totalEvents = impact?.TotalEvents ?? 0;
        CustomerCoverage = ComputeCoverage(totalEvents, impact?.EventsWithCustomer ?? 0);
        AffectedCustomerCount = CustomerCoverage == "None" ? null : impact!.DistinctCustomers;
        OperationCoverage = ComputeCoverage(totalEvents, impact?.EventsWithOperation ?? 0);
        KnownOperationCount = OperationCoverage == "None" ? null : impact!.DistinctOperations;

        CanResolve = incident.IsActive;
        ResolvedAt = incident.ResolvedAt;
        ResolvedBy = incident.ResolvedBy;
        ResolutionNote = incident.ResolutionNote;

        Evidence = evidence.Select(e => new EvidenceRow(e.SourceType.ToString(), e.Summary, e.CollectedAt)).ToList();

        if (latestAnalysisEntity is not null)
        {
            LatestAnalysis = new AiAnalysisRow(
                latestAnalysisEntity.IncidentTitle,
                latestAnalysisEntity.ProbableRootCause,
                JsonSerializer.Deserialize<List<string>>(latestAnalysisEntity.RecommendedActionsJson) ?? new List<string>(),
                latestAnalysisEntity.Confidence,
                latestAnalysisEntity.NeedsHumanReview,
                latestAnalysisEntity.CreatedAt);
        }

        var timeline = new List<TimelineEntry> { new("First error observed", FirstSeen) };
        timeline.AddRange(evidence.Select(e => new TimelineEntry($"Evidence collected: {e.SourceType}", e.CollectedAt)));
        if (LatestAnalysis is not null) timeline.Add(new TimelineEntry("AI analysis completed", latestAnalysisEntity!.CreatedAt));
        if (ResolvedAt is not null) timeline.Add(new TimelineEntry("Incident resolved", ResolvedAt.Value));
        Timeline = timeline.OrderBy(t => t.At).ToList();

        return Page();
    }

    /// <summary>Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-B - Post-Redirect-Get: resolve
    /// sonrası aynı sayfaya GET olarak yönlendirir, böylece sayfa yenilenirse ikinci bir resolve
    /// denemesi tetiklenmez.</summary>
    public async Task<IActionResult> OnPostResolveAsync(Guid id, CancellationToken cancellationToken)
    {
        var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (incident is null) return NotFound();

        try
        {
            incident.Resolve(ResolvedByInput, ResolutionNoteInput);

            _db.AuditLogs.Add(AuditLog.Create(
                AuditActorType.User, ResolvedByInput, AuditActions.IncidentResolved, AuditEntityTypes.Incident,
                incident.Id, HttpContext.GetCorrelationId(),
                changes: JsonSerializer.Serialize(new { note = ResolutionNoteInput })));

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Bkz. Incident.Resolve - zaten Resolved/Ignored bir incident'ı tekrar resolve etmeye
            // çalışmak geçersiz bir geçiş; sessizce yut, sayfa mevcut (değişmemiş) durumu gösterir.
        }

        return RedirectToPage(new { id });
    }

    private static string ComputeCoverage(int totalEvents, int eventsWithField) =>
        eventsWithField == 0 ? "None" :
        eventsWithField == totalEvents ? "Complete" : "Partial";

    public sealed record EvidenceRow(string SourceType, string Summary, DateTimeOffset CollectedAt);

    public sealed record AiAnalysisRow(
        string Title,
        string ProbableRootCause,
        IReadOnlyList<string> RecommendedActions,
        double Confidence,
        bool NeedsHumanReview,
        DateTimeOffset CreatedAt);

    public sealed record TimelineEntry(string Label, DateTimeOffset At);
}
