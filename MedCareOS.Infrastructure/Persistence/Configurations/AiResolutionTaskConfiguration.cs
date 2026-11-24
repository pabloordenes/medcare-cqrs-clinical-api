using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class AiResolutionTaskConfiguration : IEntityTypeConfiguration<AiResolutionTask>
{
    public void Configure(EntityTypeBuilder<AiResolutionTask> builder)
    {
        builder.ToTable("ai_resolution_tasks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ResourceId)
            .HasColumnName("resource_id")
            .IsRequired();

        builder.Property(x => x.RiskProbability)
            .HasColumnName("risk_probability")
            .HasColumnType("numeric(5,4)");

        // Crucial: jsonb para almacenar el plan de Semantic Kernel de forma indexable
        builder.Property(x => x.AiPlanJson)
            .HasColumnName("ai_plan_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("estado")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.ApprovedBy)
            .HasColumnName("approved_by");

        builder.Property(x => x.ApprovedAt)
            .HasColumnName("approved_at");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasIndex(x => new { x.ResourceId, x.Status })
            .HasDatabaseName("ix_ai_resolution_tasks_resource_status");
    }
}