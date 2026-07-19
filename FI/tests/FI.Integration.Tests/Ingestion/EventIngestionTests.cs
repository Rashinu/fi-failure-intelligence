using System.Net;
using System.Net.Http.Json;
using FI.Application.Ingestion;
using FI.Application.Integrations;
using FI.Infrastructure.Persistence;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FI.Integration.Tests.Ingestion;

public class EventIngestionTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public EventIngestionTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid IntegrationId, string ApiKey)> CreateIntegrationAsync(HttpClient client)
    {
        var createRequest = new CreateIntegrationRequest(
            Name: $"Stripe {Guid.NewGuid():N}",
            Provider: "stripe",
            Environment: "production",
            Owner: "backend-team",
            EndpointUrl: null,
            BusinessCriticality: "High");

        var response = await client.PostAsJsonAsync("/api/v1/integrations", createRequest);
        var created = await response.Content.ReadFromJsonAsync<CreateIntegrationResponse>();
        return (created!.IntegrationId, created.ApiKey);
    }

    [Fact]
    public async Task Ingest_WithoutApiKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/events", new
        {
            integrationId = Guid.NewGuid(),
            type = "ApiCall",
            statusCode = 401,
            occurredAt = DateTimeOffset.UtcNow
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ingest_WithValidApiKey_ReturnsCreatedAndCorrelationIdHeader()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var request = new IngestEventRequest(integrationId, "ApiCall", 401, null, null, 142, DateTimeOffset.UtcNow);
        var response = await client.PostAsJsonAsync("/api/v1/events", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();

        var body = await response.Content.ReadFromJsonAsync<IngestEventResponse>();
        body!.EventId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Ingest_WithClientCorrelationId_EchoesSameId()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        var correlationId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId.ToString());

        var request = new IngestEventRequest(integrationId, "ApiCall", 500, null, null, null, DateTimeOffset.UtcNow);
        var response = await client.PostAsJsonAsync("/api/v1/events", request);

        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be(correlationId.ToString());
    }

    [Fact]
    public async Task Ingest_WithInvalidStatusCode_ReturnsUnprocessableEntity()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var request = new IngestEventRequest(integrationId, "ApiCall", 999, null, null, null, DateTimeOffset.UtcNow);
        var response = await client.PostAsJsonAsync("/api/v1/events", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Ingest_WithFutureOccurredAt_ReturnsUnprocessableEntity()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var request = new IngestEventRequest(integrationId, "ApiCall", 200, null, null, null, DateTimeOffset.UtcNow.AddHours(1));
        var response = await client.PostAsJsonAsync("/api/v1/events", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Ingest_SameIdempotencyKeyTwice_ReturnsSameEventId()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        var idempotencyKey = Guid.NewGuid().ToString();

        var request = new IngestEventRequest(integrationId, "ApiCall", 429, null, null, null, DateTimeOffset.UtcNow);

        using var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/events") { Content = JsonContent.Create(request) };
        msg1.Headers.Add("Idempotency-Key", idempotencyKey);
        var response1 = await client.SendAsync(msg1);
        var body1 = await response1.Content.ReadFromJsonAsync<IngestEventResponse>();

        using var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/events") { Content = JsonContent.Create(request) };
        msg2.Headers.Add("Idempotency-Key", idempotencyKey);
        var response2 = await client.SendAsync(msg2);
        var body2 = await response2.Content.ReadFromJsonAsync<IngestEventResponse>();

        response1.StatusCode.Should().Be(HttpStatusCode.Created);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        body2!.EventId.Should().Be(body1!.EventId);
    }

    [Fact]
    public async Task Ingest_SameIdempotencyKeyDifferentPayload_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        var idempotencyKey = Guid.NewGuid().ToString();

        var request1 = new IngestEventRequest(integrationId, "ApiCall", 429, null, null, null, DateTimeOffset.UtcNow);
        using var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/events") { Content = JsonContent.Create(request1) };
        msg1.Headers.Add("Idempotency-Key", idempotencyKey);
        await client.SendAsync(msg1);

        var request2 = new IngestEventRequest(integrationId, "ApiCall", 500, null, null, null, DateTimeOffset.UtcNow);
        using var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/events") { Content = JsonContent.Create(request2) };
        msg2.Headers.Add("Idempotency-Key", idempotencyKey);
        var response2 = await client.SendAsync(msg2);

        response2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Ingest_ForOtherIntegration_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var (_, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var request = new IngestEventRequest(Guid.NewGuid(), "ApiCall", 200, null, null, null, DateTimeOffset.UtcNow);
        var response = await client.PostAsJsonAsync("/api/v1/events", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 33.3 — Aşama A doğrulaması.</summary>
    [Fact]
    public async Task Ingest_WithSensitiveFieldsInPayload_PersistsRedactedNotRaw()
    {
        var client = _factory.CreateClient();
        var (integrationId, apiKey) = await CreateIntegrationAsync(client);
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var requestBody = new
        {
            headers = new { Authorization = "Bearer sk_live_super_secret_token_abc123" },
            apiKey = "fi_live_should_never_be_stored_raw",
            note = "contact jane.doe@example.com or call +1-555-234-5678"
        };
        var responseBody = new { client_secret = "cs_test_should_be_masked", status = "failed" };

        var request = new IngestEventRequest(integrationId, "ApiCall", 401, requestBody, responseBody, 100, DateTimeOffset.UtcNow);
        var response = await client.PostAsJsonAsync("/api/v1/events", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<IngestEventResponse>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evt = await db.IntegrationEvents.FirstAsync(e => e.Id == body!.EventId);

        evt.RequestRedacted.Should().NotBeNullOrEmpty();
        evt.RequestRedacted.Should().NotContain("sk_live_super_secret_token_abc123");
        evt.RequestRedacted.Should().NotContain("fi_live_should_never_be_stored_raw");
        evt.RequestRedacted.Should().NotContain("jane.doe@example.com");
        evt.RequestRedacted.Should().Contain("[REDACTED]");

        evt.ResponseRedacted.Should().NotBeNullOrEmpty();
        evt.ResponseRedacted.Should().NotContain("cs_test_should_be_masked");
        evt.ResponseRedacted.Should().Contain("[REDACTED]");
    }
}
