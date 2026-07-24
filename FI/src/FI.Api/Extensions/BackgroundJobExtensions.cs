using FI.Infrastructure.Jobs;
using Hangfire;
using Hangfire.PostgreSql;

namespace FI.Api.Extensions;

public static class BackgroundJobExtensions
{
    public static IServiceCollection AddFiBackgroundJobs(this IServiceCollection services)
    {
        services.AddHangfire((sp, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(sp.GetConnectionString())));
        services.AddHangfireServer();

        services.AddScoped<ClassifyJobHandler>();
        services.AddScoped<OutboxDispatcher>();
        services.AddScoped<EvidenceCollectorJobHandler>();
        services.AddScoped<AiAnalysisJobHandler>();
        services.AddScoped<ApiKeyGracePeriodRevocationJobHandler>();
        services.AddScoped<FI.Infrastructure.Eval.PromptVersionPromotionService>();

        return services;
    }

    /// <summary>Bkz. Bölüm 20.3 (outbox dispatcher) ve Bölüm 33.4 (API key grace period).</summary>
    public static WebApplication MapFiRecurringJobs(this WebApplication app)
    {
        RecurringJob.AddOrUpdate<OutboxDispatcher>(
            "outbox-dispatcher",
            dispatcher => dispatcher.DispatchPendingAsync(),
            "*/5 * * * * *");

        RecurringJob.AddOrUpdate<ApiKeyGracePeriodRevocationJobHandler>(
            "api-key-grace-period-revocation",
            handler => handler.ExecuteAsync(CancellationToken.None),
            "0 * * * *");

        return app;
    }
}
