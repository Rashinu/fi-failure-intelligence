using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace FI.Integration.Tests.Demo;

/// <summary>
/// Bkz. docs/reviews/M20_2_DEMO_AND_SECURITY.md P0-2 - "Static Incident" demo sayfası hiçbir
/// kimlik doğrulaması, hiçbir DB erişimi ve hiçbir yazma/aksiyon içermemeli.
/// </summary>
public class GoldenIncidentDemoPageTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public GoldenIncidentDemoPageTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DemoPage_NoCredentials_Returns200_NotBlockedByAdminAuth()
    {
        // Bilerek CreateUnauthenticatedClient() - AdminBasicAuthMiddleware'in gerçekten bu
        // route'u DIŞARIDA bıraktığını kanıtlamak için (CreateClient() zaten her zaman
        // credential ekliyor, bu yüzden onunla test etmek yanlış pozitif verir).
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/Demo/GoldenIncident");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task DemoPage_ShowsGoldenIncidentNumbers_43Events12Operations7Customers()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var html = await client.GetStringAsync("/Demo/GoldenIncident");

        html.Should().Contain("43");
        html.Should().Contain("12");
        html.Should().Contain("7");
        html.Should().Contain("PaymentService (Prod)");
    }

    [Fact]
    public async Task DemoPage_HasNoResolveFormOrAnyPostAction()
    {
        // Bkz. gereksinim "no destructive actions" - sayfa hiçbir <form> içermemeli.
        var client = _factory.CreateUnauthenticatedClient();

        var html = await client.GetStringAsync("/Demo/GoldenIncident");

        html.Should().NotContain("<form");
    }

    [Fact]
    public async Task DemoPage_PostRequest_NotAllowed()
    {
        // Sayfanın PageModel'inde OnPost yok - bir POST denemesi 404/405 gibi bir "handler yok"
        // durumuna düşmeli, asla bir state değişikliği yapmamalı.
        var client = _factory.CreateUnauthenticatedClient();

        var response = await client.PostAsync("/Demo/GoldenIncident", new StringContent(""));

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task DemoPage_ShowsAllThreeScenarioTabs()
    {
        var client = _factory.CreateUnauthenticatedClient();

        var html = await client.GetStringAsync("/Demo/GoldenIncident");

        html.Should().Contain("Payment failure (Stripe)");
        html.Should().Contain("Webhook signature error (Stripe)");
        html.Should().Contain("Email delivery rate limit (SES)");
        html.Should().Contain("SignatureError");
        html.Should().Contain("RateLimitError");
    }

    [Fact]
    public async Task DemoPage_HasNoLinksToAuthProtectedRoutes()
    {
        // Bkz. canlı tespit edilen gerçek bulgu: sayfa ortak _Layout.cshtml'i (header/nav/footer)
        // kullanıyordu, o da /Incidents, /swagger, /hangfire'a linkler içeriyordu - hepsi
        // AdminBasicAuthMiddleware tarafından korunuyor. Kimliksiz bir demo ziyaretçisi bunlardan
        // birine tıklarsa 401 duvarına çarpıyordu. Artık ayrı bir _DemoLayout kullanıyor - bu link
        // hiç olmamalı.
        var client = _factory.CreateUnauthenticatedClient();

        var html = await client.GetStringAsync("/Demo/GoldenIncident");

        html.Should().NotContain("href=\"/Incidents\"");
        html.Should().NotContain("href=\"/swagger\"");
        html.Should().NotContain("href=\"/hangfire\"");
    }
}
