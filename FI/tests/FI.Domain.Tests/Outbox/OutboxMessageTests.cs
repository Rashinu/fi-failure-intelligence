using System.Diagnostics;
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
    public void Create_WithoutActiveActivity_TraceParentIsNull()
    {
        Activity.Current.Should().BeNull("bu test bağımsız çalışıyor, ortamda aktif bir Activity olmamalı");

        var message = OutboxMessage.Create(OutboxMessageType.ClassifyJob, "{\"eventId\":\"...\"}");

        message.TraceParent.Should().BeNull();
    }

    /// <summary>
    /// TD8 doğrulaması: `Create()`, çağrıldığı anda aktif bir W3C Activity varsa onun Id'sini
    /// (traceparent string'i) otomatik yakalar - ClassifyJobHandler/EvidenceCollectorJobHandler/
    /// AiAnalysisJobHandler bunu kendi Activity'lerinin parent'ı olarak kullanır (bkz. FiTelemetry).
    /// </summary>
    [Fact]
    public void Create_WithActiveActivity_CapturesTraceParent()
    {
        using var source = new ActivitySource("FI.Domain.Tests.Outbox");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("test-span");

        var message = OutboxMessage.Create(OutboxMessageType.ClassifyJob, "{\"eventId\":\"...\"}");

        message.TraceParent.Should().Be(activity!.Id);
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
