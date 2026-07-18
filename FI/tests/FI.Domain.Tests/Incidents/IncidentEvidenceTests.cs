using FI.Domain.Incidents;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Incidents;

public class IncidentEvidenceTests
{
    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var evidence = IncidentEvidence.Create(
            Guid.NewGuid(), EvidenceSourceType.Deployment, Guid.NewGuid(),
            "Deployment abc123 occurred 5 minutes before first failure", null,
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow);

        evidence.Id.Should().NotBeEmpty();
        evidence.SourceType.Should().Be(EvidenceSourceType.Deployment);
    }

    [Fact]
    public void Create_WithEmptySummary_Throws()
    {
        var act = () => IncidentEvidence.Create(
            Guid.NewGuid(), EvidenceSourceType.PreviousEvent, null, "", null, null, null);

        act.Should().Throw<ArgumentException>();
    }
}
