using System.Text.Json;
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
    public List<EvidenceRow> Evidence { get; private set; } = new();
    public AiAnalysisRow? LatestAnalysis { get; private set; }
    public List<TimelineEntry> Timeline { get; private set; } = new();

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

        var timeline = new List<TimelineEntry> { new("İlk hata görüldü", FirstSeen) };
        timeline.AddRange(evidence.Select(e => new TimelineEntry($"Evidence toplandı: {e.SourceType}", e.CollectedAt)));
        if (LatestAnalysis is not null) timeline.Add(new TimelineEntry("AI analiz tamamlandı", latestAnalysisEntity!.CreatedAt));
        Timeline = timeline.OrderBy(t => t.At).ToList();

        return Page();
    }

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
