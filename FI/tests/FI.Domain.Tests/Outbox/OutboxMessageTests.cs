using FI.Domain.Outbox;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Outbox;

public class OutboxMessageTests
{
    [Fact]
    public void Create_SetsStatusToPending()
    {
        var message = OutboxMessage.Create(OutboxMessageType.ClassifyJob, "{\"eventId\":\"...\"}");

        message.Status.Should().Be(OutboxMessageStatus.Pending);
        message.DispatchedAt.Should().BeNull();
    }

    [Fact]
    public void Create_WithEmptyPayload_Throws()
    {
        var act = () => OutboxMessage.Create(OutboxMessageType.ClassifyJob, "");

        act.Should().Throw<ArgumentException>();
    }
}
