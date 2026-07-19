using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FI.Application.Integrations;
using FI.Infrastructure.Jobs;
using FI.Infrastructure.Persistence;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FI.Integration.Tests.Connectors;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 35 — "Stripe Webhook Auth Patlaması" demo
/// senaryosu: API key rotasyonu sonrası art arda 401'ler, connector üzerinden gelir, imzası
/// doğrulanır ve tek bir incident'a toplanır. ClassifyJobHandler, ClassificationToIncidentTests'teki
/// pattern ile Hangfire zamanlamasından bağımsız olarak doğrudan DI üzerinden çağrılır.
/// </summary>
public class StripeWebhookIngestionTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public StripeWebhookIngestionTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    private static string Sign(string rawBody, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();
        return $"t={timestamp},v1={signature}";
    }

    private async Task RunClassifyAsync(Guid eventId, Guid correlationId)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ClassifyJobHandler>();
        await handler.ExecuteAsync(eventId, correlationId);
    }

    [Fact]
    public async Task RepeatedStripeAuthWebhooks_VerifiedSignature_ProduceSingleAuthenticationErrorIncident()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/integrations", new CreateIntegrationRequest(
            Name: $"Stripe Webhook {Guid.NewGuid():N}",
            Provider: "stripe",
            Environment: "production",
            Owner: "backend-team",
            EndpointUrl: null,
            BusinessCriticality: "Critical"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateIntegrationResponse>();
        created.Should().NotBeNull();

        var eventIds = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var body = "{\"type\":\"charge.failed\",\"httpStatusCode\":401,\"data\":{\"object\":{\"id\":\"ch_" + i + "\"}},\"error\":{\"code\":\"invalid_api_key\"}}";
            var signature = Sign(body, created!.WebhookSecret);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/stripe/{created.IntegrationId}/events")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Stripe-Signature", signature);

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            json.GetProperty("isSignatureVerified").GetBoolean().Should().BeTrue();
            eventIds.Add(json.GetProperty("eventId").GetGuid());
        }

        foreach (var eventId in eventIds)
        {
            await RunClassifyAsync(eventId, Guid.NewGuid());
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var incidents = await db.Incidents.Where(i => i.IntegrationId == created!.IntegrationId).ToListAsync();

        incidents.Should().HaveCount(1);
        incidents[0].Category.ToString().Should().Be("AuthenticationError");
        incidents[0].EventCount.Should().Be(6);

        var events = await db.IntegrationEvents.Where(e => e.IntegrationId == created!.IntegrationId).ToListAsync();
        events.Should().OnlyContain(e => e.IsSignatureVerified == true);
    }

    [Fact]
    public async Task StripeWebhook_InvalidSignature_StillIngested_MarkedUnverified()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/v1/integrations", new CreateIntegrationRequest(
            Name: $"Stripe Webhook {Guid.NewGuid():N}",
            Provider: "stripe",
            Environment: "production",
            Owner: "backend-team",
            EndpointUrl: null,
            BusinessCriticality: "Medium"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateIntegrationResponse>();

        var body = """{"type":"charge.failed","httpStatusCode":401,"data":{"object":{"id":"ch_bad"}}}""";
        var wrongSignature = Sign(body, "wrong-secret-entirely");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/stripe/{created!.IntegrationId}/events")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", wrongSignature);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        json.GetProperty("isSignatureVerified").GetBoolean().Should().BeFalse();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var evt = await db.IntegrationEvents.FirstAsync(e => e.IntegrationId == created.IntegrationId);
        evt.IsSignatureVerified.Should().BeFalse();
    }
}
