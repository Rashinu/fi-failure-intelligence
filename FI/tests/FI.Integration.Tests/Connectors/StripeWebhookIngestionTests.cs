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

    /// <summary>Bkz. docs/reviews/M20_2_DEMO_AND_SECURITY.md P0-1 - bir integration oluşturur ama
    /// hiçbir webhook göndermez; testlerin kendi isteğini kurmasına yardımcı, event/incident
    /// oluşup oluşmadığını doğrulamak için ortak bir yardımcı.</summary>
    private async Task<CreateIntegrationResponse> CreateStripeIntegrationAsync(HttpClient client, string criticality = "Medium")
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/integrations", new CreateIntegrationRequest(
            Name: $"Stripe Webhook {Guid.NewGuid():N}",
            Provider: "stripe",
            Environment: "production",
            Owner: "backend-team",
            EndpointUrl: null,
            BusinessCriticality: criticality));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreateIntegrationResponse>();
        created.Should().NotBeNull();
        return created!;
    }

    private async Task<int> CountEventsAsync(Guid integrationId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        return await db.IntegrationEvents.CountAsync(e => e.IntegrationId == integrationId);
    }

    [Fact]
    public async Task StripeWebhook_InvalidSignature_Returns401_NoEventCreated()
    {
        // Bkz. M20.2 onaylanan admisyon politikası - imza yanlışsa istek 401 ile reddedilir,
        // hiçbir IntegrationEvent/incident hiç oluşturulmaz (eski davranış: kabul edip
        // SIGNATURE_ERROR olarak kaydediyordu - bu, canlı bir pentest'te dashboard-spam riski
        // olarak tespit edildi).
        var client = _factory.CreateClient();
        var created = await CreateStripeIntegrationAsync(client);

        var body = """{"type":"charge.failed","httpStatusCode":401,"data":{"object":{"id":"ch_bad"}}}""";
        var wrongSignature = Sign(body, "wrong-secret-entirely");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/stripe/{created.IntegrationId}/events")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", wrongSignature);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        (await CountEventsAsync(created.IntegrationId)).Should().Be(0);
    }

    [Fact]
    public async Task StripeWebhook_MissingSignatureHeader_Returns401_NoEventCreated()
    {
        var client = _factory.CreateClient();
        var created = await CreateStripeIntegrationAsync(client);

        var body = """{"type":"charge.failed","httpStatusCode":401,"data":{"object":{"id":"ch_no_sig"}}}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/stripe/{created.IntegrationId}/events")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        // Bilerek Stripe-Signature header'ı hiç eklenmedi.

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        (await CountEventsAsync(created.IntegrationId)).Should().Be(0);
    }

    [Fact]
    public async Task StripeWebhook_ExpiredTimestamp_Returns401_NoEventCreated()
    {
        // Bkz. StripeConnector.VerifySignature - 5 dakikalık tolerans penceresi dışındaki
        // timestamp'ler reddediliyor (doğru HMAC'la imzalanmış olsalar bile).
        var client = _factory.CreateClient();
        var created = await CreateStripeIntegrationAsync(client);

        var body = """{"type":"charge.failed","httpStatusCode":401,"data":{"object":{"id":"ch_expired"}}}""";
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var signedPayload = $"{oldTimestamp}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(created.WebhookSecret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/stripe/{created.IntegrationId}/events")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", $"t={oldTimestamp},v1={signature}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        (await CountEventsAsync(created.IntegrationId)).Should().Be(0);
    }

    [Fact]
    public async Task StripeWebhook_ValidSignature_LegitimateRetryWithSameProviderId_StillDeduplicates()
    {
        // Bkz. M20.2 gereksinimi - "meşru retry'ları zayıflatma". Aynı, GEÇERLİ şekilde
        // imzalanmış event iki kez gönderilirse (gerçek bir sağlayıcı retry senaryosu), ikinci
        // istek hâlâ 200 + deduplicated=true dönmeli - 401 ile reddedilmemeli, ve DB'de tek bir
        // event olmalı (401 reddi yalnızca DOĞRULANAMAYAN isteklere uygulanıyor).
        var client = _factory.CreateClient();
        var created = await CreateStripeIntegrationAsync(client);

        var body = """{"type":"charge.failed","httpStatusCode":401,"data":{"object":{"id":"ch_retry_1"}},"error":{"code":"invalid_api_key"}}""";

        async Task<HttpResponseMessage> SendAsync()
        {
            var signature = Sign(body, created.WebhookSecret);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/stripe/{created.IntegrationId}/events")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Stripe-Signature", signature);
            return await client.SendAsync(request);
        }

        var first = await SendAsync();
        first.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        var second = await SendAsync();
        second.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var secondJson = await second.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        secondJson.GetProperty("deduplicated").GetBoolean().Should().BeTrue();

        (await CountEventsAsync(created.IntegrationId)).Should().Be(1);
    }

    [Fact]
    public async Task StripeWebhook_MalformedJsonPayload_WithInvalidSignature_Returns401_NotRaw500()
    {
        // Bkz. GlobalExceptionHandler (M20.1) - imza kontrolü JSON parse'dan ÖNCE çalıştığı için
        // (Normalize hiç çağrılmıyor), bozuk JSON + geçersiz imza kombinasyonu hâlâ temiz bir 401
        // döner, ham bir 500/stack trace değil.
        var client = _factory.CreateClient();
        var created = await CreateStripeIntegrationAsync(client);

        var malformedBody = "{ this is not valid json at all ][";
        var wrongSignature = Sign(malformedBody, "wrong-secret-entirely");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/stripe/{created.IntegrationId}/events")
        {
            Content = new StringContent(malformedBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", wrongSignature);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        (await CountEventsAsync(created.IntegrationId)).Should().Be(0);
    }

    [Fact]
    public async Task StripeWebhook_LargePayload_WithInvalidSignature_StillRejectedNotAccepted()
    {
        // Bkz. M20.2 test listesi "large payload" - burada asıl doğrulanan şey, büyük bir
        // payload'ın imza reddini ATLATMADIĞI (ör. bir boyut sınırı istisnası yüzünden yanlışlıkla
        // kabul edilmediği) - gerçek bir DoS/kapasite testi DEĞİL (canlı, paylaşılan bir ortamda
        // kasıtlı olarak yapılmadı, bkz. LIVE_THREE_PERSPECTIVE_TEST.md).
        var client = _factory.CreateClient();
        var created = await CreateStripeIntegrationAsync(client);

        var largeField = new string('a', 200_000);
        var body = "{\"type\":\"charge.failed\",\"httpStatusCode\":401,\"data\":{\"object\":{\"id\":\"ch_large\",\"note\":\"" + largeField + "\"}}}";
        var wrongSignature = Sign(body, "wrong-secret-entirely");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/stripe/{created.IntegrationId}/events")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", wrongSignature);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        (await CountEventsAsync(created.IntegrationId)).Should().Be(0);
    }
}
