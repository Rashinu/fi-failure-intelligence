using FI.Api.Middleware;
using FI.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace FI.Integration.Tests.Security;

/// <summary>
/// Bkz. docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md P0-A, M20.1 onay kısıtı #3 - "yalnızca
/// Production başlangıcının gerçekten fail-fast olduğunu ve geçerli Production config'in
/// başladığını kanıtlayacak MİNİMUM host-seviyeli test". Ayrıntılı matris zaten
/// ProductionSecretValidatorTests'te saf/host'suz olarak kapsanıyor - burada yalnızca 2 senaryo
/// var (bilerek kırılgan bir WebApplicationFactory matrisi İNŞA EDİLMEDİ).
/// </summary>
public class ProductionStartupTests
{
    [Fact]
    public void Production_WithPlaceholderSecrets_FailsToStart_BeforeAnyDatabaseConnection()
    {
        // Bkz. not - guard, Program.cs'te app.Run()'dan ÖNCE, Hangfire/DB'ye hiç dokunmadan
        // tetiklenir; bu yüzden bu senaryo (fail-fast) gerçek bir Postgres GEREKTİRMEZ -
        // sahte/ulaşılamayan bir connection string yeterli.
        using var factory = new WebApplicationFactory<FI.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:FiDatabase"] = "Host=localhost;Port=59999;Database=fi_unreachable;Username=fi_app;Password=unused",
                    ["Admin:SharedSecret"] = AdminBasicAuthMiddleware.LocalDevDefault,
                    ["ApiKeys:Pepper"] = ApiKeyAuthMiddleware.LocalDevPepperDefault
                });
            });
        });

        var ex = Record.Exception(() => factory.CreateClient());
        Assert.NotNull(ex);
        Assert.Contains("Admin:SharedSecret", ex.ToString());
        Assert.Contains("ApiKeys:Pepper", ex.ToString());
    }

    [Fact]
    public async Task Production_WithValidExternallySuppliedSecrets_StartsSuccessfully()
    {
        // Bu senaryo (guard geçtikten SONRA host'un gerçekten ayağa kalkması) gerçek bir Postgres
        // gerektiriyor - UseHangfireDashboard, Program.cs'te normal serve-mode başlangıcında
        // PostgreSqlStorage üzerinden senkron olarak bağlanıyor (bu, M20.1'den ÖNCE de var olan,
        // bu görevin kapsamı dışındaki bir davranış - burada yalnızca gözlemlendi).
        await using var postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("fi_prod_startup_test")
            .WithUsername("fi_app")
            .WithPassword("test-password-local-only")
            .Build();
        await postgres.StartAsync();

        using var factory = new WebApplicationFactory<FI.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:FiDatabase"] = postgres.GetConnectionString(),
                    ["Admin:SharedSecret"] = "a-real-strong-admin-secret-from-env",
                    ["ApiKeys:Pepper"] = "a-real-strong-pepper-value-from-env"
                });
            });
        });

        // Bkz. yukarıdaki not - Hangfire'ın PostgreSqlStorage'ı yalnızca ULAŞILABİLİR bir
        // Postgres'e ham bir bağlantı açabilmeyi gerektiriyor (şemayı kendi lazily kuruyor,
        // bkz. TD6 notu Program.cs'te); bu yüzden burada FiDbContext migration'ı ÖNCEDEN
        // çalıştırmaya gerek yok - tek koşul, host'un ilk erişimde (CreateClient) fırlamadan
        // ayağa kalkması.
        var ex = Record.Exception(() => factory.CreateClient());
        Assert.Null(ex);
    }
}
