using System.Text.Json;
using FI.Domain.AiAnalysis;
using FI.Domain.Classification;
using FI.Domain.Incidents;
using FI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FI.Infrastructure.Jobs;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 24.2, 25, 26.
/// Evidence toplandiktan sonra tetiklenir. Evidence bos ise AI cagrisi hic yapilmaz (dogrudan
/// NEEDS_HUMAN_REVIEW). Basarili ve gecerli analiz sonucu AI_ANALYZED yapar; aksi halde
/// NEEDS_HUMAN_REVIEW yapar. Her cagri (basarisiz dahil) log tablosuna yazilir; yalnizca parse
/// edilebilen, sema-echo uyumlu cikti business-facing analiz tablosuna kaydedilir.
/// </summary>
public class AiAnalysisJobHandler
{
    private readonly FiDbContext _db;
    private readonly IAiAnalysisClient _aiClient;
    private readonly Ai.AnthropicOptions _options;
    private readonly ILogger<AiAnalysisJobHandler> _logger;

    public AiAnalysisJobHandler(
        FiDbContext db, IAiAnalysisClient aiClient, IOptions<Ai.AnthropicOptions> options, ILogger<AiAnalysisJobHandler> logger)
    {
        _db = db;
        _aiClient = aiClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid incidentId, Guid correlationId, CancellationToken cancellationToken = default)
    {
        var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken);
        if (incident is null)
        {
            _logger.LogWarning("AiAnalysisJob: incident bulunamadi, atlaniyor.");
            return;
        }

        var integration = await _db.Integrations.FirstOrDefaultAsync(x => x.Id == incident.IntegrationId, cancellationToken);
        var evidenceRows = await _db.IncidentEvidence
            .Where(e => e.IncidentId == incidentId)
            .OrderByDescending(e => e.CollectedAt)
            .ToListAsync(cancellationToken);

        var promptVersion = await _db.PromptVersions.FirstOrDefaultAsync(p => p.Status == PromptVersionStatus.Active, cancellationToken);
        if (promptVersion is null)
        {
            _logger.LogError("AiAnalysisJob: aktif prompt version bulunamadi, incident NEEDS_HUMAN_REVIEW yapiliyor.");
            incident.MarkNeedsHumanReview();
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (evidenceRows.Count == 0)
        {
            incident.MarkNeedsHumanReview();
            _db.AiAnalysisLogs.Add(AiAnalysisLog.Create(
                incidentId, promptVersion.Id, "none", parseSuccess: false, schemaEchoMismatch: false,
                confidence: null, outOfEvidenceClaimsDetected: false, inputTokens: null, outputTokens: null,
                latencyMs: 0, errorMessage: "Evidence bulunamadigi icin AI cagrisi yapilmadi."));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var affectedRequests = await _db.IntegrationEvents.CountAsync(
            e => e.IntegrationId == incident.IntegrationId && e.Category == incident.Category.ToString()
                 && e.OccurredAt >= incident.FirstSeen && e.OccurredAt <= incident.LastSeen,
            cancellationToken);

        var integrationName = integration is null ? "unknown" : integration.Name;

        var deterministicClassification = new DeterministicClassificationInput(
            incident.Category.ToString(), incident.Severity.ToString(), integrationName,
            Math.Max(affectedRequests, incident.EventCount), incident.FirstSeen, incident.LastSeen,
            0, incident.Fingerprint);

        var evidenceInputs = evidenceRows
            .Select(e => new EvidenceInput(e.SourceType.ToString(), e.Summary, e.CollectedAt))
            .ToList();

        var userPayload = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            incidentId,
            deterministicClassification = new
            {
                category = deterministicClassification.Category,
                severity = deterministicClassification.Severity,
                affectedIntegration = deterministicClassification.AffectedIntegration,
                affectedRequests = deterministicClassification.AffectedRequests,
                firstSeenAt = deterministicClassification.FirstSeenAt,
                lastSeenAt = deterministicClassification.LastSeenAt,
                errorSignature = deterministicClassification.ErrorSignature
            },
            evidence = evidenceInputs.Select(e => new { sourceType = e.SourceType, summary = e.Summary, collectedAt = e.CollectedAt }),
            constraints = new { maxEvidenceItems = 10, evidenceOnly = true }
        });

        var modelId = incident.Severity == IncidentSeverity.Critical ? _options.EscalatedModel : _options.DefaultModel;

        var rawResponse = await _aiClient.AnalyzeAsync(promptVersion.SystemPromptTemplate, userPayload, modelId, cancellationToken);

        if (!rawResponse.CallSucceeded)
        {
            _db.AiAnalysisLogs.Add(AiAnalysisLog.Create(
                incidentId, promptVersion.Id, modelId, parseSuccess: false, schemaEchoMismatch: false,
                confidence: null, outOfEvidenceClaimsDetected: false,
                inputTokens: rawResponse.InputTokens, outputTokens: rawResponse.OutputTokens,
                latencyMs: rawResponse.LatencyMs, errorMessage: rawResponse.ErrorMessage));
            incident.MarkNeedsHumanReview();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("AiAnalysisJob: AI cagrisi basarisiz: {Error}", rawResponse.ErrorMessage);
            return;
        }

        var (validation, output) = AiAnalysisValidator.Validate(rawResponse.ResponseText, deterministicClassification, evidenceInputs);

        _db.AiAnalysisLogs.Add(AiAnalysisLog.Create(
            incidentId, promptVersion.Id, modelId,
            parseSuccess: validation.RejectionReason != AiAnalysisRejectionReason.ParseFailed,
            schemaEchoMismatch: validation.RejectionReason == AiAnalysisRejectionReason.SchemaEchoMismatch,
            confidence: output != null ? output.Confidence : null, outOfEvidenceClaimsDetected: validation.OutOfEvidenceClaimsDetected,
            inputTokens: rawResponse.InputTokens, outputTokens: rawResponse.OutputTokens,
            latencyMs: rawResponse.LatencyMs, errorMessage: null));

        if (!validation.IsValid || output is null)
        {
            incident.MarkNeedsHumanReview();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("AiAnalysisJob: validasyon basarisiz, sebep: {Reason}", validation.RejectionReason);
            return;
        }

        var previousLatest = await _db.AiAnalyses.Where(a => a.IncidentId == incidentId && a.IsLatest).ToListAsync(cancellationToken);
        foreach (var previous in previousLatest) previous.MarkSuperseded();

        var analysis = AiIncidentAnalysis.Create(
            incidentId, promptVersion.Id, modelId,
            output.IncidentTitle!, output.ProbableRootCause!,
            JsonSerializer.Serialize(output.Evidence), JsonSerializer.Serialize(output.EvidenceRefs ?? Array.Empty<string>()),
            JsonSerializer.Serialize(output.RecommendedActions),
            output.Confidence!.Value, validation.NeedsHumanReview, validation.OutOfEvidenceClaimsDetected);

        _db.AiAnalyses.Add(analysis);

        if (validation.NeedsHumanReview)
            incident.MarkNeedsHumanReview();
        else
            incident.MarkAiAnalyzed();

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "AiAnalysisJob: incident analiz edildi, confidence {Confidence}, needsHumanReview {NeedsReview}, correlation {CorrelationId}",
            analysis.Confidence, validation.NeedsHumanReview, correlationId);
    }
}
