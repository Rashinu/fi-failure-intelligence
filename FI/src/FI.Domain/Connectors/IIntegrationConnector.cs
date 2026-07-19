using System.Text.Json.Nodes;

namespace FI.Domain.Connectors;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 34. Her connector kendi somut sınıfıdır
/// (generic bir "connector repository" soyutlaması kasıtlı olarak eklenmez); kayıt tek bir
/// dictionary lookup ile <see cref="IConnectorRegistry"/> üzerinden ProviderKey'e göre çözülür.
/// Not (kasıtlı sadeleştirme): Mimari dokümandaki <c>Classify</c> metodu buraya eklenmedi —
/// sınıflandırma zaten <c>EventClassifier</c>'da tek gerçek kaynak olarak var (Bölüm 21);
/// connector'da ikinci bir kural motoru olması iki kaynağın birbirinden sapması riskini
/// doğururdu. Connector'lar bunun yerine ham veriyi EventClassifier'ın zaten anladığı
/// request/response JSON şekline normalize eder (bkz. <see cref="NormalizedEvent"/>).
/// </summary>
public interface IIntegrationConnector
{
    string ProviderKey { get; }

    /// <summary>Ham webhook body'sini imzadan bağımsız olarak kanonik modele çevirir.</summary>
    NormalizedEvent Normalize(RawInboundPayload payload, bool isSignatureVerified);

    /// <summary>
    /// Doğrulama başarısızsa event reddedilmez — SIGNATURE_ERROR kategorisiyle yine de kaydedilir
    /// (Bölüm 34, madde 6). Sabit-zamanlı karşılaştırma kullanılmalıdır (timing attack önlemi).
    /// </summary>
    bool VerifySignature(RawInboundPayload payload, string secret);

    /// <summary>client_secret/api_key/PAN-benzeri alanları maskeler.</summary>
    JsonNode? Redact(JsonNode? rawPayload);
}
