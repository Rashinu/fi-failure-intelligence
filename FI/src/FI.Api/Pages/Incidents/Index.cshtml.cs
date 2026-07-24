using FI.Domain.Classification;
using FI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Pages.Incidents;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md M17 (Product Proof) — mevcut Product API'nin (Bölüm 18.4)
/// üzerine ince bir sunum katmanı; yeni bir domain/backend kuralı eklemez, doğrudan FiDbContext'i
/// okur (aynı process içinde, kendi kendine HTTP çağrısı yapmaya gerek yok).
/// </summary>
public class IndexModel : PageModel
{
    private readonly FiDbContext _db;

    public IndexModel(FiDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Severity { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public List<IncidentRow> Incidents { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var query = _db.Incidents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Severity)) query = query.Where(i => i.Severity.ToString() == Severity);
        if (!string.IsNullOrWhiteSpace(Status)) query = query.Where(i => i.Status.ToString() == Status);

        var incidents = await query
            .OrderByDescending(i => i.LastSeen)
            .Take(100)
            .ToListAsync(cancellationToken);

        var integrationNames = await _db.Integrations
            .Where(x => incidents.Select(i => i.IntegrationId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        Incidents = incidents.Select(i => new IncidentRow(
            i.Id,
            integrationNames.GetValueOrDefault(i.IntegrationId, "unknown"),
            i.Category.ToString(),
            i.Severity.ToString(),
            i.Status.ToString(),
            i.FirstSeen,
            i.LastSeen,
            i.EventCount)).ToList();
    }

    public sealed record IncidentRow(
        Guid Id,
        string IntegrationName,
        string Category,
        string Severity,
        string Status,
        DateTimeOffset FirstSeen,
        DateTimeOffset LastSeen,
        int EventCount);
}
