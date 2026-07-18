using FI.Api.Middleware;
using FI.Domain.AiAnalysis;
using FI.Infrastructure.Ai;
using FI.Infrastructure.Jobs;
using FI.Infrastructure.Persistence;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.AddHttpClient<IAiAnalysisClient, AnthropicMessagesClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

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
