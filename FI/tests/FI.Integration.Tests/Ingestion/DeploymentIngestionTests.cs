using System.Net;
using System.Net.Http.Json;
using FI.Application.Ingestion;
using FI.Application.Integrations;
using FI.Integration.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace FI.Integration.Tests.Ingestion;

public class DeploymentIngestionTests : IClassFixture<FiApiFactory>
{
    private readonly FiApiFactory _factory;

    public DeploymentIngestionTests(FiApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ingest_WithValidApiKey_ReturnsCreated()
    {
        var client = _factory.CreateClient();

        var createRequest = new CreateIntegrationRequest(
            Name: $"GitHub {Guid.NewGuid():N}", Provider: "github", Environment: "production",
            Owner: "devops-team", EndpointUrl: null, BusinessCriticality: "Medium");
        var createResponse = await client.PostAsJsonAsync("/api/v1/integrations", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateIntegrationResponse>();

        client.DefaultRequestHeaders.Add("X-Api-Key", created!.ApiKey);

        var request = new IngestDeploymentRequest(
            "payments-api", "production", "a1b2c3d", DateTimeOffset.UtcNow,
            new[] { new ChangedConfigEntry("STRIPE_SECRET_KEY", true) });

        var response = await client.PostAsJsonAsync("/api/v1/deployments", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<IngestDeploymentResponse>();
        body!.DeploymentEventId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Ingest_WithoutApiKey_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var request = new IngestDeploymentRequest("svc", "production", "commit", DateTimeOffset.UtcNow, null);
        var response = await client.PostAsJsonAsync("/api/v1/deployments", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
