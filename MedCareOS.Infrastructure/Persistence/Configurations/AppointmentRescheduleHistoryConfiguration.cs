using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class AppointmentRescheduleHistoryConfiguration : IEntityTypeConfiguration<AppointmentRescheduleHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentRescheduleHistory> builder)
    {
        builder.ToTable("appointment_reschedule_history");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.AppointmentId).HasColumnName("cita_id").IsRequired();
        builder.Property(x => x.PatientId).HasColumnName("paciente_id").IsRequired();
        builder.Property(x => x.FromScheduleBlockId).HasColumnName("from_schedule_block_id").IsRequired();
        builder.Property(x => x.ToScheduleBlockId).HasColumnName("to_schedule_block_id").IsRequired();
        builder.Property(x => x.TriggeredByUserId).HasColumnName("triggered_by_user_id");

        builder.Property(x => x.Channel)
            .HasColumnName("channel")
            .HasMaxLength(AppointmentRescheduleHistory.MaxChannelLength)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasMaxLength(AppointmentRescheduleHistory.MaxReasonLength);

        builder.Property(x => x.WasDisplacedPriority)
            .HasColumnName("was_displaced_priority")
            .IsRequired();

        builder.Property(x => x.RescheduleNumber)
            .HasColumnName("reschedule_number")
            .IsRequired();

        builder.Property(x => x.RescheduledAtUtc)
            .HasColumnName("rescheduled_at_utc")
            .IsRequired();

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ScheduleBlock>()
            .WithMany()
            .HasForeignKey(x => x.FromScheduleBlockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ScheduleBlock>()
            .WithMany()
            .HasForeignKey(x => x.ToScheduleBlockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.AppointmentId)
            .HasDatabaseName("IX_appointment_reschedule_history_appointment_id");
    }
}
