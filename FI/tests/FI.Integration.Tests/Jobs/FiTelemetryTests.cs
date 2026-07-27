using System.Diagnostics;
using FI.Infrastructure.Jobs;
using FluentAssertions;
using Xunit;

namespace FI.Integration.Tests.Jobs;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md Teknik Borç TD8 - saf mantık, DB/Testcontainers gerektirmez;
/// FI.Infrastructure'a proje referansı zaten burada mevcut olduğu için ayrı bir test projesi
/// açılmadı (bkz. StripeConnectorTests'teki aynı gerekçe).
/// </summary>
public class FiTelemetryTests
{
    private static void EnsureListening()
    {
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        });
    }

    [Fact]
    public void StartLinkedActivity_WithValidTraceParent_UsesItAsParent()
    {
        EnsureListening();
        using var remoteSource = new ActivitySource("FI.Integration.Tests.Jobs");
        using var remoteActivity = remoteSource.StartActivity("upstream-request");
        var traceParent = remoteActivity!.Id;

        using var linked = FiTelemetry.StartLinkedActivity("ClassifyJob", traceParent);

        linked!.ParentId.Should().Be(traceParent);
        linked.TraceId.Should().Be(remoteActivity.TraceId);
    }

    [Fact]
    public void StartLinkedActivity_WithNullTraceParent_StartsNewRootActivity()
    {
        EnsureListening();

        using var linked = FiTelemetry.StartLinkedActivity("ClassifyJob", null);

        linked.Should().NotBeNull();
        linked!.ParentId.Should().BeNull();
    }

    [Fact]
    public void StartLinkedActivity_WithGarbageTraceParent_DoesNotThrowAndStartsRootActivity()
    {
        EnsureListening();

        var act = () => FiTelemetry.StartLinkedActivity("ClassifyJob", "not-a-real-traceparent");

        act.Should().NotThrow();
    }
}
