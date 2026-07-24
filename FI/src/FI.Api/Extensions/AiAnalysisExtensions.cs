using FI.Domain.AiAnalysis;
using FI.Infrastructure.Ai;

namespace FI.Api.Extensions;

public static class AiAnalysisExtensions
{
    /// <summary>
    /// Bkz. Bölüm 24.3. Anthropic API'sine yapılan çağrılar, .NET 8'in standart resilience
    /// handler'ı (Polly v8 tabanlı) ile sarmalanır: retry (üstel geri çekilme + jitter,
    /// `Retry-After` header'ını otomatik olarak dikkate alır), circuit breaker (art arda hatalarda
    /// geçici olarak devre dışı bırakır) ve toplam/deneme başına timeout. Önceden yalnızca
    /// `HttpClient.Timeout` vardı - geçici ağ hatalarında/429'da tek denemeyle pes ediliyordu.
    /// </summary>
    public static IServiceCollection AddFiAiAnalysis(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.SectionName));

        services.AddHttpClient<IAiAnalysisClient, AnthropicMessagesClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.UseJitter = true;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });

        return services;
    }
}
