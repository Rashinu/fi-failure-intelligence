using FI.Domain.Classification;
using FI.Domain.Incidents;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Incidents;

public class IncidentTests
{
    [Fact]
    public void Open_SetsStatusToOpenAndEventCountToOne()
    {
        var now = DateTimeOffset.UtcNow;
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.AuthenticationError, IncidentSeverity.High, now);

        incident.Status.Should().Be(IncidentStatus.Open);
        incident.EventCount.Should().Be(1);
        incident.FirstSeen.Should().Be(now);
        incident.LastSeen.Should().Be(now);
        incident.IsActive.Should().BeTrue();
    }

    [Fact]
    public void RecordNewEvent_IncrementsEventCountAndUpdatesLastSeen()
    {
        var firstSeen = DateTimeOffset.UtcNow.AddMinutes(-10);
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, firstSeen);

        var newOccurredAt = DateTimeOffset.UtcNow;
        incident.RecordNewEvent(newOccurredAt, IncidentSeverity.Medium);

        incident.EventCount.Should().Be(2);
        incident.LastSeen.Should().Be(newOccurredAt);
        incident.Severity.Should().Be(IncidentSeverity.Medium);
        incident.Status.Should().Be(IncidentStatus.Open);
    }

    [Fact]
    public void IsWithinReopenCooldown_WhenResolvedRecently_ReturnsTrue()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow.AddHours(-1));
        typeof(Incident).GetProperty(nameof(Incident.ResolvedAt))!
            .SetValue(incident, DateTimeOffset.UtcNow.AddMinutes(-10));

        incident.IsWithinReopenCooldown(DateTimeOffset.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsWithinReopenCooldown_WhenNeverResolved_ReturnsFalse()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow);

        incident.IsWithinReopenCooldown(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Reopen_SetsStatusToReopenedAndIncrementsReopenCount()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow.AddHours(-1));
        var occurredAt = DateTimeOffset.UtcNow;

        incident.Reopen(occurredAt, IncidentSeverity.High);

        incident.Status.Should().Be(IncidentStatus.Reopened);
        incident.ReopenCount.Should().Be(1);
        incident.ResolvedAt.Should().BeNull();
        incident.Severity.Should().Be(IncidentSeverity.High);
    }

    [Fact]
    public void ResetAsNewOccurrence_ResetsFirstSeenAndEventCount()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow.AddDays(-2));
        var occurredAt = DateTimeOffset.UtcNow;

        incident.ResetAsNewOccurrence(occurredAt, IncidentSeverity.Medium);

        incident.Status.Should().Be(IncidentStatus.Open);
        incident.FirstSeen.Should().Be(occurredAt);
        incident.EventCount.Should().Be(1);
        incident.Severity.Should().Be(IncidentSeverity.Medium);
    }

    [Fact]
    public void StartInvestigating_FromOpen_TransitionsToInvestigating()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow);

        incident.StartInvestigating();

        incident.Status.Should().Be(IncidentStatus.Investigating);
    }

    [Fact]
    public void StartInvestigating_FromReopened_TransitionsToInvestigating()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow.AddHours(-1));
        incident.Reopen(DateTimeOffset.UtcNow, IncidentSeverity.Medium);

        incident.StartInvestigating();

        incident.Status.Should().Be(IncidentStatus.Investigating);
    }

    [Fact]
    public void RecordNewEvent_KeepsStatusOpen_DoesNotAutoInvestigate()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow.AddMinutes(-5));

        incident.RecordNewEvent(DateTimeOffset.UtcNow, IncidentSeverity.Low);

        incident.Status.Should().Be(IncidentStatus.Open);
    }

    // --- M19 P0-B: Resolve lifecycle (bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md) ---

    [Fact]
    public void Resolve_FromOpen_SetsResolvedStateAndMetadata()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.AuthenticationError, IncidentSeverity.Critical, DateTimeOffset.UtcNow);

        incident.Resolve("murat", "Credential rotated back, verified with provider.");

        incident.Status.Should().Be(IncidentStatus.Resolved);
        incident.ResolvedAt.Should().NotBeNull();
        incident.ResolvedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        incident.ResolvedBy.Should().Be("murat");
        incident.ResolutionNote.Should().Be("Credential rotated back, verified with provider.");
        incident.ResolutionSource.Should().Be("HUMAN_MANUAL");
    }

    [Fact]
    public void Resolve_WithNullResolvedByAndNote_Succeeds()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow);

        incident.Resolve(null, null);

        incident.Status.Should().Be(IncidentStatus.Resolved);
        incident.ResolvedBy.Should().BeNull();
        incident.ResolutionNote.Should().BeNull();
    }

    [Fact]
    public void Resolve_FromNeedsHumanReview_Succeeds()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.AuthenticationError, IncidentSeverity.High, DateTimeOffset.UtcNow);
        incident.MarkNeedsHumanReview();

        incident.Resolve("support-agent", "False positive.");

        incident.Status.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public void Resolve_WhenAlreadyResolved_Throws()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow);
        incident.Resolve("first", "first note");

        var act = () => incident.Resolve("second", "second note");

        act.Should().Throw<InvalidOperationException>();
        // Bkz. M19 P0-B - geçersiz ikinci resolve denemesi, ilk resolve'un metadata'sını BOZMAMALI.
        incident.ResolvedBy.Should().Be("first");
        incident.ResolutionNote.Should().Be("first note");
    }

    [Fact]
    public void Reopen_AfterResolve_ClearsResolutionMetadataAndReactivates()
    {
        // Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md - "resolved at 14:20 -> matching failure
        // at 14:22" senaryosu: ClassifyJobHandler'ın MEVCUT IsWithinReopenCooldown dalı Reopen()'a
        // yönlendirir; burada paralel bir recovery lifecycle yok, tek doğrulanan şey Reopen'ın
        // Resolve tarafından set edilen tüm alanları doğru temizlediği.
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.AuthenticationError, IncidentSeverity.Critical, DateTimeOffset.UtcNow.AddMinutes(-10));
        var resolvedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        incident.Resolve("murat", "Thought it was fixed.");

        incident.IsWithinReopenCooldown(DateTimeOffset.UtcNow).Should().BeTrue();

        incident.Reopen(DateTimeOffset.UtcNow, IncidentSeverity.Critical);

        incident.Status.Should().Be(IncidentStatus.Reopened);
        incident.ResolvedAt.Should().BeNull();
        incident.ResolvedBy.Should().BeNull();
        incident.ResolutionNote.Should().BeNull();
        incident.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Resolve_AfterReopen_SucceedsAgain()
    {
        var incident = Incident.Open(Guid.NewGuid(), "fp123", EventCategory.ProviderError, IncidentSeverity.Low, DateTimeOffset.UtcNow.AddHours(-1));
        incident.Resolve("murat", "first fix");
        incident.Reopen(DateTimeOffset.UtcNow, IncidentSeverity.Medium);

        incident.Resolve("murat", "second, real fix");

        incident.Status.Should().Be(IncidentStatus.Resolved);
        incident.ResolutionNote.Should().Be("second, real fix");
    }
}
