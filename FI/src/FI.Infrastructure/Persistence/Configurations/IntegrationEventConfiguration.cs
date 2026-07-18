using FI.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class IntegrationEventConfiguration : IEntityTypeConfiguration<IntegrationEvent>
{
    public void Configure(EntityTypeBuilder<IntegrationEvent> builder)
    {
        builder.ToTable("integration_events", t => t.HasCheckConstraint(
            "ck_events_status_code", "status_code BETWEEN 100 AND 599"));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.IntegrationId).HasColumnName("integration_id").IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(e => e.StatusCode).HasColumnName("status_code").IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(50);
        builder.Property(e => e.RequestRedacted).HasColumnName("request_redacted").HasColumnType("jsonb");
        builder.Property(e => e.ResponseRedacted).HasColumnName("response_redacted").HasColumnType("jsonb");
        builder.Property(e => e.LatencyMs).HasColumnName("latency_ms");
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").IsRequired();
        builder.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        builder.Property(e => e.ApiKeyId).HasColumnName("api_key_id");
        builder.Property(e => e.IsSignatureVerified).HasColumnName("is_signature_verified");
        builder.Property(e => e.PayloadSizeBytes).HasColumnName("payload_size_bytes").IsRequired();
        builder.Property(e => e.IsTruncated).HasColumnName("is_truncated").IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(e => e.ReceivedAt).HasColumnName("received_at").IsRequired();

        builder.HasIndex(e => new { e.IntegrationId, e.OccurredAt });
        builder.HasIndex(e => e.CorrelationId);

        builder.HasOne<Integration>().WithMany().HasForeignKey(e => e.IntegrationId).OnDelete(DeleteBehavior.Cascade);
    }
}
