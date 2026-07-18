using FI.Domain.AiAnalysis;
using FI.Domain.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class AiIncidentAnalysisConfiguration : IEntityTypeConfiguration<AiIncidentAnalysis>
{
    public void Configure(EntityTypeBuilder<AiIncidentAnalysis> builder)
    {
        builder.ToTable("ai_analyses");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.IncidentId).HasColumnName("incident_id").IsRequired();
        builder.Property(a => a.PromptVersionId).HasColumnName("prompt_version_id").IsRequired();
        builder.Property(a => a.ModelVersion).HasColumnName("model_version").HasMaxLength(100).IsRequired();
        builder.Property(a => a.IncidentTitle).HasColumnName("incident_title").HasMaxLength(120).IsRequired();
        builder.Property(a => a.ProbableRootCause).HasColumnName("probable_root_cause").HasMaxLength(500).IsRequired();
        builder.Property(a => a.EvidenceJson).HasColumnName("evidence").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.EvidenceRefsJson).HasColumnName("evidence_refs").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.RecommendedActionsJson).HasColumnName("recommended_actions").HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.Confidence).HasColumnName("confidence").IsRequired();
        builder.Property(a => a.NeedsHumanReview).HasColumnName("needs_human_review").IsRequired();
        builder.Property(a => a.OutOfEvidenceClaimsDetected).HasColumnName("out_of_evidence_claims_detected").IsRequired();
        builder.Property(a => a.IsLatest).HasColumnName("is_latest").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(a => new { a.IncidentId, a.IsLatest });
        builder.HasOne<Incident>().WithMany().HasForeignKey(a => a.IncidentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PromptVersion>().WithMany().HasForeignKey(a => a.PromptVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
