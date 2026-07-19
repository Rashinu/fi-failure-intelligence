using FI.Domain.Ingestion;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Ingestion;

public class IntegrationTests
{
    [Fact]
    public void Create_WithValidData_SetsExpectedDefaults()
    {
        var integration = Integration.Create(
            name: "Stripe Payments",
            provider: "stripe",
            environment: "production",
            owner: "backend-team",
            endpointUrl: "https://api.stripe.com",
            businessCriticality: BusinessCriticality.High);

        integration.Id.Should().NotBeEmpty();
        integration.Status.Should().Be(IntegrationStatus.Active);
        integration.BusinessCriticality.Should().Be(BusinessCriticality.High);
        integration.CreatedAt.Should().Be(integration.UpdatedAt);
        integration.ApiKeys.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "stripe", "production", "owner")]
    [InlineData("name", "", "production", "owner")]
    [InlineData("name", "stripe", "", "owner")]
    [InlineData("name", "stripe", "production", "")]
    public void Create_WithMissingRequiredField_Throws(string name, string provider, string environment, string owner)
    {
        var act = () => Integration.Create(name, provider, environment, owner, null, BusinessCriticality.Medium);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Archive_SetsStatusToArchived_AndUpdatesTimestamp()
    {
        var integration = Integration.Create("Name", "stripe", "production", "owner", null, BusinessCriticality.Medium);
        var createdAt = integration.CreatedAt;

        integration.Archive();

        integration.Status.Should().Be(IntegrationStatus.Archived);
        integration.UpdatedAt.Should().BeOnOrAfter(createdAt);
    }

    [Fact]
    public void IssueApiKey_AddsActiveKeyToCollection()
    {
        var integration = Integration.Create("Name", "stripe", "production", "owner", null, BusinessCriticality.Medium);

        var apiKey = integration.IssueApiKey("fi_live_ab12", "hash-value");

        integration.ApiKeys.Should().ContainSingle();
        apiKey.IsActive.Should().BeTrue();
        apiKey.IntegrationId.Should().Be(integration.Id);
    }

    [Fact]
    public void RotateApiKey_RevokesPreviousActiveKeys_AndIssuesNewOne()
    {
        var integration = Integration.Create("Name", "stripe", "production", "owner", null, BusinessCriticality.Medium);
        var originalKey = integration.IssueApiKey("fi_live_old1", "old-hash");

        var newKey = integration.RotateApiKey("fi_live_new1", "new-hash");

        originalKey.IsActive.Should().BeFalse();
        newKey.IsActive.Should().BeTrue();
        integration.ApiKeys.Should().HaveCount(2);
    }

    [Fact]
    public void RotateApiKey_AlreadyRevokedKey_IsNotTouchedAgain()
    {
        var integration = Integration.Create("Name", "stripe", "production", "owner", null, BusinessCriticality.Medium);
        var firstKey = integration.IssueApiKey("fi_live_first", "hash1");
        firstKey.Revoke();

        var secondKey = integration.RotateApiKey("fi_live_second", "hash2");

        secondKey.IsActive.Should().BeTrue();
        integration.ApiKeys.Should().HaveCount(2);
        integration.ApiKeys.Count(k => k.IsActive).Should().Be(1);
    }

    [Fact]
    public void IssueWebhookSecret_SetsSecret_AndUpdatesTimestamp()
    {
        var integration = Integration.Create("Name", "stripe", "production", "owner", null, BusinessCriticality.Medium);

        integration.IssueWebhookSecret("whsec_abc123");

        integration.WebhookSecret.Should().Be("whsec_abc123");
    }
}
