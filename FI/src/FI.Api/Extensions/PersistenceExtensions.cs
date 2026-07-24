using FI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FI.Api.Extensions;

public static class PersistenceExtensions
{
    /// <summary>
    /// Bağlantı dizesi her tüketicide IConfiguration üzerinden tembel (lazy) okunur - Program.cs'in
    /// üst seviye kodunda bir yerel değişkene erken okunması, WebApplicationFactory tabanlı
    /// entegrasyon testlerinde Testcontainers'ın ConfigureAppConfiguration override'ını görmeden
    /// önce çalışıp yanlış (appsettings.json) bağlantı dizesini yakalıyordu. Bkz. ADR-015.
    /// </summary>
    public static string GetConnectionString(this IServiceProvider sp) =>
        sp.GetRequiredService<IConfiguration>().GetConnectionString("FiDatabase")!;

    public static IServiceCollection AddFiPersistence(this IServiceCollection services)
    {
        services.AddDbContext<FiDbContext>((sp, options) => options.UseNpgsql(sp.GetConnectionString()));

        services.AddHealthChecks()
            .AddNpgSql(sp => sp.GetConnectionString(), name: "postgresql", tags: new[] { "ready" });

        return services;
    }
}
