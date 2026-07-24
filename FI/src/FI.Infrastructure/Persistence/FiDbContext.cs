using FI.Domain.AiAnalysis;
using FI.Domain.Audit;
using FI.Domain.Incidents;
using FI.Domain.Ingestion;
using FI.Domain.Outbox;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FI.Infrastructure.Persistence;

/// <summary>
/// <see cref="IDataProtectionKeyContext"/> uygulaması, ASP.NET Core Data Protection anahtarlarının
/// (webhook secret şifrelemesi için, bkz. WebhookSecretProtector) container restart'ları/birden
/// fazla replica arasında kalıcı ve paylaşılan olmasını sağlar - varsayılan dosya sistemi tabanlı
/// key ring, container'lar arası paylaşılmadığı için production'da uygun değildir.
/// </summary>
public class FiDbContext : DbContext, IDataProtectionKeyContext
{
    public FiDbContext(DbContextOptions<FiDbContext> options) : base(options)
    {
    }

    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<IntegrationEvent> IntegrationEvents => Set<IntegrationEvent>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<IngestionIdempotencyKey> IngestionIdempotencyKeys => Set<IngestionIdempotencyKey>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentEvidence> IncidentEvidence => Set<IncidentEvidence>();
    public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();
    public DbSet<AiIncidentAnalysis> AiAnalyses => Set<AiIncidentAnalysis>();
    public DbSet<AiAnalysisLog> AiAnalysisLogs => Set<AiAnalysisLog>();
    public DbSet<IncidentReview> IncidentReviews => Set<IncidentReview>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FiDbContext).Assembly);
    }
}
