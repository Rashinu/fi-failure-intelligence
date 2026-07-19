using FI.Domain.Ingestion;

namespace FI.Domain.Connectors;

/// <summary>
/// Bölüm 34'teki <c>NormalizedEvent</c>'in FI'ye özgü karşılığı. <c>RequestJson</c>/<c>ResponseJson</c>
/// bilinçli olarak zaten <see cref="Infrastructure.Jobs.ClassifyJobHandler"/>'ın beklediği şekle
/// (headers.X-Signature-Valid, headers.Retry-After, error.code, path) sahiptir — bu sayede
/// connector'lar deterministik sınıflandırma kuralını (Bölüm 21) tekrar implemente etmez,
/// mevcut EventClassifier tek gerçek kaynak olarak kalır.
/// </summary>
public sealed record NormalizedEvent(
    IntegrationEventType EventType,
    int StatusCode,
    string? RequestJson,
    string? ResponseJson,
    int? LatencyMs,
    DateTimeOffset OccurredAt,
    string? ProviderEventId);
