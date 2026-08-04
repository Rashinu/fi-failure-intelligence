using FI.Api.ErrorHandling;
using FI.Api.Extensions;
using FI.Api.Middleware;
using FI.Api.Security;
using FI.Domain.AiAnalysis;
using FI.Infrastructure.Ai;
using FI.Infrastructure.Persistence;
using FI.Infrastructure.Security;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;

// Bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bolum 29. JSON structured logging, Program.cs
// disaridan (Testcontainers/WebApplicationFactory) baglanti dizesi override edilmeden ONCE
// bootstrap logger olarak calisir; asil logger builder.Host.UseSerilog ile yeniden kurulur.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfig) => loggerConfig
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", "fi-api")
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Bkz. docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md P0-B - tek, tutarlı bir RFC 7807
// ProblemDetails çıktısı; controller-özel try/catch birincil strateji DEĞİL, bilinçli olarak
// dar kalan istisnalar (bkz. IncidentsController.Resolve) hâlâ kendi yerel eşlemesini korur.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Bkz. docs/CTO_REVIEW_ANALYSIS.md M17 (Product Proof) - mevcut Product API'nin uzerine ince
// bir Razor Pages sunum katmani (Incident Dashboard/Detail); ayri bir frontend projesi/deploy
// gerektirmez.
builder.Services.AddRazorPages();

builder.Services.AddFiPersistence();
builder.Services.AddFiBackgroundJobs();
builder.Services.AddFiConnectors();
builder.Services.AddFiAiAnalysis(builder.Configuration);
builder.Services.AddFiObservability(builder.Configuration);
builder.Services.AddFiRateLimiting();

// Bkz. Bölüm 33.4 - webhook secret'lar düz metin değil, Data Protection ile şifrelenir; anahtar
// halkası FiDbContext'te kalıcı tutulur (container restart/çoklu replica arasında paylaşılır).
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<FiDbContext>()
    .SetApplicationName("FI");
builder.Services.AddScoped<IWebhookSecretProtector, WebhookSecretProtector>();

var app = builder.Build();

// Bkz. Bölüm 50 madde 7 (eski davranış) ve Faz 2 "Production Readiness" kararı: migration'lar
// artık normal başlatmada OTOMATİK uygulanmaz - birden fazla replica aynı anda ayağa kalktığında
// migration'ı eşzamanlı uygulamaya çalışması race condition riski taşır. Bunun yerine ayrı bir
// "migrator" modu: `dotnet FI.Api.dll --migrate` (veya container'da aynı image, farklı komut)
// yalnızca migration'ı uygulayıp seed'i yapar ve HTTP sunucusunu hiç başlatmadan çıkar - bu,
// deploy pipeline'ında app başlamadan ÖNCE çalıştırılacak ayrı bir adımdır (bkz. docker-compose.yml
// `fi-migrate` servisi).
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
    await db.Database.MigrateAsync();

    // Bkz. docs/product/M19_CLOSE_THE_PRODUCT_LOOP.md - "Aktif Prompt Kalitesi". Bu, kasıtlı bir
    // bootstrap istisnası - PromptVersion.CreateActive'in kendi XML doc'unda belgelendiği gibi,
    // PromptPromotionGate'i yalnızca bu TEK, hiç ACTIVE versiyon yokken çalışan koşulda atlar.
    // Bootstrap sonrası herhangi bir gerçek prompt güncellemesi CreateDraft + gerçek bir
    // PromptVersionsController.Promote (golden dataset gate) akışından geçmelidir - burada bir
    // sahte eval skoru asla üretilmez (EvalOverallAverage/EvaluatedAt bilerek null bırakılır).
    if (!db.PromptVersions.Any(p => p.Status == PromptVersionStatus.Active))
    {
        db.PromptVersions.Add(PromptVersion.CreateActive(PromptTemplates.RootCauseV1Label, PromptTemplates.RootCauseV1SystemPrompt));
        await db.SaveChangesAsync();
    }

    // Bkz. docs/CTO_REVIEW_ANALYSIS.md TD6 - gerçek 2-replika testinde bulundu: Hangfire'ın kendi
    // storage şeması (CREATE SCHEMA "hangfire") normalde ilk bağlanan fi-app instance'ı tarafından
    // lazily kuruluyordu - 2+ replika soğuk başlangıçta eşzamanlı bağlanırsa bu bir yarışa girip
    // kaybeden 23505 ile çöküyordu. IGlobalConfiguration'ı burada, migrate adımının tek
    // (garantili-seri) instance'ında resolve etmek, Hangfire'ın PostgreSqlStorage kurucusunu (ve
    // dolayısıyla şema kurulumunu) sunucuyu hiç başlatmadan tetikler - herhangi bir fi-app
    // replikası ayağa kalkmadan önce şema garantili olarak var olur.
    scope.ServiceProvider.GetRequiredService<Hangfire.IGlobalConfiguration>();

    Log.Information("Migrator modu tamamlandı, çıkılıyor.");
    return;
}

// Bkz. docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md P0-A - Development/Docker Compose'un
// bilinçli olarak kullandığı zayıf placeholder secret'ların (Admin:SharedSecret, ApiKeys:Pepper)
// Production'da sessizce kabul edilmesini engeller. Yalnızca serve-mode'u etkiler - migrate
// modu yukarıda zaten "return" ile çıktığından bu kontrolün hiçbir zaman migration'ı engellemez.
var insecureProductionSecretKeys = ProductionSecretValidator.Validate(app.Configuration, app.Environment.IsProduction());
if (insecureProductionSecretKeys.Count > 0)
{
    throw new InvalidOperationException(
        $"Production ortamı, güvensiz/placeholder değerlerle başlatılamaz. " +
        $"Eksik veya yanlış yapılandırılmış anahtarlar: {string.Join(", ", insecureProductionSecretKeys)}.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.UseMiddleware<CorrelationIdMiddleware>();

// Bkz. docs/reviews/M20_1_PRODUCTION_SAFETY_REPORT.md P0-B - CorrelationIdMiddleware'den SONRA
// (traceId zaten HttpContext.Items'ta olsun diye), ama rate limiting/auth/routing/endpoint
// execution'dan ÖNCE (hepsini sarabilsin diye) kayıtlı.
app.UseExceptionHandler();

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseMiddleware<AdminBasicAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();

// Bkz. docs/CTO_REVIEW_ANALYSIS.md Due Diligence D7/D8: AdminBasicAuthMiddleware yukarıda
// "/hangfire" için zaten tam bir kimlik doğrulama kapısı uyguluyor. Hangfire'ın kendi varsayılan
// filtresi (LocalRequestsOnlyAuthorizationFilter) bunun ÜSTÜNE ikinci, bağımsız bir kontrol daha
// ekliyor - ve bu kontrol, "isteğin gerçekten localhost'tan geldiği" varsayımına dayanıyor, ki bu
// bir reverse proxy'nin veya Docker port-forwarding'in arkasında güvenilir değil (canlı doğrulandı:
// admin kimlik bilgisiyle bile Docker Compose port-forwarding üzerinden 401 döndü). Erişimi tek,
// tutarlı bir kapıda (AdminBasicAuthMiddleware) toplamak için Hangfire'ın kendi filtresini burada
// devre dışı bırakıyoruz - bu route zaten hiçbir zaman middleware'i atlayarak buraya ulaşmıyor.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new AlwaysAllowDashboardAuthorizationFilter() }
});

app.MapFiRecurringJobs();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

namespace FI.Api
{
    public partial class Program { }
}
