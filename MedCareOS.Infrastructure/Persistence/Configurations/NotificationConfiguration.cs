using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notificaciones");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasDefaultValue(RiskThresholds.DefaultTenantId)
            .IsRequired();

        builder.Property(x => x.AppointmentId)
            .HasColumnName("cita_id");

        builder.Property(x => x.WaitingListOfferId)
            .HasColumnName("waiting_list_offer_id");

        builder.Property(x => x.PatientId)
            .HasColumnName("paciente_id")
            .IsRequired();

        builder.Property(x => x.Type)
            .HasColumnName("tipo")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Channel)
            .HasColumnName("canal")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.OriginalChannel)
            .HasColumnName("canal_original")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.Status)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.TemplateKey)
            .HasColumnName("template_key")
            .HasMaxLength(Notification.MaxTemplateKeyLength)
            .IsRequired();

        builder.Property(x => x.TemplateVersion)
            .HasColumnName("template_version")
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(x => x.Locale)
            .HasColumnName("locale")
            .HasMaxLength(Notification.MaxLocaleLength)
            .IsRequired();

        builder.Property(x => x.TemplateParamsJson)
            .HasColumnName("template_params_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Content)
            .HasColumnName("contenido");

        builder.Property(x => x.ContentRedactedAt)
            .HasColumnName("content_redacted_at");

        builder.Property(x => x.ScheduledFor)
            .HasColumnName("scheduled_for")
            .IsRequired();

        builder.Property(x => x.SentAt)
            .HasColumnName("sent_at");

        builder.Property(x => x.DeliveredAt)
            .HasColumnName("delivered_at");

        builder.Property(x => x.ReadAt)
            .HasColumnName("read_at");

        builder.Property(x => x.FailedAt)
            .HasColumnName("failed_at");

        builder.Property(x => x.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.ProviderName)
            .HasColumnName("provider_name")
            .HasMaxLength(Notification.MaxProviderNameLength);

        builder.Property(x => x.ProviderMessageId)
            .HasColumnName("provider_message_id")
            .HasMaxLength(Notification.MaxProviderMessageIdLength);

        builder.Property(x => x.Error)
            .HasColumnName("error")
            .HasMaxLength(Notification.MaxErrorLength);

        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("CreatedAt")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WaitingListOffer>()
            .WithMany()
            .HasForeignKey(x => x.WaitingListOfferId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.WaitingListOfferId)
            .HasDatabaseName("IX_notificaciones_waiting_list_offer_id");

        builder.HasIndex(x => new { x.AppointmentId, x.Type, x.ScheduledFor })
            .HasDatabaseName("UX_notificaciones_idempotency")
            .IsUnique()
            .HasFilter("cita_id IS NOT NULL AND estado <> 'Cancelled'");

        builder.HasIndex(x => x.ProviderMessageId)
            .HasDatabaseName("UX_notificaciones_provider_message_id")
            .IsUnique()
            .HasFilter("provider_message_id IS NOT NULL");

        builder.HasIndex(x => new { x.ContentRedactedAt, x.CreatedAt })
            .HasDatabaseName("IX_notificaciones_retention")
            .HasFilter("content_redacted_at IS NULL");

        builder.HasIndex(x => new { x.Status, x.ScheduledFor })
            .HasDatabaseName("IX_notificaciones_scheduled")
            .HasFilter("estado = 'Scheduled'");
    }
}
