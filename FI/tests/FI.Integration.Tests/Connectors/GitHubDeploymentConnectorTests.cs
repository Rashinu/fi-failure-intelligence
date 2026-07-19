using System.Security.Cryptography;
using System.Text;
using FI.Domain.Connectors;
using FI.Infrastructure.Connectors;
using FluentAssertions;

namespace FI.Integration.Tests.Connectors;

public class GitHubDeploymentConnectorTests
{
    private const string Secret = "gh-webhook-secret";
    private readonly GitHubDeploymentConnector _connector = new();

    private static string Sign(string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
    }

    private const string SampleBody = """
        {
          "repository": { "name": "checkout-service" },
          "deployment": { "sha": "abc123", "environment": "production", "created_at": "2026-07-18T10:00:00Z" },
          "changedConfig": [{ "key": "STRIPE_WEBHOOK_SECRET", "changed": true }]
        }
        """;

    [Fact]
    public void VerifySignature_ValidSignature_ReturnsTrue()
    {
        var header = Sign(SampleBody, Secret);
        var payload = new RawInboundPayload(SampleBody, new Dictionary<string, string> { ["X-Hub-Signature-256"] = header });

        _connector.VerifySignature(payload, Secret).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_WrongSecret_ReturnsFalse()
    {
        var header = Sign(SampleBody, "wrong-secret");
        var payload = new RawInboundPayload(SampleBody, new Dictionary<string, string> { ["X-Hub-Signature-256"] = header });

        _connector.VerifySignature(payload, Secret).Should().BeFalse();
    }

    [Fact]
    public void Normalize_ParsesCommitEnvironmentAndChangedConfig()
    {
        var payload = new RawInboundPayload(SampleBody, new Dictionary<string, string>());

        var normalized = _connector.Normalize(payload);

        normalized.Service.Should().Be("checkout-service");
        normalized.Commit.Should().Be("abc123");
        normalized.Environment.Should().Be("production");
        normalized.ChangedConfig.Should().ContainSingle(c => c.Key == "STRIPE_WEBHOOK_SECRET" && c.Changed);
    }
}
