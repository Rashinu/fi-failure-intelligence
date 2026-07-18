using FI.Domain.Classification;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Classification;

public class SeverityCalculatorTests
{
    [Fact]
    public void ProviderErrorWithHighVolume_IsCritical()
    {
        var severity = SeverityCalculator.Calculate(EventCategory.ProviderError, 50, 50, 50, false);
        severity.Should().Be(IncidentSeverity.Critical);
    }

    [Fact]
    public void CriticalBusinessIntegration_IsAlwaysCritical()
    {
        var severity = SeverityCalculator.Calculate(EventCategory.ClientErrorOther, 0, 0, 0, true);
        severity.Should().Be(IncidentSeverity.Critical);
    }

    [Fact]
    public void SignatureErrorCategory_IsAtLeastHigh()
    {
        var severity = SeverityCalculator.Calculate(EventCategory.SignatureError, 0, 0, 0, false);
        severity.Should().Be(IncidentSeverity.High);
    }

    [Fact]
    public void HighVolumeWithoutSpecialCategory_IsHigh()
    {
        var severity = SeverityCalculator.Calculate(EventCategory.ClientErrorOther, 0, 20, 20, false);
        severity.Should().Be(IncidentSeverity.High);
    }

    [Fact]
    public void ModerateVolume_IsMedium()
    {
        var severity = SeverityCalculator.Calculate(EventCategory.ClientErrorOther, 0, 0, 5, false);
        severity.Should().Be(IncidentSeverity.Medium);
    }

    [Fact]
    public void LowVolumeOrdinaryCategory_IsLow()
    {
        var severity = SeverityCalculator.Calculate(EventCategory.ClientErrorOther, 0, 0, 0, false);
        severity.Should().Be(IncidentSeverity.Low);
    }
}
