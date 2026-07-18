using System.Net.Http.Json;
using FI.Application.Ingestion;
using FI.Application.Integrations;
using FI.Domain.Incidents;
using FI.Infrastructure.Jobs;
using FI.Infrastructure.Persistence;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FI.Integration.Tests.Evidence;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 23 (Evidence Collection).
/// ClassifyJobHandler ve EvidenceCollectorJobHandler DI uzerinden dogrudan cagrilir (Hangfire
/// zamanlamasina bagli kalmamak icin - ayni gerekce M3 testlerinde de kullanildi).
/// </summary>
public class EvidenceCollectionTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public EvidenceCollectionTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid IntegrationId, string ApiKey)> CreateIntegrationAsync(HttpClient client)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/integrations", new CreateIntegrationRequest(
            Name: $"Stripe {Guid.NewGuid():N}",
            Provider: "stripe",
            Environment: "production",
            Owner: "backend-team",
            EndpointUrl: null,
            BusinessCriticality: "Medium"));

        var created = await createResponse.Content.ReadFromJsonAsync<CreateIntegrationResponse>();
        return (created!.IntegrationId, created.ApiKey);
    }

    private async Task<Guid> IngestEventAsync(HttpClient client, Guid integrationId, int statusCode, DateTimeOffset occurredAt)
    {
        var ingestResponse = await client.PostAsJsonAsync("/api/v1/events",
            new IngestEventRequest(integrationId, "ApiCall", statusCode, null, null, 100, occurredAt));
        ingestResponse.EnsureSuccessStatusCode();
        var body = await ingestResponse.Content.ReadFromJsonAsync<IngestEventResponse>();
        return body!.EventId;
    }

    private async Task<Guid> ClassifyAndReturnIncidentIdAsync(Guid eventId)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ClassifyJobHandler>();
        await handler.ExecuteAsync(eventId, Guid.NewGuid());

        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evt = await db.IntegrationEvents.FirstAsync(e => e.Id == eventId);
        var category = Enum.Parse<FI.Domain.Classification.EventCategory>(evt.Category!);
        var incident = await db.Incidents.FirstAsync(i => i.IntegrationId == evt.IntegrationId && i.Category == category);
        return incident.Id;
    }

    private async Task CollectEvidenceAsync(Guid incidentId)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<EvidenceCollectorJobHandler>();
        await handler.ExecuteAsync(incidentId, Guid.NewGuid());
    }

    [Fact]
    public async Task NewIncident_TransitionsToInvestigating_AfterEvidenceCollection()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, integrationId, 401, DateTimeOffset.UtcNow);
        var incidentId = await ClassifyAndReturnIncidentIdAsync(eventId);
        await CollectEvidenceAsync(incidentId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incident = await db.Incidents.FirstAsync(i => i.Id == incidentId);

        incident.Status.Should().Be(IncidentStatus.Investigating);
    }

    [Fact]
    public async Task RecentDeployment_ProducesDeploymentEvidence()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var deployedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        await client.PostAsJsonAsync("/api/v1/deployments", new IngestDeploymentRequest(
            "payments-api", "production", "a1b2c3d", deployedAt, null));

        var eventId = await IngestEventAsync(client, integrationId, 401, DateTimeOffset.UtcNow);
        var incidentId = await ClassifyAndReturnIncidentIdAsync(eventId);
        await CollectEvidenceAsync(incidentId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evidence = await db.IncidentEvidence.Where(e => e.IncidentId == incidentId).ToListAsync();

        evidence.Should().Contain(e => e.SourceType == EvidenceSourceType.Deployment);
    }

    [Fact]
    public async Task PreviousEventsWithinWindow_ProducePreviousEventEvidence()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var baseTime = DateTimeOffset.UtcNow;
        await IngestEventAsync(client, integrationId, 500, baseTime.AddHours(-1));
        await IngestEventAsync(client, integrationId, 500, baseTime.AddMinutes(-30));

        var eventId = await IngestEventAsync(client, integrationId, 401, baseTime);
        var incidentId = await ClassifyAndReturnIncidentIdAsync(eventId);
        await CollectEvidenceAsync(incidentId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evidence = await db.IncidentEvidence.Where(e => e.IncidentId == incidentId).ToListAsync();

        evidence.Should().Contain(e => e.SourceType == EvidenceSourceType.PreviousEvent);
    }

    [Fact]
    public async Task NoDeploymentsOrPreviousEvents_ProducesEmptyEvidenceList_NotFabricated()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, integrationId, 401, DateTimeOffset.UtcNow);
        var incidentId = await ClassifyAndReturnIncidentIdAsync(eventId);
        await CollectEvidenceAsync(incidentId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evidence = await db.IncidentEvidence.Where(e => e.IncidentId == incidentId).ToListAsync();

        evidence.Should().BeEmpty();
    }
}
