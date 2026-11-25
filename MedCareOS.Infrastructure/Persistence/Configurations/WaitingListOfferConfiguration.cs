using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class WaitingListOfferConfiguration : IEntityTypeConfiguration<WaitingListOffer>
{
    public void Configure(EntityTypeBuilder<WaitingListOffer> builder)
    {
        builder.ToTable("waiting_list_offers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.WaitingListId).HasColumnName("waiting_list_id").IsRequired();
        builder.Property(x => x.PatientId).HasColumnName("paciente_id").IsRequired();
        builder.Property(x => x.ScheduleBlockId).HasColumnName("schedule_block_id").IsRequired();
        builder.Property(x => x.SourceAppointmentId).HasColumnName("source_appointment_id");
        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasMaxLength(WaitingListOffer.MaxSourceTypeLength)
            .IsRequired();
        builder.Property(x => x.CascadeAttemptNumber).HasColumnName("cascade_attempt_number").IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.OfferedAt).HasColumnName("offered_at").IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.RespondedAt).HasColumnName("responded_at");
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
        builder.Property(x => x.ResultAppointmentId).HasColumnName("result_appointment_id");
        builder.Property(x => x.NotificationId).HasColumnName("notification_id");
        builder.Property(x => x.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(WaitingListOffer.MaxCancellationReasonLength);

        builder.Property(x => x.ConcurrencyStamp)
            .HasColumnName("concurrency_stamp")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne<WaitingList>()
            .WithMany()
            .HasForeignKey(x => x.WaitingListId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ScheduleBlock>()
            .WithMany()
            .HasForeignKey(x => x.ScheduleBlockId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.SourceAppointmentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.ResultAppointmentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.WaitingListId)
            .HasDatabaseName("IX_waiting_list_offers_waiting_list_id");

        builder.HasIndex(x => x.PatientId)
            .HasDatabaseName("IX_waiting_list_offers_paciente_id");

        // Mantener índice FK B-Tree explícito además del índice único parcial
        builder.HasIndex(x => x.ScheduleBlockId)
            .HasDatabaseName("IX_waiting_list_offers_schedule_block_id");

        builder.HasIndex(x => x.ScheduleBlockId)
            .IsUnique()
            .HasDatabaseName("UX_waiting_list_offers_active_block")
            .HasFilter("status = 'Pending'");

        builder.HasIndex(x => new { x.Status, x.ExpiresAt })
            .HasDatabaseName("IX_waiting_list_offers_pending_expires_at")
            .HasFilter("status = 'Pending'");
    }
}
