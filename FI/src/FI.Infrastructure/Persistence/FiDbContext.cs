using FI.Domain.AiAnalysis;
using FI.Domain.Incidents;
using FI.Domain.Ingestion;
using FI.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace FI.Infrastructure.Persistence;

public class FiDbContext : DbContext
{
    public FiDbContext(DbContextOptions<FiDbContext> options) : base(options)
    {
    }

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FiDbContext).Assembly);
    }
}
