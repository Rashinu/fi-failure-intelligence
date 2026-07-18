using System.Text.Json;
using FI.Domain.Classification;
using FI.Domain.Incidents;
using FI.Domain.Outbox;
using FI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FI.Infrastructure.Jobs;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 19.2, 21, 22.
/// Hangfire tarafından OutboxDispatcher üzerinden tetiklenir. Deterministik sınıflandırma +
/// fingerprint hesabı + incident upsert. Yalnızca incident yeni açıldığında veya
/// reopen/reset-as-new-occurrence olduğunda EvidenceCollectorJob outbox'a yazılır — zaten
/// aktif bir incident'a bağlanan her tekrar event için evidence yeniden toplanmaz (Bölüm 23).
/// AI analiz adımı (M5) bu job'a henüz bağlı değil.
/// </summary>
public class ClassifyJobHandler
{
    private readonly FiDbContext _db;
    private readonly ILogger<ClassifyJobHandler> _logger;

    public ClassifyJobHandler(FiDbContext db, ILogger<ClassifyJobHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid eventId, Guid correlationId, CancellationToken cancellationToken = default)
    {
        var evt = await _db.IntegrationEvents.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (evt is null)
        {
            _logger.LogWarning("ClassifyJob: event {EventId} bulunamadı, atlanıyor.", eventId);
            return;
        }

        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == evt.IntegrationId, cancellationToken);
        if (integration is null)
        {
            _logger.LogWarning("ClassifyJob: integration {IntegrationId} bulunamadı, atlanıyor.", evt.IntegrationId);
            return;
        }

        var input = await BuildClassificationInputAsync(evt, cancellationToken);
        var result = EventClassifier.Classify(input);

        evt.SetCategory(result.Category.ToString());

        var fingerprint = FingerprintCalculator.Compute(evt.IntegrationId, result.Category, result.ErrorSignature);

        var now = DateTimeOffset.UtcNow;
        var since10Min = now.AddMinutes(-10);
        var since15Min = now.AddMinutes(-15);
        var since30Min = now.AddMinutes(-30);

        var recentEventsQuery = _db.IntegrationEvents.Where(e => e.IntegrationId == evt.IntegrationId && e.Category == result.Category.ToString());
        var count10 = await recentEventsQuery.CountAsync(e => e.OccurredAt >= since10Min, cancellationToken);
        var count15 = await recentEventsQuery.CountAsync(e => e.OccurredAt >= since15Min, cancellationToken);
        var count30 = await recentEventsQuery.CountAsync(e => e.OccurredAt >= since30Min, cancellationToken);

        var severity = SeverityCalculator.Calculate(
            result.Category, count10, count15, count30,
            integration.BusinessCriticality == Domain.Ingestion.BusinessCriticality.Critical);

        var existingIncident = await _db.Incidents.FirstOrDefaultAsync(
            i => i.IntegrationId == evt.IntegrationId
                 && i.Fingerprint == fingerprint
                 && i.FingerprintAlgorithmVersion == FingerprintCalculator.AlgorithmVersion,
            cancellationToken);

        Incident incidentForEvidence;
        var needsEvidenceCollection = false;

        if (existingIncident is null)
        {
            incidentForEvidence = Incident.Open(evt.IntegrationId, fingerprint, result.Category, severity, evt.OccurredAt);
            _db.Incidents.Add(incidentForEvidence);
            needsEvidenceCollection = true;
        }
        else if (existingIncident.IsActive)
        {
            existingIncident.RecordNewEvent(evt.OccurredAt, severity);
            incidentForEvidence = existingIncident;
        }
        else if (existingIncident.IsWithinReopenCooldown(now))
        {
            existingIncident.Reopen(evt.OccurredAt, severity);
            incidentForEvidence = existingIncident;
            needsEvidenceCollection = true;
        }
        else
        {
            existingIncident.ResetAsNewOccurrence(evt.OccurredAt, severity);
            incidentForEvidence = existingIncident;
            needsEvidenceCollection = true;
        }

        if (needsEvidenceCollection)
        {
            _db.OutboxMessages.Add(OutboxMessage.Create(
                OutboxMessageType.EvidenceCollectorJob,
                JsonSerializer.Serialize(new { incidentId = incidentForEvidence.Id, correlationId })));
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Event {EventId} sınıflandırıldı: {Category} (fingerprint {Fingerprint}), correlation {CorrelationId}",
            eventId, result.Category, fingerprint, correlationId);
    }

    private async Task<ClassificationInput> BuildClassificationInputAsync(Domain.Ingestion.IntegrationEvent evt, CancellationToken cancellationToken)
    {
        bool hasInvalidSignatureHeader = false;
        bool hasRetryAfterHeader = false;
        string? normalizedErrorCode = null;
        string? normalizedEndpointPath = null;

        if (evt.RequestRedacted is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(evt.RequestRedacted);
                if (doc.RootElement.TryGetProperty("headers", out var headers))
                {
                    if (headers.TryGetProperty("X-Signature-Valid", out var sigValid) &&
                        sigValid.ValueKind == JsonValueKind.False)
                        hasInvalidSignatureHeader = true;

                    if (headers.TryGetProperty("Retry-After", out _))
                        hasRetryAfterHeader = true;
                }

                if (doc.RootElement.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
                    normalizedEndpointPath = NormalizePath(path.GetString());
            }
            catch (JsonException)
            {
                // Ham istek JSON olarak ayrıştırılamıyorsa header/path tabanlı kurallar atlanır.
            }
        }

        if (evt.ResponseRedacted is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(evt.ResponseRedacted);
                if (doc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                    normalizedErrorCode = code.GetString();
            }
            catch (JsonException)
            {
                // Ham yanıt JSON olarak ayrıştırılamıyorsa yalnızca ham metin regex'e girer.
            }
        }

        var isDuplicate = evt.IdempotencyKey is not null &&
            await _db.IntegrationEvents.CountAsync(
                e => e.IntegrationId == evt.IntegrationId && e.IdempotencyKey == evt.IdempotencyKey && e.Id != evt.Id,
                cancellationToken) > 0;

        return new ClassificationInput(
            StatusCode: evt.StatusCode,
            HasInvalidSignatureHeader: hasInvalidSignatureHeader,
            HasRetryAfterHeader: hasRetryAfterHeader,
            ErrorBodyText: evt.ResponseRedacted,
            HasSchemaValidationFailure: false, // gerçek şema validasyonu connector'larla gelecek (M4+)
            MissingSchemaFields: Array.Empty<string>(),
            IsDuplicateWithinWindow: isDuplicate,
            IsTimeoutError: false, // exception-tabanlı timeout/network sinyali connector entegrasyonlarıyla gelecek (M4+)
            IsNetworkError: false,
            NetworkExceptionType: null,
            NormalizedErrorCode: normalizedErrorCode,
            NormalizedEndpointPath: normalizedEndpointPath);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "unknown_path";
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = segments.Select(s =>
            Guid.TryParse(s, out _) ? "{id}" :
            int.TryParse(s, out _) ? "{n}" : s);
        return "/" + string.Join("/", normalized);
    }
}
