using System.Security.Cryptography;
using System.Text.Json;
using FI.Api.Middleware;
using FI.Application.Integrations;
using FI.Domain.Audit;
using FI.Domain.Ingestion;
using FI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Controllers;

[ApiController]
[Route("api/v1/integrations")]
public class IntegrationsController : ControllerBase
{
    private readonly FiDbContext _db;
    private readonly IConfiguration _configuration;

    public IntegrationsController(FiDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<ActionResult<CreateIntegrationResponse>> Create(
        [FromBody] CreateIntegrationRequest request,
        CancellationToken cancellationToken)
    {
        var criticality = ParseCriticality(request.BusinessCriticality);

        var integration = Integration.Create(
            request.Name,
            request.Provider,
            request.Environment,
            request.Owner,
            request.EndpointUrl,
            criticality);

        var pepper = _configuration["ApiKeys:Pepper"] ?? "local-dev-pepper-change-me";
        var (rawKey, keyPrefix, keyHash) = GenerateApiKey(pepper);
        integration.IssueApiKey(keyPrefix, keyHash);

        var webhookSecret = GenerateWebhookSecret();
        integration.IssueWebhookSecret(webhookSecret);

        _db.Integrations.Add(integration);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = integration.Id },
            new CreateIntegrationResponse(integration.Id, rawKey, webhookSecret));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<IntegrationResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var integrations = await _db.Integrations
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(integrations.Select(ToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<IntegrationResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var integration = await _db.Integrations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        return integration is null ? NotFound() : Ok(ToResponse(integration));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIntegrationRequest request, CancellationToken cancellationToken)
    {
        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (integration is null) return NotFound();

        var criticality = ParseCriticality(request.BusinessCriticality);
        var oldEndpointUrl = integration.EndpointUrl;
        integration.Update(request.Name, request.EndpointUrl, request.Owner, criticality);

        // Bkz. Bolum 23 - CONFIG_CHANGE evidence kaynagi bu audit kaydini okur. Yalnizca gercek
        // bir degisiklik oldugunda yazilir (no-op update'ler yanlis sinyal uretmemeli).
        if (oldEndpointUrl != request.EndpointUrl)
        {
            _db.AuditLogs.Add(AuditLog.Create(
                AuditActorType.User, actorId: null, AuditActions.IntegrationUpdated, AuditEntityTypes.Integration,
                integration.Id, HttpContext.GetCorrelationId(),
                JsonSerializer.Serialize(new { field = "endpointUrl", oldValue = oldEndpointUrl, newValue = request.EndpointUrl })));
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Bkz. Bölüm 33.4 — eski key(ler) anında revoke edilir (grace period sadeleştirildi, bkz. Integration.RotateApiKey).</summary>
    [HttpPost("{id:guid}/api-key/rotate")]
    public async Task<ActionResult<RotateApiKeyResponse>> RotateApiKey(Guid id, CancellationToken cancellationToken)
    {
        var integration = await _db.Integrations.Include(i => i.ApiKeys).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (integration is null) return NotFound();

        var pepper = _configuration["ApiKeys:Pepper"] ?? "local-dev-pepper-change-me";
        var (rawKey, keyPrefix, keyHash) = GenerateApiKey(pepper);
        var newApiKey = integration.RotateApiKey(keyPrefix, keyHash, DateTimeOffset.UtcNow);

        // EF Core sadece "yeni eklenen çocuk"ı, ebeveyn Added durumundaysa cascade ile Added
        // işaretler. Burada Integration zaten var olan (Unchanged) bir varlık olduğundan,
        // koleksiyona eklenen yeni ApiKey (önceden atanmış Guid PK'sıyla) DetectChanges
        // tarafından yanlışlıkla Unchanged/Modified sanılır ve var olmayan bir satırı UPDATE
        // etmeye çalışıp DbUpdateConcurrencyException fırlatır. Açıkça Added işaretlenmesi gerekir.
        _db.ApiKeys.Add(newApiKey);

        _db.AuditLogs.Add(AuditLog.Create(
            AuditActorType.User, actorId: null, AuditActions.ApiKeyRotated, AuditEntityTypes.Integration,
            integration.Id, HttpContext.GetCorrelationId(),
            JsonSerializer.Serialize(new { newKeyPrefix = keyPrefix })));

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new RotateApiKeyResponse(integration.Id, rawKey));
    }

    [HttpPost("{id:guid}/webhook-secret/rotate")]
    public async Task<ActionResult<RotateWebhookSecretResponse>> RotateWebhookSecret(Guid id, CancellationToken cancellationToken)
    {
        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (integration is null) return NotFound();

        var webhookSecret = GenerateWebhookSecret();
        integration.IssueWebhookSecret(webhookSecret);

        _db.AuditLogs.Add(AuditLog.Create(
            AuditActorType.User, actorId: null, AuditActions.WebhookSecretRotated, AuditEntityTypes.Integration,
            integration.Id, HttpContext.GetCorrelationId(), changes: null));

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new RotateWebhookSecretResponse(integration.Id, webhookSecret));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        var integration = await _db.Integrations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (integration is null) return NotFound();

        integration.Archive();
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static BusinessCriticality ParseCriticality(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? BusinessCriticality.Medium
            : Enum.Parse<BusinessCriticality>(value, ignoreCase: true);

    /// <summary>
    /// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 33.4 — key_hash HMAC-SHA256 + pepper ile hesaplanır, ham key asla saklanmaz.
    /// </summary>
    private static (string RawKey, string KeyPrefix, string KeyHash) GenerateApiKey(string pepper)
    {
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(secretBytes).Replace("+", "").Replace("/", "").Replace("=", "");
        var rawKey = $"fi_live_{secret}";
        var keyPrefix = rawKey[..Math.Min(12, rawKey.Length)];

        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(pepper));
        var keyHash = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(rawKey)));
        return (rawKey, keyPrefix, keyHash);
    }

    /// <summary>Bkz. Bölüm 34 — webhook imza doğrulaması için paylaşılan sır (API key'den ayrı).</summary>
    private static string GenerateWebhookSecret()
    {
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        return $"whsec_{Convert.ToBase64String(secretBytes).Replace("+", "").Replace("/", "").Replace("=", "")}";
    }

    private static IntegrationResponse ToResponse(Integration integration) => new(
        integration.Id,
        integration.Name,
        integration.Provider,
        integration.Environment,
        integration.Owner,
        integration.EndpointUrl,
        integration.BusinessCriticality.ToString(),
        integration.Status.ToString(),
        integration.CreatedAt,
        integration.UpdatedAt);
}
