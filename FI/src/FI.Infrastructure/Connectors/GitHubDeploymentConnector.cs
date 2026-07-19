using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FI.Domain.Connectors;

namespace FI.Infrastructure.Connectors;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 36 — Mock GitHub Deployment Connector.
/// GitHub'ın <c>deployment_status</c> webhook zarfını taklit eder. Beklenen ham gövde şekli:
/// { "repository": { "name": "checkout-service" },
///   "deployment": { "sha": "abc123", "environment": "production", "created_at": "..." },
///   "changedConfig": [{ "key": "STRIPE_WEBHOOK_SECRET", "changed": true }] }
/// </summary>
public sealed class GitHubDeploymentConnector : IDeploymentConnector
{
    public string ProviderKey => "github";

    public NormalizedDeployment Normalize(RawInboundPayload payload)
    {
        using var doc = JsonDocument.Parse(payload.RawBody);
        var root = doc.RootElement;

        var service = root.TryGetProperty("repository", out var repo) && repo.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString() ?? "unknown-service"
            : "unknown-service";

        var deployment = root.GetProperty("deployment");
        var commit = deployment.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() ?? "unknown" : "unknown";
        var environment = deployment.TryGetProperty("environment", out var envEl) ? envEl.GetString() ?? "unknown" : "unknown";
        var deployedAt = deployment.TryGetProperty("created_at", out var createdEl) && createdEl.TryGetDateTimeOffset(out var dto)
            ? dto
            : DateTimeOffset.UtcNow;

        IReadOnlyList<ChangedConfigField>? changedConfig = null;
        if (root.TryGetProperty("changedConfig", out var changedEl) && changedEl.ValueKind == JsonValueKind.Array)
        {
            changedConfig = changedEl.EnumerateArray()
                .Select(e => new ChangedConfigField(
                    e.GetProperty("key").GetString() ?? "unknown",
                    e.TryGetProperty("changed", out var changedFlag) && changedFlag.GetBoolean()))
                .ToList();
        }

        return new NormalizedDeployment(service, environment, commit, deployedAt, changedConfig);
    }

    /// <summary>X-Hub-Signature-256: sha256={hmac}. HMAC-SHA256(rawBody, secret).</summary>
    public bool VerifySignature(RawInboundPayload payload, string secret)
    {
        var header = payload.Header("X-Hub-Signature-256");
        if (string.IsNullOrEmpty(header) || !header.StartsWith("sha256=", StringComparison.Ordinal)) return false;

        var signaturePart = header["sha256=".Length..];

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload.RawBody));
        var expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signaturePart.ToLowerInvariant()));
    }
}
