using System.Net.Http.Json;
using FI.Application.Ingestion;
using FI.Application.Integrations;
using FI.Infrastructure.Jobs;
using FI.Infrastructure.Persistence;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FI.Integration.Tests.Classification;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 38.2 (M3 Acceptance Criteria).
/// Outbox dispatcher/Hangfire zamanlamasina bagli kalmamak icin ClassifyJobHandler DI
/// uzerinden dogrudan cagrilir; bu, "outbox mesaji dogru yazildi mi" ile "classify+fingerprint+
/// incident mantigi dogru mu" sorularini ayri, deterministik testler olarak ele alir.
/// </summary>
public class ClassificationToIncidentTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public ClassificationToIncidentTests(FiApiFactory factory)
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

    private async Task<Guid> IngestEventAsync(HttpClient client, string apiKey, Guid integrationId, int statusCode, object? response = null)
    {
        var ingestResponse = await client.PostAsJsonAsync("/api/v1/events",
            new IngestEventRequest(integrationId, "ApiCall", statusCode, null, response, 100, DateTimeOffset.UtcNow));
        ingestResponse.EnsureSuccessStatusCode();
        var body = await ingestResponse.Content.ReadFromJsonAsync<IngestEventResponse>();
        return body!.EventId;
    }

    private async Task RunClassifyAsync(Guid eventId, Guid correlationId)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ClassifyJobHandler>();
        await handler.ExecuteAsync(eventId, correlationId);
    }

    [Fact]
    public async Task RepeatedEvent_UpdatesExistingIncident_DoesNotCreateNew()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        for (var i = 0; i < 5; i++)
        {
            var eventId = await IngestEventAsync(client, apiKey, integrationId, 401);
            await RunClassifyAsync(eventId, Guid.NewGuid());
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incidents = await db.Incidents.Where(i => i.IntegrationId == integrationId).ToListAsync();

        incidents.Should().HaveCount(1);
        incidents[0].EventCount.Should().Be(5);
    }

    [Fact]
    public async Task DifferentRootCauses_ProduceSeparateIncidents()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        for (var i = 0; i < 3; i++)
        {
            var eventId = await IngestEventAsync(client, apiKey, integrationId, 401);
            await RunClassifyAsync(eventId, Guid.NewGuid());
        }

        for (var i = 0; i < 4; i++)
        {
            var eventId = await IngestEventAsync(client, apiKey, integrationId, 503);
            await RunClassifyAsync(eventId, Guid.NewGuid());
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incidents = await db.Incidents.Where(i => i.IntegrationId == integrationId).OrderBy(i => i.Category).ToListAsync();

        incidents.Should().HaveCount(2);
        incidents.Sum(i => i.EventCount).Should().Be(7);
    }

    [Fact]
    public async Task StatusCode401_ClassifiesEventAsAuthenticationError()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, apiKey, integrationId, 401);
        await RunClassifyAsync(eventId, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evt = await db.IntegrationEvents.FirstAsync(e => e.Id == eventId);

        evt.Category.Should().Be("AuthenticationError");
    }

    [Fact]
    public async Task StatusCode500_ClassifiesEventAsProviderError()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, apiKey, integrationId, 500);
        await RunClassifyAsync(eventId, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evt = await db.IntegrationEvents.FirstAsync(e => e.Id == eventId);

        evt.Category.Should().Be("ProviderError");
    }

    [Fact]
    public async Task IngestedEvent_AlwaysWritesOutboxMessageInSameTransaction()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, apiKey, integrationId, 401);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var messages = await db.OutboxMessages.ToListAsync();
        var outboxExists = messages.Any(m => m.Payload.Contains(eventId.ToString()));

        outboxExists.Should().BeTrue();
    }
}
