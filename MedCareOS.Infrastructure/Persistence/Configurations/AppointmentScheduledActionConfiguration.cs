using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class AppointmentScheduledActionConfiguration : IEntityTypeConfiguration<AppointmentScheduledAction>
{
    public void Configure(EntityTypeBuilder<AppointmentScheduledAction> builder)
    {
        builder.ToTable("appointment_scheduled_actions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.AppointmentId)
            .HasColumnName("appointment_id")
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.DueAt)
            .HasColumnName("due_at")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(x => x.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.MaxAttempts)
            .HasColumnName("max_attempts")
            .HasDefaultValue(5)
            .IsRequired();

        builder.Property(x => x.LockedBy)
            .HasColumnName("locked_by")
            .HasMaxLength(AppointmentScheduledAction.MaxLockedByLength);

        builder.Property(x => x.LockedAt)
            .HasColumnName("locked_at");

        builder.Property(x => x.LeaseUntil)
            .HasColumnName("lease_until");

        builder.Property(x => x.LockToken)
            .HasColumnName("lock_token")
            .IsConcurrencyToken();

        builder.Property(x => x.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(AppointmentScheduledAction.MaxErrorLength);

        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder.Property(x => x.PlanVersion)
            .HasColumnName("plan_version")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(x => x.MetadataJson)
            .HasColumnName("metadata_json")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.DueAt)
            .HasDatabaseName("IX_scheduled_actions_due_claim")
            .HasFilter("status = 'Pending'");

        builder.HasIndex(x => x.LeaseUntil)
            .HasDatabaseName("IX_scheduled_actions_expired_lease")
            .HasFilter("status = 'Processing'");

        builder.HasIndex(x => new { x.AppointmentId, x.Kind })
            .HasDatabaseName("UX_scheduled_actions_active_appointment_kind")
            .IsUnique()
            .HasFilter("status IN ('Pending', 'Processing')");

        builder.HasIndex(x => new { x.AppointmentId, x.Status })
            .HasDatabaseName("IX_scheduled_actions_appointment_status");
    }
}
