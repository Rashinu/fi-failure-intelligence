using System.Diagnostics.Metrics;

namespace FI.Infrastructure.Jobs;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md Teknik Borç TD2 - eşzamanlılık çakışması retry'leri
/// yalnızca tek tek log satırlarında görünüyordu, toplu bir sayaç/alarm yüzeyi yoktu. Bir
/// dakikada onlarca retry üreten gerçek bir fingerprint çekişmesi (bkz. Bölüm 5 madde 5), tek tek
/// log satırlarını taramadan fark edilemezdi. Bu, `FI.Api` OpenTelemetry meter'ına bağlı (bkz.
/// ObservabilityExtensions.AddFiObservability) basit bir sayaç.
/// </summary>
public static class FiJobMetrics
{
    private static readonly Meter Meter = new("FI.Api");

    public static readonly Counter<long> ClassifyJobConcurrencyRetries = Meter.CreateCounter<long>(
        "fi.classify_job.concurrency_retries",
        description: "ClassifyJobHandler'ın eşzamanlılık çakışması nedeniyle yeniden denediği event sınıflandırma sayısı.");
}
