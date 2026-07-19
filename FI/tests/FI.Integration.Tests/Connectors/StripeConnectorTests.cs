using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FI.Domain.Connectors;
using FI.Infrastructure.Connectors;
using FluentAssertions;

namespace FI.Integration.Tests.Connectors;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 35 — pure logic, DB/Testcontainers
/// gerektirmez; FI.Infrastructure'a proje referansı zaten burada mevcut olduğu için ayrı bir
/// test projesi açılmadı.
/// </summary>
public class StripeConnectorTests
{
    private const string Secret = "whsec_test_secret";
    private readonly StripeConnector _connector = new();

    private static string Sign(string rawBody, string secret, long? timestampOverride = null)
    {
        var timestamp = timestampOverride ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestamp}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();
        return $"t={timestamp},v1={signature}";
    }

    [Fact]
    public void VerifySignature_ValidSignature_ReturnsTrue()
    {
        var body = """{"type":"charge.failed","httpStatusCode":401}""";
        var header = Sign(body, Secret);
        var payload = new RawInboundPayload(body, new Dictionary<string, string> { ["Stripe-Signature"] = header });

        _connector.VerifySignature(payload, Secret).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_WrongSecret_ReturnsFalse()
    {
        var body = """{"type":"charge.failed","httpStatusCode":401}""";
        var header = Sign(body, "different-secret");
        var payload = new RawInboundPayload(body, new Dictionary<string, string> { ["Stripe-Signature"] = header });

        _connector.VerifySignature(payload, Secret).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_ExpiredTimestamp_ReturnsFalse()
    {
        var body = """{"type":"charge.failed","httpStatusCode":401}""";
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();
        var header = Sign(body, Secret, oldTimestamp);
        var payload = new RawInboundPayload(body, new Dictionary<string, string> { ["Stripe-Signature"] = header });

        _connector.VerifySignature(payload, Secret).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_MissingHeader_ReturnsFalse()
    {
        var payload = new RawInboundPayload("{}", new Dictionary<string, string>());

        _connector.VerifySignature(payload, Secret).Should().BeFalse();
    }

    [Fact]
    public void Normalize_SignatureVerified_SetsHeaderFlagTrue()
    {
        var body = """{"type":"charge.failed","httpStatusCode":401,"data":{"object":{"id":"ch_123"}},"error":{"code":"invalid_api_key"}}""";
        var payload = new RawInboundPayload(body, new Dictionary<string, string>());

        var normalized = _connector.Normalize(payload, isSignatureVerified: true);

        normalized.StatusCode.Should().Be(401);
        normalized.ProviderEventId.Should().Be("ch_123");

        using var requestDoc = JsonDocument.Parse(normalized.RequestJson!);
        requestDoc.RootElement.GetProperty("headers").GetProperty("X-Signature-Valid").GetBoolean().Should().BeTrue();

        using var responseDoc = JsonDocument.Parse(normalized.ResponseJson!);
        responseDoc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_api_key");
    }

    [Fact]
    public void Normalize_SignatureNotVerified_SetsHeaderFlagFalse()
    {
        var body = """{"type":"charge.failed","httpStatusCode":401}""";
        var payload = new RawInboundPayload(body, new Dictionary<string, string>());

        var normalized = _connector.Normalize(payload, isSignatureVerified: false);

        using var requestDoc = JsonDocument.Parse(normalized.RequestJson!);
        requestDoc.RootElement.GetProperty("headers").GetProperty("X-Signature-Valid").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void Redact_MasksClientSecretAndApiKeyFields()
    {
        var node = JsonDocument.Parse("""{"client_secret":"sk_live_abc","nested":{"api_key":"key_123","keep":"visible"}}""").RootElement;
        var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(node.GetRawText());

        var redacted = _connector.Redact(jsonNode);

        redacted!["client_secret"]!.GetValue<string>().Should().Be("[REDACTED]");
        redacted["nested"]!["api_key"]!.GetValue<string>().Should().Be("[REDACTED]");
        redacted["nested"]!["keep"]!.GetValue<string>().Should().Be("visible");
    }
}
