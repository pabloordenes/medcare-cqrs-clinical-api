using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class ScheduleBlockConfiguration : IEntityTypeConfiguration<ScheduleBlock>
{
    public void Configure(EntityTypeBuilder<ScheduleBlock> builder)
    {
        builder.ToTable("schedule_blocks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.StaffId)
            .HasColumnName("medico_id")
            .IsRequired();

        builder.Property(x => x.BoxId)
            .HasColumnName("box_id")
            .IsRequired();

        builder.Property(x => x.StartTime)
            .HasColumnName("fecha_inicio")
            .IsRequired();

        builder.Property(x => x.EndTime)
            .HasColumnName("fecha_fin")
            .IsRequired();

        builder.Property(x => x.IsOverbook)
            .HasColumnName("is_overbook")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.SourceAppointmentId)
            .HasColumnName("source_appointment_id")
            .IsRequired(false);

        builder.Property(x => x.OverbookStatus)
            .HasColumnName("overbook_status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(OverbookBlockStatus.None)
            .IsRequired();

        builder.Property(x => x.DeactivatedAt)
            .HasColumnName("deactivated_at");

        builder.Property(x => x.DeactivationReason)
            .HasColumnName("deactivation_reason")
            .HasMaxLength(ScheduleBlock.MaxDeactivationReasonLength);

        builder.Property(x => x.OccupiedAppointmentId)
            .HasColumnName("occupied_appointment_id");

        builder.Property(x => x.ConcurrencyStamp)
            .HasColumnName("concurrency_stamp")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // propiedades de regla (opcionales cuando el bloque es de sobrecupo)
        builder.Property(x => x.RuleGroupId)
            .HasColumnName("rule_group_id")
            .IsRequired(false);

        builder.Property(x => x.RuleStartDate)
            .HasColumnName("rule_start_date")
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(x => x.RuleEndDate)
            .HasColumnName("rule_end_date")
            .HasColumnType("date")
            .IsRequired(false);

        builder.Property(x => x.RuleStartTime)
            .HasColumnName("rule_start_time")
            .HasColumnType("time")
            .IsRequired(false);

        builder.Property(x => x.RuleEndTime)
            .HasColumnName("rule_end_time")
            .HasColumnType("time")
            .IsRequired(false);

        builder.Property(x => x.RuleDaysOfWeek)
            .HasColumnName("rule_days_of_week")
            .IsRequired(false);

        // fk
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Box>()
            .WithMany()
            .HasForeignKey(x => x.BoxId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.SourceAppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.BoxId)
            .HasDatabaseName("IX_schedule_blocks_box_id");

        builder.HasIndex(x => x.SourceAppointmentId)
            .HasDatabaseName("IX_schedule_blocks_source_appointment_id")
            .IsUnique()
            .HasFilter("source_appointment_id IS NOT NULL");

        builder.HasIndex(x => new { x.BoxId, x.StartTime, x.EndTime })
            .HasDatabaseName("IX_schedule_blocks_active_overbooks_box_slot")
            .HasFilter("is_overbook = true AND overbook_status IN ('Open', 'Offered', 'Occupied')");
    }
}
