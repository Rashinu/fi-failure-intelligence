using FI.Domain.Classification;
using FI.Domain.Incidents;
using FI.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.IntegrationId).HasColumnName("integration_id").IsRequired();
        builder.Property(i => i.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(i => i.FingerprintAlgorithmVersion).HasColumnName("fingerprint_algorithm_version").IsRequired();
        builder.Property(i => i.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(i => i.Severity).HasColumnName("severity").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(i => i.AssigneeId).HasColumnName("assignee_id");
        builder.Property(i => i.FirstSeen).HasColumnName("first_seen").IsRequired();
        builder.Property(i => i.LastSeen).HasColumnName("last_seen").IsRequired();
        builder.Property(i => i.EventCount).HasColumnName("event_count").IsRequired();
        builder.Property(i => i.ReopenCount).HasColumnName("reopen_count").IsRequired();
        builder.Property(i => i.ResolutionSource).HasColumnName("resolution_source").HasMaxLength(30);
        builder.Property(i => i.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(i => new { i.IntegrationId, i.Fingerprint, i.FingerprintAlgorithmVersion }).IsUnique();
        builder.HasIndex(i => new { i.Status, i.LastSeen });
        builder.HasIndex(i => new { i.Severity, i.Status });

        builder.HasOne<Integration>().WithMany().HasForeignKey(i => i.IntegrationId).OnDelete(DeleteBehavior.Cascade);

        // Gercek Docker Compose ortaminda (Hangfire 20 paralel worker) ayni fingerprint'e ait
        // birden fazla ClassifyJob'un ES ZAMANLI calisip EventCount++ gibi read-modify-write
        // artislarini birbirinin uzerine yazmasi (lost update) canli bir E2E testinde bulundu.
        // Postgres'in xmin sistem sutunu optimistic concurrency token olarak kullanilir; catisma
        // olursa DbUpdateConcurrencyException firlar ve ClassifyJobHandler tum islemi yeniden
        // dener (bkz. ClassifyJobHandler.ExecuteAsync).
        builder.Property<uint>("xmin").IsRowVersion();
    }
}
