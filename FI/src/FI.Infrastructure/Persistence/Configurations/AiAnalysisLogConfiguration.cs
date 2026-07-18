using FI.Domain.AiAnalysis;
using FI.Domain.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class AiAnalysisLogConfiguration : IEntityTypeConfiguration<AiAnalysisLog>
{
    public void Configure(EntityTypeBuilder<AiAnalysisLog> builder)
    {
        builder.ToTable("ai_analysis_logs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.IncidentId).HasColumnName("incident_id").IsRequired();
        builder.Property(l => l.PromptVersionId).HasColumnName("prompt_version_id").IsRequired();
        builder.Property(l => l.ModelVersion).HasColumnName("model_version").HasMaxLength(100).IsRequired();
        builder.Property(l => l.ParseSuccess).HasColumnName("parse_success").IsRequired();
        builder.Property(l => l.SchemaEchoMismatch).HasColumnName("schema_echo_mismatch").IsRequired();
        builder.Property(l => l.Confidence).HasColumnName("confidence");
        builder.Property(l => l.OutOfEvidenceClaimsDetected).HasColumnName("out_of_evidence_claims_detected").IsRequired();
        builder.Property(l => l.InputTokens).HasColumnName("input_tokens");
        builder.Property(l => l.OutputTokens).HasColumnName("output_tokens");
        builder.Property(l => l.LatencyMs).HasColumnName("latency_ms").IsRequired();
        builder.Property(l => l.ErrorMessage).HasColumnName("error_message");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(l => new { l.IncidentId, l.CreatedAt });
        builder.HasOne<Incident>().WithMany().HasForeignKey(l => l.IncidentId).OnDelete(DeleteBehavior.Cascade);
    }
}
