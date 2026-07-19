using System.Text;
using System.Text.Json;
using FI.Domain.Connectors;
using FI.Domain.Ingestion;
using FI.Domain.Outbox;
using FI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Controllers;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 34-37. Provider webhook'larının
/// varış noktası — genel <c>/api/v1/events</c>/<c>/api/v1/deployments</c> endpoint'lerinden
/// farklı olarak burada kimlik doğrulama X-Api-Key değil, connector'a özgü webhook imzasıdır
/// (Stripe-Signature, X-Hub-Signature-256, vb.) — bu yüzden <see cref="ApiKeyAuthMiddleware"/>
/// kapsamı dışındadır (route prefix'i middleware'in korumalı liste'sinde yok).
/// İmza doğrulaması başarısız olsa bile event reddedilmez; SIGNATURE_ERROR olarak kaydedilir
/// (Bölüm 34, madde 6) — bu bilginin kendisi bir incident sinyalidir.
/// </summary>
[ApiController]
[Route("api/v1/webhooks/{provider}/{integrationId:guid}")]
public class WebhooksController : ControllerBase
{
    private readonly FiDbContext _db;
    private readonly IConnectorRegistry _registry;

    public WebhooksController(FiDbContext db, IConnectorRegistry registry)
    {
        _db = db;
        _registry = registry;
    }

    [HttpPost("events")]
    public async Task<IActionResult> IngestEvent(string provider, Guid integrationId, CancellationToken cancellationToken)
    {
        if (!_registry.TryGetIntegrationConnector(provider, out var connector))
            return NotFound(new { error = $"Bilinmeyen provider: {provider}." });

        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
        if (integration is null) return NotFound(new { error = "Entegrasyon bulunamadı." });

        var payload = await ReadRawPayloadAsync(cancellationToken);

        var isSignatureVerified = integration.WebhookSecret is not null &&
            connector.VerifySignature(payload, integration.WebhookSecret);

        var normalized = connector.Normalize(payload, isSignatureVerified);

        var integrationEvent = IntegrationEvent.Create(
            integrationId,
            normalized.EventType,
            normalized.StatusCode,
            normalized.RequestJson,
            normalized.ResponseJson,
            normalized.LatencyMs,
            correlationId: Guid.NewGuid(),
            idempotencyKey: normalized.ProviderEventId,
            apiKeyId: null,
            isSignatureVerified: isSignatureVerified,
            payloadSizeBytes: Encoding.UTF8.GetByteCount(payload.RawBody),
            isTruncated: false,
            occurredAt: normalized.OccurredAt);

        if (normalized.ProviderEventId is not null)
        {
            var alreadyProcessed = await _db.IntegrationEvents.AnyAsync(
                e => e.IntegrationId == integrationId && e.IdempotencyKey == normalized.ProviderEventId,
                cancellationToken);
            if (alreadyProcessed) return Ok(new { eventId = (Guid?)null, deduplicated = true });
        }

        _db.IntegrationEvents.Add(integrationEvent);
        _db.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageType.ClassifyJob,
            JsonSerializer.Serialize(new { eventId = integrationEvent.Id, correlationId = integrationEvent.CorrelationId })));

        await _db.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new { eventId = integrationEvent.Id, isSignatureVerified });
    }

    [HttpPost("deployments")]
    public async Task<IActionResult> IngestDeployment(string provider, Guid integrationId, CancellationToken cancellationToken)
    {
        if (!_registry.TryGetDeploymentConnector(provider, out var connector))
            return NotFound(new { error = $"Bilinmeyen deployment provider: {provider}." });

        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == integrationId, cancellationToken);
        if (integration is null) return NotFound(new { error = "Entegrasyon bulunamadı." });

        var payload = await ReadRawPayloadAsync(cancellationToken);

        if (integration.WebhookSecret is null || !connector.VerifySignature(payload, integration.WebhookSecret))
            return Unauthorized(new { error = "Webhook imzası doğrulanamadı." });

        var normalized = connector.Normalize(payload);

        var changedConfigJson = normalized.ChangedConfig is null
            ? null
            : JsonSerializer.Serialize(normalized.ChangedConfig.Select(c => new { key = c.Key, changed = c.Changed }));

        var deployment = Deployment.Create(
            integrationId,
            normalized.Service,
            normalized.Environment,
            normalized.Commit,
            changedConfigJson,
            normalized.DeployedAt);

        _db.Deployments.Add(deployment);
        await _db.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new { deploymentEventId = deployment.Id });
    }

    private async Task<RawInboundPayload> ReadRawPayloadAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        return new RawInboundPayload(rawBody, headers);
    }
}
