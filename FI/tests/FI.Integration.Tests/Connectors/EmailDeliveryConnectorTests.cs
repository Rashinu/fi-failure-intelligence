using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FI.Domain.Connectors;
using FI.Infrastructure.Connectors;
using FluentAssertions;

namespace FI.Integration.Tests.Connectors;

public class EmailDeliveryConnectorTests
{
    private const string Secret = "email-webhook-secret";

    [Theory]
    [InlineData("bounce", 502, "hard_bounce")]
    [InlineData("dropped", 503, "dropped")]
    [InlineData("complaint", 400, "complaint")]
    [InlineData("delivered", 200, null)]
    public void SesConnector_Normalize_MapsEventTypeToSyntheticStatusCode(string eventType, int expectedStatus, string? expectedErrorCode)
    {
        var connector = new SesConnector();
        var body = $$"""{"eventType":"{{eventType}}","messageId":"msg-1"}""";
        var payload = new RawInboundPayload(body, new Dictionary<string, string>());

        var normalized = connector.Normalize(payload, isSignatureVerified: true);

        normalized.StatusCode.Should().Be(expectedStatus);
        normalized.ProviderEventId.Should().Be("msg-1");

        using var responseDoc = JsonDocument.Parse(normalized.ResponseJson!);
        if (expectedErrorCode is null)
            responseDoc.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        else
            responseDoc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(expectedErrorCode);
    }

    [Fact]
    public void SesConnector_ProviderKey_IsSes() => new SesConnector().ProviderKey.Should().Be("ses");

    [Fact]
    public void SendGridConnector_ProviderKey_IsSendGrid() => new SendGridConnector().ProviderKey.Should().Be("sendgrid");

    [Fact]
    public void SendGridConnector_VerifySignature_ValidHmac_ReturnsTrue()
    {
        var connector = new SendGridConnector();
        var body = """{"eventType":"bounce","messageId":"msg-2"}""";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var payload = new RawInboundPayload(body, new Dictionary<string, string>
        {
            ["X-Twilio-Email-Event-Webhook-Signature"] = signature
        });

        connector.VerifySignature(payload, Secret).Should().BeTrue();
    }
}
