using System.Security.Cryptography;
using System.Text;

namespace FI.Domain.Classification;

/// <summary>
/// Bölüm 22: fingerprint = SHA256(integrationId + "|" + category + "|" + errorSignature).
/// Aynı entegrasyon+kategori ama farklı errorSignature -> farklı fingerprint -> ayrı incident.
/// </summary>
public static class FingerprintCalculator
{
    public const int AlgorithmVersion = 1;

    public static string Compute(Guid integrationId, EventCategory category, string errorSignature)
    {
        var canonical = $"{integrationId}|{category}|{errorSignature}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
