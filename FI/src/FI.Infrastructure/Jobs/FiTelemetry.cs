using System.Diagnostics;

namespace FI.Infrastructure.Jobs;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md Teknik Borç TD8 - job handler'ların, OutboxMessage.TraceParent
/// üzerinden taşınan uzak (remote) parent context'e bağlı kendi Activity'lerini başlatabilmesi
/// için paylaşılan ActivitySource. "FI.Api" adı, ObservabilityExtensions.AddFiObservability'deki
/// `.AddSource("FI.Api")` ile eşleşir - bu isim daha önce hiçbir Activity üretmeyen, ileriye dönük
/// bırakılmış bir kanca idi; şimdi ilk kez gerçek span'ler üretiyor.
/// </summary>
public static class FiTelemetry
{
    public static readonly ActivitySource ActivitySource = new("FI.Api");

    /// <summary>
    /// Bkz. TD3 - <paramref name="traceParent"/>, OutboxMessage.TraceParent üzerinden taşınan bir
    /// W3C traceparent string'i (ör. "00-{traceId}-{spanId}-01"). Geçerliyse job'un Activity'sini
    /// o remote context'in child'ı olarak başlatır; yoksa/parse edilemiyorsa yeni bir kök trace
    /// başlatır (kaybolan bağlam yerine sessizce devam eder - trace-context propagasyonu bir
    /// gözlemlenebilirlik iyileştirmesi, iş mantığını bloklayacak kritik bir bağımlılık değil).
    /// </summary>
    public static Activity? StartLinkedActivity(string name, string? traceParent, ActivityKind kind = ActivityKind.Consumer)
    {
        if (!string.IsNullOrWhiteSpace(traceParent) && ActivityContext.TryParse(traceParent, null, out var parentContext))
            return ActivitySource.StartActivity(name, kind, parentContext);

        return ActivitySource.StartActivity(name, kind);
    }
}
