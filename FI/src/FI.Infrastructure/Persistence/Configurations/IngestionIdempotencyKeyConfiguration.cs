using FI.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class IngestionIdempotencyKeyConfiguration : IEntityTypeConfiguration<IngestionIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IngestionIdempotencyKey> builder)
    {
        builder.ToTable("ingestion_idempotency_keys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id).HasColumnName("id");
        builder.Property(k => k.IntegrationId).HasColumnName("integration_id").IsRequired();
        builder.Property(k => k.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(k => k.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        builder.Property(k => k.ResourceType).HasColumnName("resource_type").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(k => k.ResourceId).HasColumnName("resource_id").IsRequired();
        builder.Property(k => k.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(k => new { k.IntegrationId, k.IdempotencyKey }).IsUnique();
    }
}
