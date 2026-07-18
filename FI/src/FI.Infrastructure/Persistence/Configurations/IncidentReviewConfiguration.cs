using FI.Domain.AiAnalysis;
using FI.Domain.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class IncidentReviewConfiguration : IEntityTypeConfiguration<IncidentReview>
{
    public void Configure(EntityTypeBuilder<IncidentReview> builder)
    {
        builder.ToTable("incident_reviews");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.IncidentId).HasColumnName("incident_id").IsRequired();
        builder.Property(r => r.AiAnalysisId).HasColumnName("ai_analysis_id");
        builder.Property(r => r.Decision).HasColumnName("decision").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.FinalContentJson).HasColumnName("final_content").HasColumnType("jsonb");
        builder.Property(r => r.ReviewerNotes).HasColumnName("reviewer_notes");
        builder.Property(r => r.ReviewedAt).HasColumnName("reviewed_at").IsRequired();

        builder.HasIndex(r => new { r.IncidentId, r.ReviewedAt });
        builder.HasOne<Incident>().WithMany().HasForeignKey(r => r.IncidentId).OnDelete(DeleteBehavior.Cascade);
    }
}
