using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FI.Api.Middleware;
using FI.Application.Ingestion;
using FI.Domain.Ingestion;
using Microsoft.AspNetCore.Mvc;

namespace FI.Api.Controllers;

[ApiController]
[Route("api/v1/deployments")]
public class DeploymentsController : ControllerBase
{
    private readonly FI.Infrastructure.Persistence.FiDbContext _db;

    public DeploymentsController(FI.Infrastructure.Persistence.FiDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<IngestDeploymentResponse>> Ingest(
        [FromBody] IngestDeploymentRequest request,
        CancellationToken cancellationToken)
    {
        var integrationId = HttpContext.GetAuthenticatedIntegrationId();
        if (integrationId is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Service) || string.IsNullOrWhiteSpace(request.Environment) || string.IsNullOrWhiteSpace(request.Commit))
            return UnprocessableEntity(new { error = "service, environment ve commit zorunludur." });

        if (request.DeployedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return UnprocessableEntity(new { error = "deployedAt gelecekte olamaz." });

        // changedConfig sözleşme gereği yalnızca {key, changed} taşır — değer asla kabul edilmez (Bölüm 18.3).
        var changedConfigJson = request.ChangedConfig is null
            ? null
            : JsonSerializer.Serialize(request.ChangedConfig.Select(c => new { key = c.Key, changed = c.Changed }));

        var deployment = Deployment.Create(
            integrationId,
            request.Service,
            request.Environment,
            request.Commit,
            changedConfigJson,
            request.DeployedAt);

        _db.Deployments.Add(deployment);
        await _db.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new IngestDeploymentResponse(deployment.Id));
    }
}
