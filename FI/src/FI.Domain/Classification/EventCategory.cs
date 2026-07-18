namespace FI.Domain.Classification;

/// <summary>
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 21 — 11 kategorilik kanonik taksonomi.
/// Sıra önemlidir: rule engine bu önceliğe göre üstten alta değerlendirir.
/// </summary>
public enum EventCategory
{
    SignatureError = 1,
    AuthenticationError = 2,
    AuthorizationError = 3,
    RateLimitError = 4,
    SchemaMismatch = 5,
    DuplicateEvent = 6,
    Timeout = 7,
    ProviderError = 8,
    ClientErrorOther = 9,
    NetworkError = 10,
    UnknownError = 11
}
