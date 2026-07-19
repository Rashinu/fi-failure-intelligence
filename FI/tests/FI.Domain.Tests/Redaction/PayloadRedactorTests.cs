using System.Text.Json.Nodes;
using FI.Domain.Redaction;
using FluentAssertions;

namespace FI.Domain.Tests.Redaction;

/// <summary>Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 33.3.</summary>
public class PayloadRedactorTests
{
    [Theory]
    [InlineData("authorization")]
    [InlineData("Authorization")]
    [InlineData("x-api-key")]
    [InlineData("X-Auth-Token")]
    [InlineData("apiKey")]
    [InlineData("secret")]
    [InlineData("client_secret")]
    [InlineData("password")]
    [InlineData("token")]
    public void RedactJson_SensitiveFieldName_FullyMasksValue(string fieldName)
    {
        var node = JsonNode.Parse($$"""{"{{fieldName}}":"super-secret-value-123"}""");

        var redacted = PayloadRedactor.RedactJson(node);

        redacted![fieldName]!.GetValue<string>().Should().Be("[REDACTED]");
    }

    [Fact]
    public void RedactJson_NestedSensitiveField_IsMasked()
    {
        var node = JsonNode.Parse("""{"headers":{"Authorization":"Bearer abc123"},"keep":"visible"}""");

        var redacted = PayloadRedactor.RedactJson(node);

        redacted!["headers"]!["Authorization"]!.GetValue<string>().Should().Be("[REDACTED]");
        redacted["keep"]!.GetValue<string>().Should().Be("visible");
    }

    [Fact]
    public void RedactJson_SensitiveFieldInsideArray_IsMasked()
    {
        var node = JsonNode.Parse("""[{"token":"abc"},{"token":"def"}]""");

        var redacted = PayloadRedactor.RedactJson(node);

        redacted![0]!["token"]!.GetValue<string>().Should().Be("[REDACTED]");
        redacted[1]!["token"]!.GetValue<string>().Should().Be("[REDACTED]");
    }

    [Fact]
    public void RedactJson_NonSensitiveFieldWithEmbeddedBearerToken_IsPatternRedacted()
    {
        var node = JsonNode.Parse("""{"note":"got Bearer eyJhbGciOiJIUzI1NiJ9.abc.def in the logs"}""");

        var redacted = PayloadRedactor.RedactJson(node);

        redacted!["note"]!.GetValue<string>().Should().Be("got Bearer [REDACTED] in the logs");
    }

    [Fact]
    public void RedactText_Email_MasksLocalPart()
    {
        PayloadRedactor.RedactText("contact john.doe@example.com for help")
            .Should().Be("contact j***@example.com for help");
    }

    [Fact]
    public void RedactText_ValidCreditCardNumber_MasksAllButLastFour()
    {
        // 4111 1111 1111 1111 is a well-known Luhn-valid test card number.
        PayloadRedactor.RedactText("card on file: 4111 1111 1111 1111")
            .Should().Be("card on file: **** **** **** 1111");
    }

    [Fact]
    public void RedactText_LuhnInvalidDigitSequence_IsLeftUntouched()
    {
        // Same shape, but fails the Luhn check - could be an order id, must not be masked.
        var text = "order id: 1234 5678 9012 3456";

        PayloadRedactor.RedactText(text).Should().Be(text);
    }

    [Fact]
    public void RedactText_PhoneNumber_MasksAllButLastFour()
    {
        PayloadRedactor.RedactText("call +1-555-234-5678 now")
            .Should().Be("call ***-***-5678 now");
    }

    [Fact]
    public void RedactText_JwtLikeToken_IsMasked()
    {
        var text = "session eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U expired";

        PayloadRedactor.RedactText(text).Should().Contain("[REDACTED]").And.NotContain("eyJhbGciOiJIUzI1NiJ9");
    }

    [Fact]
    public void RedactJson_Idempotent_SecondPassProducesSameResult()
    {
        var node = JsonNode.Parse("""{"token":"abc","note":"email me at a@b.com"}""");

        var once = PayloadRedactor.RedactJson(node);
        var twice = PayloadRedactor.RedactJson(JsonNode.Parse(once!.ToJsonString()));

        twice!.ToJsonString().Should().Be(once.ToJsonString());
    }

    [Fact]
    public void RedactJson_Null_ReturnsNull()
    {
        PayloadRedactor.RedactJson(null).Should().BeNull();
    }
}
