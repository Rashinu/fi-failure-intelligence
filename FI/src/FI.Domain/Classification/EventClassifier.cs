using System.Text.RegularExpressions;

namespace FI.Domain.Classification;

/// <summary>
/// Deterministik rule engine (Bölüm 21). AI bu sınıflandırmanın SORUMLUSU DEĞİLDİR — bu sınıf
/// saf, framework'ten bağımsız bir fonksiyondur ve tablo-güdümlü testlerle doğrulanır.
/// İlk eşleşen kural kategoriyi belirler; hiçbiri eşleşmezse UnknownError.
/// </summary>
public static class EventClassifier
{
    private static readonly Regex SignatureInvalidRegex = new("invalid.*signature", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SignatureCodeRegex = new("signature_verification_failed|invalid_hmac", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AuthMessageRegex = new("invalid[_ ]api[_ ]key|unauthorized|invalid[_ ]token", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AuthzMessageRegex = new("insufficient[_ ]scope|permission[_ ]denied|forbidden", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RateLimitMessageRegex = new("rate[_ ]limit", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ClassificationResult Classify(ClassificationInput input)
    {
        var body = input.ErrorBodyText ?? string.Empty;

        // 1. SIGNATURE_ERROR
        if (input.HasInvalidSignatureHeader || SignatureInvalidRegex.IsMatch(body) || SignatureCodeRegex.IsMatch(body))
            return new ClassificationResult(EventCategory.SignatureError, "signature_mismatch");

        // 2. AUTHENTICATION_ERROR
        if (input.StatusCode == 401 || (input.StatusCode == 403 && AuthMessageRegex.IsMatch(body)))
        {
            var code = input.NormalizedErrorCode ?? "unknown";
            return new ClassificationResult(EventCategory.AuthenticationError, $"{input.StatusCode}_{code}");
        }

        // 3. AUTHORIZATION_ERROR
        if (input.StatusCode == 403 && AuthzMessageRegex.IsMatch(body))
            return new ClassificationResult(EventCategory.AuthorizationError, $"{input.StatusCode}_{input.NormalizedErrorCode ?? "forbidden"}");

        // 4. RATE_LIMIT_ERROR
        if (input.StatusCode == 429 || input.HasRetryAfterHeader || RateLimitMessageRegex.IsMatch(body))
            return new ClassificationResult(EventCategory.RateLimitError, "rate_limit");

        // 5. SCHEMA_MISMATCH
        if (input.HasSchemaValidationFailure)
        {
            var fields = input.MissingSchemaFields.Count > 0
                ? string.Join(",", input.MissingSchemaFields.OrderBy(f => f, StringComparer.Ordinal).Select(f => $"field_missing:{f}"))
                : "field_missing:unknown";
            return new ClassificationResult(EventCategory.SchemaMismatch, fields);
        }

        // 6. DUPLICATE_EVENT
        if (input.IsDuplicateWithinWindow)
            return new ClassificationResult(EventCategory.DuplicateEvent, "duplicate");

        // 7. TIMEOUT
        if (input.IsTimeoutError)
            return new ClassificationResult(EventCategory.Timeout, input.NormalizedEndpointPath ?? "unknown_path");

        // 8. PROVIDER_ERROR
        if (input.StatusCode is >= 500 and < 600)
            return new ClassificationResult(EventCategory.ProviderError, input.StatusCode.ToString());

        // 9. CLIENT_ERROR_OTHER
        if (input.StatusCode is >= 400 and < 500)
            return new ClassificationResult(EventCategory.ClientErrorOther, input.StatusCode.ToString());

        // 10. NETWORK_ERROR
        if (input.IsNetworkError)
            return new ClassificationResult(EventCategory.NetworkError, $"{input.NetworkExceptionType ?? "unknown"}_flag");

        // 11. UNKNOWN_ERROR (fallback, her zaman needsHumanReview)
        var normalizedPrefix = body.Length > 200 ? body[..200] : body;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalizedPrefix)));
        return new ClassificationResult(EventCategory.UnknownError, hash);
    }
}
