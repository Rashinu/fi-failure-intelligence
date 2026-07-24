using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FI.Api.Extensions;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Bkz. Bölüm 30 - span hiyerarşisi: IngestEvent/ClassifyEvent/CollectEvidence/AIAnalysis
    /// zinciri aynı fi.correlation_id ile ilişkilendirilir. `Otel:OtlpEndpoint` yapılandırılmışsa
    /// (ör. Seq, Grafana Tempo, Jaeger) OTLP exporter da eklenir; yapılandırılmamışsa yalnızca
    /// konsol exporter kullanılır (yerel geliştirme varsayılanı, hiçbir ek altyapı gerektirmez).
    /// </summary>
    public static IServiceCollection AddFiObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["Otel:OtlpEndpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName: "fi-api", serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource("FI.Api")
                    .AddAspNetCoreInstrumentation(o => o.Filter = ctx => ctx.Request.Path != "/health/live" && ctx.Request.Path != "/health/ready")
                    .AddHttpClientInstrumentation()
                    .AddConsoleExporter();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return services;
    }
}
