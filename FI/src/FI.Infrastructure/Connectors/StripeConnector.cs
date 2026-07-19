using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FI.Domain.Connectors;
using FI.Domain.Ingestion;
using FI.Domain.Redaction;

namespace FI.Infrastructure.Connectors;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 35 — Mock Stripe Connector.
/// Gerçek Stripe SDK'sı kullanılmaz; bu, demo/pilot senaryosu için Stripe'ın webhook zarfını
/// ve imza şemasını (Stripe-Signature: t=...,v1=...) taklit eden bir mock'tur.
/// Beklenen ham gövde şekli: { "type": "charge.failed", "httpStatusCode": 401,
///   "data": { "object": { "id": "ch_123", "status": "failed" } },
///   "error": { "code": "invalid_api_key" } }
/// </summary>
public sealed class StripeConnector : IIntegrationConnector
{
    public string ProviderKey => "stripe";

    public NormalizedEvent Normalize(RawInboundPayload payload, bool isSignatureVerified)
    {
        using var doc = JsonDocument.Parse(payload.RawBody);
        var root = doc.RootElement;

        var statusCode = root.TryGetProperty("httpStatusCode", out var statusEl) && statusEl.ValueKind == JsonValueKind.Number
            ? statusEl.GetInt32()
            : 200;

        var providerEventId = root.TryGetProperty("data", out var data) &&
                               data.TryGetProperty("object", out var obj) &&
                               obj.TryGetProperty("id", out var idEl)
            ? idEl.GetString()
            : null;

        var errorCode = root.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var codeEl)
            ? codeEl.GetString()
            : null;

        var requestJson = JsonSerializer.Serialize(new
        {
            provider = ProviderKey,
            type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null,
            headers = new Dictionary<string, object?>
            {
                ["X-Signature-Valid"] = isSignatureVerified
            }
        });

        var responseJson = JsonSerializer.Serialize(new
        {
            error = errorCode is null ? null : new { code = errorCode }
        });

        return new NormalizedEvent(
            EventType: IntegrationEventType.WebhookIn,
            StatusCode: statusCode,
            RequestJson: requestJson,
            ResponseJson: responseJson,
            LatencyMs: null,
            OccurredAt: DateTimeOffset.UtcNow,
            ProviderEventId: providerEventId);
    }

    /// <summary>
    /// Stripe-Signature: t={unixTimestamp},v1={hmac}. HMAC-SHA256("{t}.{rawBody}", secret).
    /// 5 dakikadan eski/ileri timestamp'ler replay koruması olarak reddedilir.
    /// </summary>
    public bool VerifySignature(RawInboundPayload payload, string secret)
    {
        var header = payload.Header("Stripe-Signature");
        if (string.IsNullOrEmpty(header)) return false;

        string? timestampPart = null;
        string? signaturePart = null;
        foreach (var segment in header.Split(','))
        {
            var kv = segment.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t") timestampPart = kv[1];
            else if (kv[0] == "v1") signaturePart = kv[1];
        }

        if (timestampPart is null || signaturePart is null) return false;
        if (!long.TryParse(timestampPart, out var unixTimestamp)) return false;

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        if (Math.Abs((DateTimeOffset.UtcNow - timestamp).TotalMinutes) > 5) return false;

        var signedPayload = $"{timestampPart}.{payload.RawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signaturePart.ToLowerInvariant()));
    }

    /// <summary>Bkz. Bölüm 33.3 — tek gerçek redaction motoruna delege eder (bkz. PayloadRedactor'ın kendi XML doc'u).</summary>
    public JsonNode? Redact(JsonNode? rawPayload) => PayloadRedactor.RedactJson(rawPayload);
}
