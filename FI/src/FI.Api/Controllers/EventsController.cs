using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FI.Api.Middleware;
using FI.Application.Ingestion;
using FI.Domain.Ingestion;
using FI.Domain.Outbox;
using FI.Domain.Redaction;
using FI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
public class EventsController : ControllerBase
{
    private const int MaxSingleFieldBytes = 256 * 1024;
    private const int SoftTotalPayloadBytes = 64 * 1024;
    private static readonly TimeSpan ContentHashWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdempotencyKeyWindow = TimeSpan.FromDays(7);

    private readonly FiDbContext _db;

    public EventsController(FiDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<IngestEventResponse>> Ingest(
        [FromBody] IngestEventRequest request,
        CancellationToken cancellationToken)
    {
        var integrationId = HttpContext.GetAuthenticatedIntegrationId();
        if (integrationId is null) return Unauthorized();
        var apiKeyId = HttpContext.GetAuthenticatedApiKeyId();
        var correlationId = HttpContext.GetCorrelationId();

        if (integrationId != request.IntegrationId)
            return BadRequest(new { error = "integrationId, kimlik doğrulanan API key'in entegrasyonuyla eşleşmiyor." });

        if (request.StatusCode is < 100 or > 599)
            return UnprocessableEntity(new { error = "statusCode 100-599 aralığında olmalıdır." });

        if (request.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return UnprocessableEntity(new { error = "occurredAt gelecekte olamaz." });

        if (!Enum.TryParse<IntegrationEventType>(request.Type, ignoreCase: true, out var eventType))
            return UnprocessableEntity(new { error = $"Geçersiz type: {request.Type}. Beklenen: ApiCall | WebhookIn | WebhookOut." });

        // Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 33.3 - Aşama A: ham payload hiçbir
        // observability sistemine (burada: veritabanına) redaction'sız yazılmaz.
        var requestJson = RedactToJsonString(request.Request);
        var responseJson = RedactToJsonString(request.Response);

        if (ByteLength(requestJson) > MaxSingleFieldBytes || ByteLength(responseJson) > MaxSingleFieldBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = $"Tek alan {MaxSingleFieldBytes} byte sınırını aşamaz." });

        var totalBytes = ByteLength(requestJson) + ByteLength(responseJson);
        var isTruncated = totalBytes > SoftTotalPayloadBytes;
        if (isTruncated)
        {
            requestJson = null;
            responseJson = null;
        }

        var idempotencyKeyHeader = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var requestHash = ComputeRequestHash(request);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var idempotencyOutcome = await CheckIdempotencyAsync(
            request.IntegrationId, idempotencyKeyHeader, requestHash, IngestionResourceType.Event, cancellationToken);

        if (idempotencyOutcome.Conflict)
            return Conflict(new { error = "Idempotency-Key aynı, ama içerik farklı." });

        if (idempotencyOutcome.ExistingResourceId is { } existingEventId)
            return Ok(new IngestEventResponse(existingEventId, correlationId));

        var integrationEvent = IntegrationEvent.Create(
            request.IntegrationId,
            eventType,
            request.StatusCode,
            requestJson,
            responseJson,
            request.Latency,
            correlationId,
            idempotencyKeyHeader,
            apiKeyId,
            isSignatureVerified: null,
            payloadSizeBytes: totalBytes,
            isTruncated: isTruncated,
            occurredAt: request.OccurredAt);

        _db.IntegrationEvents.Add(integrationEvent);

        var effectiveKey = idempotencyKeyHeader ?? requestHash;
        _db.IngestionIdempotencyKeys.Add(IngestionIdempotencyKey.Create(
            request.IntegrationId, effectiveKey, requestHash, IngestionResourceType.Event, integrationEvent.Id));

        _db.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageType.ClassifyJob,
            JsonSerializer.Serialize(new { eventId = integrationEvent.Id, correlationId })));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new IngestEventResponse(integrationEvent.Id, correlationId));
    }

    private async Task<(bool Conflict, Guid? ExistingResourceId)> CheckIdempotencyAsync(
        Guid integrationId,
        string? idempotencyKeyHeader,
        string requestHash,
        IngestionResourceType resourceType,
        CancellationToken cancellationToken)
    {
        var key = idempotencyKeyHeader ?? requestHash;
        var minCreatedAt = idempotencyKeyHeader is not null
            ? DateTimeOffset.UtcNow - IdempotencyKeyWindow
            : DateTimeOffset.UtcNow - ContentHashWindow;

        var existing = await _db.IngestionIdempotencyKeys
            .Where(k => k.IntegrationId == integrationId && k.IdempotencyKey == key && k.ResourceType == resourceType && k.CreatedAt >= minCreatedAt)
            .OrderByDescending(k => k.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null) return (false, null);

        return existing.RequestHash == requestHash ? (false, existing.ResourceId) : (true, null);
    }

    private static string? RedactToJsonString(object? value)
    {
        if (value is null) return null;
        var node = JsonSerializer.SerializeToNode(value);
        var redacted = PayloadRedactor.RedactJson(node);
        return redacted?.ToJsonString();
    }

    private static int ByteLength(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private static string ComputeRequestHash(IngestEventRequest request)
    {
        var canonical = $"{request.IntegrationId}|{request.Type}|{request.StatusCode}|{request.OccurredAt:O}|" +
                         $"{JsonSerializer.Serialize(request.Request)}|{JsonSerializer.Serialize(request.Response)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
