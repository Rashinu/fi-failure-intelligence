using System.Security.Cryptography;
using FI.Application.Integrations;
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

        _db.Integrations.Add(integration);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = integration.Id },
            new CreateIntegrationResponse(integration.Id, rawKey));
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
        integration.Update(request.Name, request.EndpointUrl, request.Owner, criticality);

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
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
