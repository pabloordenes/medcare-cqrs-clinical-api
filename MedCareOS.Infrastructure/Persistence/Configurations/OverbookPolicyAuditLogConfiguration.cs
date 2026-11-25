using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class OverbookPolicyAuditLogConfiguration : IEntityTypeConfiguration<OverbookPolicyAuditLog>
{
    public void Configure(EntityTypeBuilder<OverbookPolicyAuditLog> builder)
    {
        builder.ToTable("overbook_policy_audit_logs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id").IsRequired();
        builder.Property(x => x.PreviousAutoOverbookEnabled).HasColumnName("previous_auto_overbook_enabled").IsRequired();
        builder.Property(x => x.NewAutoOverbookEnabled).HasColumnName("new_auto_overbook_enabled").IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasMaxLength(OverbookPolicyAuditLog.MaxReasonLength)
            .IsRequired();

        builder.Property(x => x.EvaluatedCountAtChange).HasColumnName("evaluated_count_at_change").IsRequired();
        builder.Property(x => x.FalsePositiveRateAtChange)
            .HasColumnName("false_positive_rate_at_change")
            .HasColumnType("numeric(5,4)")
            .IsRequired();
        builder.Property(x => x.GatePassed).HasColumnName("gate_passed").IsRequired();
        builder.Property(x => x.ForcedOverride).HasColumnName("forced_override").IsRequired();
        builder.Property(x => x.ChangedAtUtc).HasColumnName("changed_at_utc").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ChangedAtUtc })
            .HasDatabaseName("IX_overbook_policy_audit_logs_tenant_time");
    }
}
