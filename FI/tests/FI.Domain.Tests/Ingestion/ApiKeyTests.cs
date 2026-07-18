using FI.Domain.Ingestion;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Ingestion;

public class ApiKeyTests
{
    [Fact]
    public void Create_SetsIsActiveTrue()
    {
        var apiKey = ApiKey.Create(Guid.NewGuid(), "fi_live_ab12", "hash");

        apiKey.IsActive.Should().BeTrue();
        apiKey.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void Revoke_SetsIsActiveFalse()
    {
        var apiKey = ApiKey.Create(Guid.NewGuid(), "fi_live_ab12", "hash");

        apiKey.Revoke();

        apiKey.IsActive.Should().BeFalse();
        apiKey.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public void RecordUsage_IncrementsUsageCountAndSetsLastUsedAt()
    {
        var apiKey = ApiKey.Create(Guid.NewGuid(), "fi_live_ab12", "hash");

        apiKey.RecordUsage();
        apiKey.RecordUsage();

        apiKey.UsageCount.Should().Be(2);
        apiKey.LastUsedAt.Should().NotBeNull();
    }
}
