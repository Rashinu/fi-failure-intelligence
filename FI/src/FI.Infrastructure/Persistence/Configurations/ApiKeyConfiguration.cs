using FI.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id).HasColumnName("id");
        builder.Property(k => k.IntegrationId).HasColumnName("integration_id").IsRequired();
        builder.Property(k => k.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(12).IsRequired();
        builder.Property(k => k.KeyHash).HasColumnName("key_hash").HasMaxLength(200).IsRequired();
        builder.Property(k => k.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(k => k.LastRotatedAt).HasColumnName("last_rotated_at");
        builder.Property(k => k.RevokedAt).HasColumnName("revoked_at");
        builder.Property(k => k.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(k => k.UsageCount).HasColumnName("usage_count").IsRequired();

        builder.HasIndex(k => k.KeyHash).IsUnique();
    }
}
