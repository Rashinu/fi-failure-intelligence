using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FI.Api.Middleware;
using FI.Application.Integrations;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace FI.Integration.Tests.Security;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md - Due Diligence D7: control-plane rotalarının (integrations
/// CRUD, prompt-version promosyonu, incident dashboard, Hangfire dashboard) hiç authentication
/// gerektirmediği doğrulandı ve AdminBasicAuthMiddleware ile kapatıldı. Bu testler kapının
/// kendisini doğruluyor - diğer tüm integration testleri FiApiFactory.CreateClient() üzerinden
/// otomatik olarak geçerli kimlik bilgisiyle çalışır (bkz. FiApiFactory.CreateDefaultClient).
/// </summary>
public class ControlPlaneAuthTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public ControlPlaneAuthTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateIntegration_WithoutCredentials_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/integrations", new CreateIntegrationRequest(
            Name: "no-auth-test", Provider: "stripe", Environment: "production",
            Owner: "test", EndpointUrl: null, BusinessCriticality: "Medium"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateIntegration_WithWrongSecret_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:wrong-secret")));

        var response = await client.PostAsJsonAsync("/api/v1/integrations", new CreateIntegrationRequest(
            Name: "wrong-auth-test", Provider: "stripe", Environment: "production",
            Owner: "test", EndpointUrl: null, BusinessCriticality: "Medium"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateIntegration_WithCorrectSharedSecret_Succeeds()
    {
        var client = _factory.CreateClient(); // FiApiFactory varsayılan olarak doğru kimlik bilgisini ekliyor

        var response = await client.PostAsJsonAsync("/api/v1/integrations", new CreateIntegrationRequest(
            Name: "correct-auth-test", Provider: "stripe", Environment: "production",
            Owner: "test", EndpointUrl: null, BusinessCriticality: "Medium"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task IncidentDashboard_WithoutCredentials_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/Incidents");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Bulunan ek açık: /api/v1/incidents (JSON API), gated /Incidents Razor dashboard'uyla
    /// birebir aynı veriyi dönüyordu ama D7'nin ilk kapsamında (raporun kendi route listesi)
    /// yer almadığı için kimliksiz erişilebilir kalmıştı - dashboard'u gate'lemenin amacını
    /// boşa çıkaran bir bypass. Kullanıcı onayıyla kapsama eklendi.
    /// </summary>
    [Fact]
    public async Task IncidentsJsonApi_WithoutCredentials_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/v1/incidents");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IncidentsJsonApi_WithCorrectSharedSecret_Succeeds()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/incidents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HangfireDashboard_WithoutCredentials_Returns401()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/hangfire");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EventIngestion_IsNotGatedByAdminAuth_UsesApiKeyInstead()
    {
        // Ingestion endpoint'leri (ApiKeyAuthMiddleware ile korunuyor) admin auth kapsamı DIŞINDA -
        // kimlik bilgisi olmadan çağrıldığında admin-401 değil, eksik-X-Api-Key 401'i dönmeli.
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/events", new object());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("X-Api-Key", "admin-auth değil, API-key middleware'i reddetmiş olmalı");
    }
}
