using FI.Domain.Ingestion;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Ingestion;

public class DeploymentTests
{
    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var deployment = Deployment.Create(
            Guid.NewGuid(), "payments-api", "production", "a1b2c3d", null, DateTimeOffset.UtcNow);

        deployment.Id.Should().NotBeEmpty();
        deployment.Service.Should().Be("payments-api");
    }

    [Theory]
    [InlineData("", "production", "commit")]
    [InlineData("service", "", "commit")]
    [InlineData("service", "production", "")]
    public void Create_WithMissingRequiredField_Throws(string service, string environment, string commit)
    {
        var act = () => Deployment.Create(null, service, environment, commit, null, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithNullIntegrationId_Succeeds()
    {
        var deployment = Deployment.Create(null, "svc", "production", "commit", null, DateTimeOffset.UtcNow);

        deployment.IntegrationId.Should().BeNull();
    }
}
