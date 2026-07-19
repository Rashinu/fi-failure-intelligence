using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FI.Domain.Connectors;
using FI.Domain.Ingestion;
using FI.Domain.Redaction;

namespace FI.Infrastructure.Connectors;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 37 — Mock AWS SES / SendGrid Connector.
/// SES ve SendGrid iki ayrı ProviderKey'e sahip olsa da (Bölüm 34'teki dictionary lookup bunu
/// gerektirir) normalize/redact mantığı ortaktır; yalnızca imza header adı ve mock imza şeması
/// sağlayıcıya göre değişir — bu yüzden ortak mantık burada, iki ince alt sınıfta (SesConnector,
/// SendGridConnector) paylaşılır.
///
/// Beklenen ham gövde şekli: { "eventType": "bounce"|"complaint"|"dropped"|"delivered",
///   "messageId": "...", "recipient": "..." }
///
/// E-posta teslim olayları gerçek HTTP çağrıları değildir; FI'nin genel ingestion modeli yine de
/// bir statusCode bekler (Bölüm 21 kuralları buna göre çalışır), bu yüzden burada sentetik ama
/// tutarlı bir eşleme kullanılır: bounce→502 (PROVIDER_ERROR), dropped→503 (PROVIDER_ERROR),
/// complaint→400 (CLIENT_ERROR_OTHER — alıcı kaynaklı sinyal), delivered→200.
/// > DELIVERY_FAILURE, Bölüm 37'nin notuyla tutarlı olarak core taksonomiye eklenmez;
/// connector-özel alt-kategori errorCode alanında (`error.code`) taşınır.
/// </summary>
public abstract class EmailDeliveryConnectorBase : IIntegrationConnector
{
    public abstract string ProviderKey { get; }
    protected abstract string SignatureHeaderName { get; }

    public NormalizedEvent Normalize(RawInboundPayload payload, bool isSignatureVerified)
    {
        using var doc = JsonDocument.Parse(payload.RawBody);
        var root = doc.RootElement;

        var eventType = root.TryGetProperty("eventType", out var eventTypeEl) ? eventTypeEl.GetString() ?? "unknown" : "unknown";
        var messageId = root.TryGetProperty("messageId", out var messageIdEl) ? messageIdEl.GetString() : null;

        var (statusCode, errorCode) = eventType switch
        {
            "bounce" => (502, "hard_bounce"),
            "dropped" => (503, "dropped"),
            "complaint" => (400, "complaint"),
            "delivered" => (200, (string?)null),
            _ => (200, (string?)null)
        };

        var requestJson = JsonSerializer.Serialize(new
        {
            provider = ProviderKey,
            eventType,
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
            ProviderEventId: messageId);
    }

    public bool VerifySignature(RawInboundPayload payload, string secret)
    {
        var header = payload.Header(SignatureHeaderName);
        if (string.IsNullOrEmpty(header)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload.RawBody));
        var expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(header.ToLowerInvariant()));
    }

    public JsonNode? Redact(JsonNode? rawPayload) => PayloadRedactor.RedactJson(rawPayload);
}

public sealed class SesConnector : EmailDeliveryConnectorBase
{
    public override string ProviderKey => "ses";
    protected override string SignatureHeaderName => "X-Amz-Sns-Signature";
}

public sealed class SendGridConnector : EmailDeliveryConnectorBase
{
    public override string ProviderKey => "sendgrid";
    protected override string SignatureHeaderName => "X-Twilio-Email-Event-Webhook-Signature";
}
