using FI.Domain.AiAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FI.Infrastructure.Persistence.Configurations;

public class PromptVersionConfiguration : IEntityTypeConfiguration<PromptVersion>
{
    public void Configure(EntityTypeBuilder<PromptVersion> builder)
    {
        builder.ToTable("prompt_versions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.VersionLabel).HasColumnName("version_label").HasMaxLength(50).IsRequired();
        builder.Property(p => p.SystemPromptTemplate).HasColumnName("system_prompt_template").IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.RolloutPercentage).HasColumnName("rollout_percentage").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.EvalOverallAverage).HasColumnName("eval_overall_average");
        builder.Property(p => p.EvalPerDimensionJson).HasColumnName("eval_per_dimension").HasColumnType("jsonb");
        builder.Property(p => p.EvaluatedAt).HasColumnName("evaluated_at");

        builder.HasIndex(p => p.VersionLabel).IsUnique();
    }
}
