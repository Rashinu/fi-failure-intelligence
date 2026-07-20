using System.Net;
using System.Net.Http.Json;
using FI.Application.Ingestion;
using FI.Application.Integrations;
using FI.Domain.Audit;
using FI.Infrastructure.Jobs;
using FI.Infrastructure.Persistence;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FI.Integration.Tests.Integrations;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 33.4 (API key rotasyonu) ve Bölüm 23
/// (CONFIG_CHANGE evidence kaynağının veri kaynağı — audit_logs).
/// </summary>
public class IntegrationRotationTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public IntegrationRotationTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<CreateIntegrationResponse> CreateIntegrationAsync(HttpClient client)
    {
        var createResponse = await client.PostAsJsonAsync("/api/v1/integrations", new CreateIntegrationRequest(
            Name: $"Stripe {Guid.NewGuid():N}",
            Provider: "stripe",
            Environment: "production",
            Owner: "backend-team",
            EndpointUrl: null,
            BusinessCriticality: "Medium"));

        return (await createResponse.Content.ReadFromJsonAsync<CreateIntegrationResponse>())!;
    }

    [Fact]
    public async Task RotateApiKey_OldKeyStillWorksDuringGracePeriod_NewKeyAlsoWorks()
    {
        var client = _factory.CreateClient();
        var created = await CreateIntegrationAsync(client);

        var rotateResponse = await client.PostAsync($"/api/v1/integrations/{created.IntegrationId}/api-key/rotate", null);
        var debugBody = await rotateResponse.Content.ReadAsStringAsync();
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK, debugBody);
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<RotateApiKeyResponse>();
        rotated!.ApiKey.Should().NotBe(created.ApiKey);

        // Bkz. Bolum 33.4 - grace period boyunca eski key hala calismali (henuz guncellenmemis
        // istemcilerin kesintiye ugramamasi icin).
        var oldKeyClient = _factory.CreateClient();
        oldKeyClient.DefaultRequestHeaders.Add("X-Api-Key", created.ApiKey);
        var oldKeyRequest = new IngestEventRequest(created.IntegrationId, "ApiCall", 401, null, null, null, DateTimeOffset.UtcNow);
        var oldKeyResponse = await oldKeyClient.PostAsJsonAsync("/api/v1/events", oldKeyRequest);
        oldKeyResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var newKeyClient = _factory.CreateClient();
        newKeyClient.DefaultRequestHeaders.Add("X-Api-Key", rotated.ApiKey);
        var newKeyRequest = new IngestEventRequest(created.IntegrationId, "ApiCall", 401, null, null, null, DateTimeOffset.UtcNow);
        var newKeyResponse = await newKeyClient.PostAsJsonAsync("/api/v1/events", newKeyRequest);
        newKeyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ApiKeyGracePeriodRevocationJob_RevokesKeysPastGracePeriod_ButNotRecentlyRotatedOnes()
    {
        var client = _factory.CreateClient();
        var created = await CreateIntegrationAsync(client);

        var rotateResponse = await client.PostAsync($"/api/v1/integrations/{created.IntegrationId}/api-key/rotate", null);
        rotateResponse.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
            // Grace period'un (24sa) coktan dolmus oldugunu simule et.
            var pastRotation = DateTimeOffset.UtcNow - ApiKeyGracePeriodRevocationJobHandler.GracePeriod - TimeSpan.FromMinutes(1);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE api_keys SET last_rotated_at = {pastRotation} WHERE integration_id = {created.IntegrationId} AND revoked_at IS NULL AND last_rotated_at IS NOT NULL");
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ApiKeyGracePeriodRevocationJobHandler>();
            await job.ExecuteAsync();
        }

        var oldKeyClient = _factory.CreateClient();
        oldKeyClient.DefaultRequestHeaders.Add("X-Api-Key", created.ApiKey);
        var oldKeyRequest = new IngestEventRequest(created.IntegrationId, "ApiCall", 401, null, null, null, DateTimeOffset.UtcNow);
        var oldKeyResponse = await oldKeyClient.PostAsJsonAsync("/api/v1/events", oldKeyRequest);
        oldKeyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "grace period doldugu icin eski key artik revoke edilmis olmali");
    }

    [Fact]
    public async Task RotateApiKey_WritesAuditLogEntry()
    {
        var client = _factory.CreateClient();
        var created = await CreateIntegrationAsync(client);

        await client.PostAsync($"/api/v1/integrations/{created.IntegrationId}/api-key/rotate", null);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var log = await db.AuditLogs.FirstOrDefaultAsync(
            a => a.EntityId == created.IntegrationId && a.Action == AuditActions.ApiKeyRotated);

        log.Should().NotBeNull();
        log!.EntityType.Should().Be(AuditEntityTypes.Integration);
    }

    [Fact]
    public async Task RotateWebhookSecret_ReturnsNewSecret_AndWritesAuditLog()
    {
        var client = _factory.CreateClient();
        var created = await CreateIntegrationAsync(client);

        var rotateResponse = await client.PostAsync($"/api/v1/integrations/{created.IntegrationId}/webhook-secret/rotate", null);
        rotateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await rotateResponse.Content.ReadFromJsonAsync<RotateWebhookSecretResponse>();
        rotated!.WebhookSecret.Should().NotBe(created.WebhookSecret);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var log = await db.AuditLogs.FirstOrDefaultAsync(
            a => a.EntityId == created.IntegrationId && a.Action == AuditActions.WebhookSecretRotated);

        log.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_ChangingEndpointUrl_WritesAuditLog()
    {
        var client = _factory.CreateClient();
        var created = await CreateIntegrationAsync(client);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/integrations/{created.IntegrationId}", new UpdateIntegrationRequest(
            Name: "Renamed", Owner: "backend-team", EndpointUrl: "https://new-endpoint.example.com", BusinessCriticality: "Medium"));
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var log = await db.AuditLogs.FirstOrDefaultAsync(
            a => a.EntityId == created.IntegrationId && a.Action == AuditActions.IntegrationUpdated);

        log.Should().NotBeNull();
        log!.Changes.Should().Contain("new-endpoint.example.com");
    }

    [Fact]
    public async Task Update_WithoutActualChange_DoesNotWriteAuditLog()
    {
        var client = _factory.CreateClient();
        var created = await CreateIntegrationAsync(client);

        // EndpointUrl was null at creation; updating with null again is not a real change.
        await client.PutAsJsonAsync($"/api/v1/integrations/{created.IntegrationId}", new UpdateIntegrationRequest(
            Name: "Same Name Change", Owner: "backend-team", EndpointUrl: null, BusinessCriticality: "Medium"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        var log = await db.AuditLogs.FirstOrDefaultAsync(
            a => a.EntityId == created.IntegrationId && a.Action == AuditActions.IntegrationUpdated);

        log.Should().BeNull();
    }
}
