using FI.Api.Middleware;
using FI.Domain.AiAnalysis;
using FI.Domain.Connectors;
using FI.Infrastructure.Ai;
using FI.Infrastructure.Connectors;
using FI.Infrastructure.Jobs;
using FI.Infrastructure.Persistence;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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

// Baglanti dizesi her tuketicide IConfiguration uzerinden tembel (lazy) okunur - Program.cs'in
// ust seviye kodunda bir yerel degiskene erken okunmasi, WebApplicationFactory tabanli
// entegrasyon testlerinde Testcontainers'in ConfigureAppConfiguration override'ini gormeden
// once calisip yanlis (appsettings.json) baglanti dizesini yakaliyordu. Bkz. ADR-015.
static string GetConnectionString(IServiceProvider sp) =>
    sp.GetRequiredService<IConfiguration>().GetConnectionString("FiDatabase")!;

builder.Services.AddDbContext<FiDbContext>((sp, options) => options.UseNpgsql(GetConnectionString(sp)));

builder.Services.AddHealthChecks()
    .AddNpgSql(sp => GetConnectionString(sp), name: "postgresql", tags: new[] { "ready" });

builder.Services.AddHangfire((sp, config) => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(GetConnectionString(sp))));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<ClassifyJobHandler>();
builder.Services.AddScoped<OutboxDispatcher>();
builder.Services.AddScoped<EvidenceCollectorJobHandler>();
builder.Services.AddScoped<AiAnalysisJobHandler>();
builder.Services.AddScoped<ApiKeyGracePeriodRevocationJobHandler>();
builder.Services.AddScoped<FI.Infrastructure.Eval.PromptVersionPromotionService>();

// Bkz. Bolum 34 - Mock Stripe/GitHub/SES/SendGrid connector'lari, ProviderKey'e gore dictionary
// lookup ile IConnectorRegistry uzerinden cozulur.
builder.Services.AddSingleton<IIntegrationConnector, StripeConnector>();
builder.Services.AddSingleton<IIntegrationConnector, SesConnector>();
builder.Services.AddSingleton<IIntegrationConnector, SendGridConnector>();
builder.Services.AddSingleton<IDeploymentConnector, GitHubDeploymentConnector>();
builder.Services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();

builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.AddHttpClient<IAiAnalysisClient, AnthropicMessagesClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Bkz. Bolum 30 - span hiyerarsisi: IngestEvent/ClassifyEvent/CollectEvidence/AIAnalysis zinciri
// ayni fi.correlation_id ile iliskilendirilir (CorrelationIdMiddleware, Activity'ye tag ekler).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "fi-api", serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        .AddSource("FI.Api")
        .AddAspNetCoreInstrumentation(o => o.Filter = ctx => ctx.Request.Path != "/health/live" && ctx.Request.Path != "/health/ready")
        .AddHttpClientInstrumentation()
        .AddConsoleExporter());

var app = builder.Build();

// Container başlangıcında migration'ı otomatik uygula (bkz. docs/FAILURE_INTELLIGENCE_ARCHITECTURE.md Bölüm 50, madde 7).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FiDbContext>();
    db.Database.Migrate();

    // Bolum 26.3: M5'te tek bir ACTIVE prompt version (v1) seed edilir.
    if (!db.PromptVersions.Any(p => p.Status == PromptVersionStatus.Active))
    {
        db.PromptVersions.Add(PromptVersion.CreateActive(PromptTemplates.RootCauseV1Label, PromptTemplates.RootCauseV1SystemPrompt));
        db.SaveChanges();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.UseHangfireDashboard("/hangfire");

// Outbox dispatcher: bekleyen ClassifyJob kayıtlarını 5 saniyede bir enqueue eder (Bölüm 20.3).
RecurringJob.AddOrUpdate<OutboxDispatcher>(
    "outbox-dispatcher",
    dispatcher => dispatcher.DispatchPendingAsync(),
    "*/5 * * * * *");

// Bölüm 33.4 - rotasyon grace period'u (24sa) dolan API key'leri saatte bir revoke eder.
RecurringJob.AddOrUpdate<ApiKeyGracePeriodRevocationJobHandler>(
    "api-key-grace-period-revocation",
    handler => handler.ExecuteAsync(CancellationToken.None),
    "0 * * * *");

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
