using FI.Domain.Classification;
using FluentAssertions;
using Xunit;

namespace FI.Domain.Tests.Classification;

public class FingerprintCalculatorTests
{
    [Fact]
    public void SameInputs_ProduceSameFingerprint()
    {
        var id = Guid.NewGuid();
        var fp1 = FingerprintCalculator.Compute(id, EventCategory.AuthenticationError, "401_invalid_api_key");
        var fp2 = FingerprintCalculator.Compute(id, EventCategory.AuthenticationError, "401_invalid_api_key");

        fp1.Should().Be(fp2);
    }

    [Fact]
    public void DifferentErrorSignature_ProducesDifferentFingerprint()
    {
        var id = Guid.NewGuid();
        var fp1 = FingerprintCalculator.Compute(id, EventCategory.AuthenticationError, "401_invalid_api_key");
        var fp2 = FingerprintCalculator.Compute(id, EventCategory.AuthenticationError, "401_expired_token");

        fp1.Should().NotBe(fp2);
    }

    [Fact]
    public void DifferentIntegrationId_ProducesDifferentFingerprint()
    {
        var fp1 = FingerprintCalculator.Compute(Guid.NewGuid(), EventCategory.ProviderError, "503");
        var fp2 = FingerprintCalculator.Compute(Guid.NewGuid(), EventCategory.ProviderError, "503");

        fp1.Should().NotBe(fp2);
    }

    [Fact]
    public void DifferentCategory_SameSignatureText_ProducesDifferentFingerprint()
    {
        var id = Guid.NewGuid();
        var fp1 = FingerprintCalculator.Compute(id, EventCategory.ProviderError, "duplicate");
        var fp2 = FingerprintCalculator.Compute(id, EventCategory.DuplicateEvent, "duplicate");

        fp1.Should().NotBe(fp2);
    }
}
