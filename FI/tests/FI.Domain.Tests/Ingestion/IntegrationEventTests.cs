using FI.Domain.Ingestion;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Ingestion;

public class IntegrationEventTests
{
    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void Create_WithOutOfRangeStatusCode_Throws(int statusCode)
    {
        var act = () => IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.ApiCall, statusCode, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(100)]
    [InlineData(401)]
    [InlineData(599)]
    public void Create_WithValidStatusCode_Succeeds(int statusCode)
    {
        var evt = IntegrationEvent.Create(
            Guid.NewGuid(), IntegrationEventType.ApiCall, statusCode, null, null, null,
            Guid.NewGuid(), null, null, null, 0, false, DateTimeOffset.UtcNow);

        evt.StatusCode.Should().Be(statusCode);
        evt.Id.Should().NotBeEmpty();
        evt.ReceivedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }
}
