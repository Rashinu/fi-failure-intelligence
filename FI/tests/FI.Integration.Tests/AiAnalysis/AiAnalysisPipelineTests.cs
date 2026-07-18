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

namespace FI.Integration.Tests.AiAnalysis;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 24, 25, 26. Gercek Anthropic API
/// cagrisi yerine FiApiFactory.FakeAiClient kullanilir. Job'lar Hangfire zamanlamasina bagli
/// kalmamak icin DI uzerinden dogrudan cagrilir (M3-M4 testlerindeki gerekce ile ayni).
/// </summary>
public class AiAnalysisPipelineTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public AiAnalysisPipelineTests(FiApiFactory factory)
    {
        _factory = factory;
        _factory.FakeAiClient.NextResponseOverride = null;
        _factory.FakeAiClient.SimulateCallFailure = false;
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

    private async Task<Guid> RunFullPipelineAsync(HttpClient client, Guid integrationId, DateTimeOffset deployedAt)
    {
        await client.PostAsJsonAsync("/api/v1/deployments", new IngestDeploymentRequest(
            "payments-api", "production", "a1b2c3d", deployedAt, null));

        var eventId = await IngestEventAsync(client, integrationId, 401, DateTimeOffset.UtcNow);

        using var scope = _factory.Services.CreateScope();
        var classify = scope.ServiceProvider.GetRequiredService<ClassifyJobHandler>();
        await classify.ExecuteAsync(eventId, Guid.NewGuid());

        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evt = await db.IntegrationEvents.FirstAsync(e => e.Id == eventId);
        var category = Enum.Parse<FI.Domain.Classification.EventCategory>(evt.Category!);
        var incident = await db.Incidents.FirstAsync(i => i.IntegrationId == integrationId && i.Category == category);

        var evidenceHandler = scope.ServiceProvider.GetRequiredService<EvidenceCollectorJobHandler>();
        await evidenceHandler.ExecuteAsync(incident.Id, Guid.NewGuid());

        var aiHandler = scope.ServiceProvider.GetRequiredService<AiAnalysisJobHandler>();
        await aiHandler.ExecuteAsync(incident.Id, Guid.NewGuid());

        return incident.Id;
    }

    [Fact]
    public async Task FullPipeline_WithEvidence_ProducesAiAnalyzedIncidentWithAnalysis()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var incidentId = await RunFullPipelineAsync(client, integrationId, DateTimeOffset.UtcNow.AddMinutes(-30));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incident = await db.Incidents.FirstAsync(i => i.Id == incidentId);
        var analysis = await db.AiAnalyses.FirstOrDefaultAsync(a => a.IncidentId == incidentId && a.IsLatest);

        incident.Status.Should().Be(IncidentStatus.AiAnalyzed);
        analysis.Should().NotBeNull();
        analysis!.NeedsHumanReview.Should().BeFalse();
        analysis.ProbableRootCause.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetIncidentDetail_AfterAnalysis_ReturnsLatestAnalysis()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var incidentId = await RunFullPipelineAsync(client, integrationId, DateTimeOffset.UtcNow.AddMinutes(-30));

        var response = await client.GetAsync($"/api/v1/incidents/{incidentId}");
        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("latestAnalysis");
        json.Should().Contain("probableRootCause");
    }

    [Fact]
    public async Task AiCallFailure_MarksIncidentNeedsHumanReview_AndLogsFailure()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        _factory.FakeAiClient.SimulateCallFailure = true;
        try
        {
            var incidentId = await RunFullPipelineAsync(client, integrationId, DateTimeOffset.UtcNow.AddMinutes(-30));

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
            var incident = await db.Incidents.FirstAsync(i => i.Id == incidentId);
            var logs = await db.AiAnalysisLogs.Where(l => l.IncidentId == incidentId).ToListAsync();

            incident.Status.Should().Be(IncidentStatus.NeedsHumanReview);
            logs.Should().Contain(l => !l.ParseSuccess && l.ErrorMessage != null);
        }
        finally
        {
            _factory.FakeAiClient.SimulateCallFailure = false;
        }
    }

    [Fact]
    public async Task MalformedAiResponse_MarksIncidentNeedsHumanReview_WithoutCreatingAnalysis()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        _factory.FakeAiClient.NextResponseOverride = "not valid json {{{";
        try
        {
            var incidentId = await RunFullPipelineAsync(client, integrationId, DateTimeOffset.UtcNow.AddMinutes(-30));

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
            var incident = await db.Incidents.FirstAsync(i => i.Id == incidentId);
            var analysis = await db.AiAnalyses.FirstOrDefaultAsync(a => a.IncidentId == incidentId);

            incident.Status.Should().Be(IncidentStatus.NeedsHumanReview);
            analysis.Should().BeNull();
        }
        finally
        {
            _factory.FakeAiClient.NextResponseOverride = null;
        }
    }

    [Fact]
    public async Task NoEvidence_SkipsAiCallEntirely_MarksNeedsHumanReview()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, integrationId, 401, DateTimeOffset.UtcNow);

        using var scope = _factory.Services.CreateScope();
        var classify = scope.ServiceProvider.GetRequiredService<ClassifyJobHandler>();
        await classify.ExecuteAsync(eventId, Guid.NewGuid());

        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evt = await db.IntegrationEvents.FirstAsync(e => e.Id == eventId);
        var category = Enum.Parse<FI.Domain.Classification.EventCategory>(evt.Category!);
        var incident = await db.Incidents.FirstAsync(i => i.IntegrationId == integrationId && i.Category == category);

        // Evidence toplanmadan (StartInvestigating hic cagrilmadan) dogrudan AI job'i cagir.
        var aiHandler = scope.ServiceProvider.GetRequiredService<AiAnalysisJobHandler>();
        await aiHandler.ExecuteAsync(incident.Id, Guid.NewGuid());

        var refreshed = await db.Incidents.FirstAsync(i => i.Id == incident.Id);
        refreshed.Status.Should().Be(IncidentStatus.NeedsHumanReview);
    }
}
