using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FI.Api.Extensions;

/// <summary>
/// Bkz. docs/CTO_REVIEW_ANALYSIS.md Due Diligence D9 - hiçbir yerde rate limiting yoktu.
/// D7 ile control plane artık authentication gerektiriyor, ama ingestion endpoint'leri
/// (/api/v1/events, /api/v1/webhooks, /api/v1/deployments) hâlâ yalnızca bir API key
/// gerektiriyor - geçerli (veya çalınmış) bir key'e sahip biri, hacimli istekle hem DB'yi hem de
/// (evidence varsa) faturalandırılan Anthropic API çağrılarını kontrolsüz tüketebilir. Bu,
/// yalnızca "/api/v1" altındaki rotalar için IP başına minimal bir sabit-pencere limiti - Razor
/// dashboard, Hangfire, health check'ler kapsam dışı (zaten authentication ile veya insan
/// etkileşimiyle sınırlı).
/// </summary>
public static class RateLimitingExtensions
{
    private const int PermitLimit = 100;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    public static IServiceCollection AddFiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (!httpContext.Request.Path.StartsWithSegments("/api/v1"))
                    return RateLimitPartition.GetNoLimiter("unrestricted");

                var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermitLimit,
                    Window = Window,
                    QueueLimit = 0
                });
            });
        });

        return services;
    }
}
