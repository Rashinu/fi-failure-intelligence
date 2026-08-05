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
}
