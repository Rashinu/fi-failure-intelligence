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
/// </summary>
public class FiApiFactory : WebApplicationFactory<FI.Api.Program>, IAsyncLifetime
{
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
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
    }
}
