using System.Net;
using System.Net.Http.Json;
using FI.Application.Incidents;
using FI.Application.Ingestion;
using FI.Application.Integrations;
using FI.Infrastructure.Jobs;
using FI.Infrastructure.Persistence;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FI.Integration.Tests.Incidents;

/// <summary>
/// Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md P0-B - Incident.Resolve() artık gerçekten
/// ulaşılabilir bir durum; bu testler hem API katmanını hem de "resolved sonrası eşleşen bir
/// failure gelirse ne olur" sorusunu (mevcut Reopen/cooldown mekanizması üzerinden, paralel bir
/// recovery lifecycle olmadan) doğrular.
/// </summary>
public class IncidentResolutionTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public IncidentResolutionTests(FiApiFactory factory)
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

    private async Task<Guid> IngestEventAsync(HttpClient client, Guid integrationId, int statusCode)
    {
        var ingestResponse = await client.PostAsJsonAsync("/api/v1/events",
            new IngestEventRequest(integrationId, "ApiCall", statusCode, null, null, 100, DateTimeOffset.UtcNow));
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

    private async Task<Guid> CreateOpenIncidentAsync(HttpClient client)
    {
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var eventId = await IngestEventAsync(client, integrationId, 401);
        await RunClassifyAsync(eventId, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incident = await db.Incidents.SingleAsync(i => i.IntegrationId == integrationId);
        return incident.Id;
    }

    [Fact]
    public async Task Resolve_OpenIncident_TransitionsToResolvedAndReturnsMetadata()
    {
        var client = _factory.CreateClient();
        var incidentId = await CreateOpenIncidentAsync(client);

        var response = await client.PostAsJsonAsync($"/api/v1/incidents/{incidentId}/resolve",
            new ResolveIncidentRequest("murat", "Credential rotated back, verified."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<IncidentDetailResponse>();
        body!.Status.Should().Be("Resolved");
        body.Resolution.Should().NotBeNull();
        body.Resolution!.ResolvedBy.Should().Be("murat");
        body.Resolution.ResolutionNote.Should().Be("Credential rotated back, verified.");
    }

    [Fact]
    public async Task Resolve_AlreadyResolvedIncident_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var incidentId = await CreateOpenIncidentAsync(client);
        await client.PostAsJsonAsync($"/api/v1/incidents/{incidentId}/resolve", new ResolveIncidentRequest("murat", "first"));

        var second = await client.PostAsJsonAsync($"/api/v1/incidents/{incidentId}/resolve", new ResolveIncidentRequest("someone-else", "second"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Resolve_NonExistentIncident_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/v1/incidents/{Guid.NewGuid()}/resolve", new ResolveIncidentRequest(null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resolve_WithoutAdminCredentials_ReturnsUnauthorized()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/v1/incidents/{Guid.NewGuid()}/resolve", new ResolveIncidentRequest(null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md Preflight Constraint 3: "resolved at 14:20
    /// -> matching failure at 14:22" senaryosu. Burada YENİ bir recovery lifecycle YOK - resolve
    /// sonrası ResolvedAt set edildiği için, ClassifyJobHandler'ın MEVCUT
    /// existingIncident.IsWithinReopenCooldown(now) dalı otomatik olarak Reopen()'a yönlendirir.
    /// </summary>
    [Fact]
    public async Task MatchingFailureAfterResolve_WithinCooldown_ReopensAutomatically()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var firstEventId = await IngestEventAsync(client, integrationId, 401);
        await RunClassifyAsync(firstEventId, Guid.NewGuid());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incidentId = (await db.Incidents.SingleAsync(i => i.IntegrationId == integrationId)).Id;

        var resolveResponse = await client.PostAsJsonAsync($"/api/v1/incidents/{incidentId}/resolve",
            new ResolveIncidentRequest("murat", "Thought it was fixed."));
        resolveResponse.EnsureSuccessStatusCode();

        // Aynı fingerprint ile eşleşen yeni bir failure - cooldown (30dk) içinde.
        var secondEventId = await IngestEventAsync(client, integrationId, 401);
        await RunClassifyAsync(secondEventId, Guid.NewGuid());

        var getResponse = await client.GetAsync($"/api/v1/incidents/{incidentId}");
        var body = await getResponse.Content.ReadFromJsonAsync<IncidentDetailResponse>();

        body!.Status.Should().Be("Reopened");
        body.ReopenCount.Should().Be(1);
        body.Resolution.Should().BeNull("reopen, önceki resolve metadata'sını temizlemeli");
    }
}
