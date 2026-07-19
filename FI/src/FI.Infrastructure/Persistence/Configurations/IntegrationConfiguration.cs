using FI.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class IntegrationConfiguration : IEntityTypeConfiguration<Integration>
{
    public void Configure(EntityTypeBuilder<Integration> builder)
    {
        builder.ToTable("integrations");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(i => i.Provider).HasColumnName("provider").HasMaxLength(100).IsRequired();
        builder.Property(i => i.Environment).HasColumnName("environment").HasMaxLength(50).IsRequired();
        builder.Property(i => i.Owner).HasColumnName("owner").HasMaxLength(200).IsRequired();
        builder.Property(i => i.EndpointUrl).HasColumnName("endpoint_url").HasMaxLength(500);
        builder.Property(i => i.BusinessCriticality)
            .HasColumnName("business_criticality")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(i => i.WebhookSecret).HasColumnName("webhook_secret").HasMaxLength(200);

        builder.HasIndex(i => new { i.Name, i.Environment }).IsUnique();

        builder.HasMany(i => i.ApiKeys)
            .WithOne()
            .HasForeignKey(k => k.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.ApiKeys).HasField("_apiKeys").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
