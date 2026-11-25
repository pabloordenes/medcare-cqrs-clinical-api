using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class OverbookCollisionEventConfiguration : IEntityTypeConfiguration<OverbookCollisionEvent>
{
    public void Configure(EntityTypeBuilder<OverbookCollisionEvent> builder)
    {
        builder.ToTable("overbook_collision_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.OriginalAppointmentId)
            .HasColumnName("original_appointment_id")
            .IsRequired();

        builder.Property(x => x.OverbookAppointmentId)
            .HasColumnName("overbook_appointment_id");

        builder.Property(x => x.OverbookBlockId)
            .HasColumnName("overbook_block_id")
            .IsRequired();

        builder.Property(x => x.TriggerSource)
            .HasColumnName("trigger_source")
            .HasMaxLength(OverbookCollisionEvent.MaxTriggerSourceLength)
            .IsRequired();

        builder.Property(x => x.Winner)
            .HasColumnName("winner")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.WinnerAppointmentId)
            .HasColumnName("winner_appointment_id");

        builder.Property(x => x.DisplacedAppointmentId)
            .HasColumnName("displaced_appointment_id");

        builder.Property(x => x.RuleApplied)
            .HasColumnName("rule_applied")
            .HasMaxLength(OverbookCollisionEvent.MaxRuleAppliedLength)
            .IsRequired();

        builder.Property(x => x.IsManualOverride)
            .HasColumnName("is_manual_override")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.OverrideReason)
            .HasColumnName("override_reason")
            .HasMaxLength(OverbookCollisionEvent.MaxOverrideReasonLength);

        builder.Property(x => x.OverriddenByUserId)
            .HasColumnName("overridden_by_user_id");

        builder.Property(x => x.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .IsRequired();

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.OriginalAppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.OverbookAppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ScheduleBlock>()
            .WithMany()
            .HasForeignKey(x => x.OverbookBlockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.OccurredAtUtc })
            .HasDatabaseName("IX_overbook_collision_events_tenant_occurred");

        builder.HasIndex(x => new { x.OriginalAppointmentId, x.OverbookBlockId, x.TriggerSource })
            .HasDatabaseName("IX_overbook_collision_events_lookup");
    }
}
