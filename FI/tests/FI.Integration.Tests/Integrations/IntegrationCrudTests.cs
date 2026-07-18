using System.Net;
using System.Net.Http.Json;
using FI.Application.Integrations;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace FI.Integration.Tests.Integrations;

public class IntegrationCrudTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public IntegrationCrudTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_Then_GetById_ReturnsSameIntegration()
    {
        var client = _factory.CreateClient();

        var createRequest = new CreateIntegrationRequest(
            Name: $"Stripe {Guid.NewGuid():N}",
            Provider: "stripe",
            Environment: "production",
            Owner: "backend-team",
            EndpointUrl: "https://api.stripe.com",
            BusinessCriticality: "High");

        var createResponse = await client.PostAsJsonAsync("/api/v1/integrations", createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await createResponse.Content.ReadFromJsonAsync<CreateIntegrationResponse>();
        created.Should().NotBeNull();
        created!.ApiKey.Should().StartWith("fi_live_");

        var getResponse = await client.GetAsync($"/api/v1/integrations/{created.IntegrationId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await getResponse.Content.ReadFromJsonAsync<IntegrationResponse>();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.IntegrationId);
        fetched.Name.Should().Be(createRequest.Name);
        fetched.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Delete_ArchivesIntegration()
    {
        var client = _factory.CreateClient();

        var createRequest = new CreateIntegrationRequest(
            Name: $"GitHub {Guid.NewGuid():N}",
            Provider: "github",
            Environment: "production",
            Owner: "devops-team",
            EndpointUrl: null,
            BusinessCriticality: "Medium");

        var createResponse = await client.PostAsJsonAsync("/api/v1/integrations", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateIntegrationResponse>();

        var deleteResponse = await client.DeleteAsync($"/api/v1/integrations/{created!.IntegrationId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await client.GetAsync($"/api/v1/integrations/{created.IntegrationId}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<IntegrationResponse>();
        fetched!.Status.Should().Be("Archived");
    }

    [Fact]
    public async Task GetById_WithUnknownId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/integrations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
