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
}
