using FI.Domain.Ingestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class DeploymentConfiguration : IEntityTypeConfiguration<Deployment>
{
    public void Configure(EntityTypeBuilder<Deployment> builder)
    {
        builder.ToTable("deployments");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.IntegrationId).HasColumnName("integration_id");
        builder.Property(d => d.Service).HasColumnName("service").HasMaxLength(200).IsRequired();
        builder.Property(d => d.Environment).HasColumnName("environment").HasMaxLength(50).IsRequired();
        builder.Property(d => d.Commit).HasColumnName("commit").HasMaxLength(100).IsRequired();
        builder.Property(d => d.ChangedConfig).HasColumnName("changed_config").HasColumnType("jsonb");
        builder.Property(d => d.DeployedAt).HasColumnName("deployed_at").IsRequired();
        builder.Property(d => d.ReceivedAt).HasColumnName("received_at").IsRequired();

        builder.HasOne<Integration>().WithMany().HasForeignKey(d => d.IntegrationId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(d => new { d.Service, d.Environment, d.DeployedAt });
    }
}
