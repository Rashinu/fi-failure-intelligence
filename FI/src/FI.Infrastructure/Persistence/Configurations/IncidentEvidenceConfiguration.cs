using FI.Domain.Incidents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class IncidentEvidenceConfiguration : IEntityTypeConfiguration<IncidentEvidence>
{
    public void Configure(EntityTypeBuilder<IncidentEvidence> builder)
    {
        builder.ToTable("incident_evidence");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.IncidentId).HasColumnName("incident_id").IsRequired();
        builder.Property(e => e.SourceType).HasColumnName("source_type").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.SourceId).HasColumnName("source_id");
        builder.Property(e => e.Summary).HasColumnName("summary").IsRequired();
        builder.Property(e => e.StructuredData).HasColumnName("structured_data").HasColumnType("jsonb");
        builder.Property(e => e.WindowStart).HasColumnName("window_start");
        builder.Property(e => e.WindowEnd).HasColumnName("window_end");
        builder.Property(e => e.CollectedAt).HasColumnName("collected_at").IsRequired();

        builder.HasIndex(e => new { e.IncidentId, e.CollectedAt });

        builder.HasOne<Incident>().WithMany().HasForeignKey(e => e.IncidentId).OnDelete(DeleteBehavior.Cascade);
    }
}
