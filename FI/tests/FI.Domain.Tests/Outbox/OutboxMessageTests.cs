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

    [Fact]
    public void MarkFailed_SetsStatusAndFailureMetadata()
    {
        var message = OutboxMessage.Create(OutboxMessageType.ClassifyJob, "{\"eventId\":\"...\"}");

        message.MarkFailed("System.Exception: boom");

        message.Status.Should().Be(OutboxMessageStatus.Failed);
        message.FailureCount.Should().Be(1);
        message.LastFailedAt.Should().NotBeNull();
        message.LastError.Should().Be("System.Exception: boom");
    }

    [Fact]
    public void MarkFailed_CalledTwice_IncrementsFailureCount()
    {
        var message = OutboxMessage.Create(OutboxMessageType.ClassifyJob, "{\"eventId\":\"...\"}");

        message.MarkFailed("first error");
        message.MarkFailed("second error");

        message.FailureCount.Should().Be(2);
        message.LastError.Should().Be("second error");
    }

    [Fact]
    public void MarkFailed_WithVeryLongError_Truncates()
    {
        var message = OutboxMessage.Create(OutboxMessageType.ClassifyJob, "{\"eventId\":\"...\"}");
        var longError = new string('x', 3000);

        message.MarkFailed(longError);

        message.LastError!.Length.Should().Be(2000);
    }
}
