namespace FI.Domain.Classification;

/// <summary>
/// Bölüm 21'deki rule engine'in girdisi. Tüm I/O (DB sorgusu, JSON ayrıştırma) çağıran katmanda
/// (ClassifyJobHandler) yapılır; bu record yalnızca sonuçları taşır — EventClassifier saf kalır.
/// </summary>
public sealed record ClassificationInput(
    int StatusCode,
    bool HasInvalidSignatureHeader,
    bool HasRetryAfterHeader,
    string? ErrorBodyText,
    bool HasSchemaValidationFailure,
    IReadOnlyList<string> MissingSchemaFields,
    bool IsDuplicateWithinWindow,
    bool IsTimeoutError,
    bool IsNetworkError,
    string? NetworkExceptionType,
    string? NormalizedErrorCode,
    string? NormalizedEndpointPath);
