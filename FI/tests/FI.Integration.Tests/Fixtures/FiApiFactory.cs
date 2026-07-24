using FI.Domain.AiAnalysis;
using FI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace FI.Integration.Tests.Fixtures;

/// <summary>
/// Gerçek bir PostgreSQL container'ı ayağa kaldırıp FI.Api'yi buna karşı test eder.
/// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md ADR-006 (Testcontainers stratejisi).
/// Gerçek Anthropic API çağrısı yerine FakeAiAnalysisClient kullanılır (Bölüm 38.1).
/// </summary>
public class FiApiFactory : WebApplicationFactory<FI.Api.Program>, IAsyncLifetime
{
    public FakeAiAnalysisClient FakeAiClient { get; } = new();

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("fi_test")
        .WithUsername("fi_app")
        .WithPassword("test-password-local-only")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs artik baglanti dizesini her tuketicide (DbContext/health check/Hangfire)
        // IConfiguration uzerinden DI build zamaninda tembel okuyor (bkz. ADR-015), bu yuzden
        // ConfigureAppConfiguration ile eklenen override guvenilir sekilde etkili oluyor.
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FiDatabase"] = _postgres.GetConnectionString()
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAiAnalysisClient));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddSingleton<IAiAnalysisClient>(FakeAiClient);
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Bkz. Faz 2 "Production Readiness" karari: migration'lar artik uygulama baslangicinda
        // otomatik uygulanmiyor (bkz. Program.cs "--migrate" modu). Testler icin burada, gercek
        // deploy pipeline'indaki ayri migration adimini taklit ederek acikca cagirilir.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
        await db.Database.MigrateAsync();

        if (!db.PromptVersions.Any(p => p.Status == FI.Domain.AiAnalysis.PromptVersionStatus.Active))
        {
            db.PromptVersions.Add(FI.Domain.AiAnalysis.PromptVersion.CreateActive(
                FI.Infrastructure.Ai.PromptTemplates.RootCauseV1Label,
                FI.Infrastructure.Ai.PromptTemplates.RootCauseV1SystemPrompt));
            await db.SaveChangesAsync();
        }
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
    }
}
