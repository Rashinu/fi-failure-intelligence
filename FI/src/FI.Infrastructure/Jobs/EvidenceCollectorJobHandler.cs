using System.Text.Json;
using FI.Domain.Incidents;
using FI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FI.Infrastructure.Jobs;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 23. 4 kaynaktan 3'unu doldurur:
/// DEPLOYMENT, PREVIOUS_EVENT, HISTORICAL_INCIDENT. CONFIG_CHANGE, config-degisiklik audit
/// gunlugu (henuz insa edilmedi) kuruluncaya kadar kasitli olarak atlanir - hicbir evidence
/// yanlislikla uydurulmuyor; sadece gercek veriye sahip oldugumuz kaynaklar doldurulur.
/// Herhangi bir kaynak bos donerse, o sourceType evidence listesinde yer almaz.
/// </summary>
public class EvidenceCollectorJobHandler
{
    private const int MaxEvidenceItems = 10;
    private const int MaxPreviousEvents = 5;
    private const int MaxHistoricalIncidents = 5;
    private static readonly TimeSpan DeploymentWindowBefore = TimeSpan.FromHours(2);
    private static readonly TimeSpan PreviousEventWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan HistoricalIncidentWindow = TimeSpan.FromDays(90);

    private readonly FiDbContext _db;
    private readonly ILogger<EvidenceCollectorJobHandler> _logger;

    public EvidenceCollectorJobHandler(FiDbContext db, ILogger<EvidenceCollectorJobHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid incidentId, Guid correlationId, CancellationToken cancellationToken = default)
    {
        var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);
        if (incident is null)
        {
            _logger.LogWarning("EvidenceCollectorJob: incident {IncidentId} bulunamadı, atlanıyor.", incidentId);
            return;
        }

        var integration = await _db.Integrations.FirstOrDefaultAsync(x => x.Id == incident.IntegrationId, cancellationToken);
        if (integration is null)
        {
            _logger.LogWarning("EvidenceCollectorJob: integration {IntegrationId} bulunamadı, atlanıyor.", incident.IntegrationId);
            return;
        }

        var deploymentEvidence = await CollectDeploymentEvidenceAsync(incident, integration.Name, cancellationToken);
        var historicalEvidence = await CollectHistoricalIncidentEvidenceAsync(incident, cancellationToken);
        var previousEventEvidence = await CollectPreviousEventEvidenceAsync(incident, cancellationToken);

        // Onceliklendirme (Bolum 23): CONFIG_CHANGE (yok) > HISTORICAL_INCIDENT > DEPLOYMENT > PREVIOUS_EVENT.
        var ordered = historicalEvidence.Concat(deploymentEvidence).Concat(previousEventEvidence)
            .Take(MaxEvidenceItems)
            .ToList();

        foreach (var evidence in ordered)
            _db.IncidentEvidence.Add(evidence);

        incident.StartInvestigating();

        _db.OutboxMessages.Add(FI.Domain.Outbox.OutboxMessage.Create(
            FI.Domain.Outbox.OutboxMessageType.AiAnalysisJob,
            JsonSerializer.Serialize(new { incidentId, correlationId })));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Incident {IncidentId} icin {Count} evidence toplandi, correlation {CorrelationId}",
            incidentId, ordered.Count, correlationId);
    }

    private async Task<List<IncidentEvidence>> CollectDeploymentEvidenceAsync(Incident incident, string integrationName, CancellationToken cancellationToken)
    {
        var windowStart = incident.FirstSeen - DeploymentWindowBefore;
        var windowEnd = incident.FirstSeen;

        var deployments = await _db.Deployments
            .Where(d => d.IntegrationId == incident.IntegrationId && d.DeployedAt >= windowStart && d.DeployedAt <= windowEnd)
            .OrderByDescending(d => d.DeployedAt)
            .ToListAsync(cancellationToken);

        return deployments.Select(d =>
        {
            var minutesBefore = (int)(incident.FirstSeen - d.DeployedAt).TotalMinutes;
            var summary = $"Deployment '{d.Commit}' to {d.Service}/{d.Environment} occurred {minutesBefore} minute(s) before first failure";
            return IncidentEvidence.Create(
                incident.Id, EvidenceSourceType.Deployment, d.Id, summary,
                d.ChangedConfig, windowStart, windowEnd);
        }).ToList();
    }

    private async Task<List<IncidentEvidence>> CollectHistoricalIncidentEvidenceAsync(Incident incident, CancellationToken cancellationToken)
    {
        var windowStart = DateTimeOffset.UtcNow - HistoricalIncidentWindow;

        var historical = await _db.Incidents
            .Where(i => i.IntegrationId == incident.IntegrationId
                        && i.Id != incident.Id
                        && i.Category == incident.Category
                        && (i.Status == IncidentStatus.Resolved || i.Status == IncidentStatus.Ignored)
                        && i.ResolvedAt != null
                        && i.ResolvedAt >= windowStart)
            .OrderByDescending(i => i.ResolvedAt)
            .Take(MaxHistoricalIncidents)
            .ToListAsync(cancellationToken);

        return historical.Select(h =>
        {
            var summary = $"Similar {h.Category} incident resolved on {h.ResolvedAt:yyyy-MM-dd} after {h.EventCount} event(s)";
            return IncidentEvidence.Create(
                incident.Id, EvidenceSourceType.HistoricalIncident, h.Id, summary,
                null, windowStart, DateTimeOffset.UtcNow);
        }).ToList();
    }

    private async Task<List<IncidentEvidence>> CollectPreviousEventEvidenceAsync(Incident incident, CancellationToken cancellationToken)
    {
        var windowStart = incident.FirstSeen - PreviousEventWindow;

        var previousEvents = await _db.IntegrationEvents
            .Where(e => e.IntegrationId == incident.IntegrationId && e.OccurredAt < incident.FirstSeen && e.OccurredAt >= windowStart)
            .OrderByDescending(e => e.OccurredAt)
            .Take(MaxPreviousEvents)
            .ToListAsync(cancellationToken);

        return previousEvents.Select(e =>
        {
            var summary = $"Previous event: status {e.StatusCode}, category {e.Category ?? "uncategorized"}, {(int)(incident.FirstSeen - e.OccurredAt).TotalMinutes} minute(s) before first failure";
            return IncidentEvidence.Create(
                incident.Id, EvidenceSourceType.PreviousEvent, e.Id, summary,
                null, windowStart, incident.FirstSeen);
        }).ToList();
    }
}
