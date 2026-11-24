using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class PatientActionTokenConfiguration : IEntityTypeConfiguration<PatientActionToken>
{
    public void Configure(EntityTypeBuilder<PatientActionToken> builder)
    {
        builder.ToTable("patient_action_tokens");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(PatientActionToken.TokenHashLength)
            .IsRequired();

        builder.Property(x => x.AppointmentId)
            .HasColumnName("cita_id")
            .IsRequired();

        builder.Property(x => x.PatientId)
            .HasColumnName("paciente_id")
            .IsRequired();

        builder.Property(x => x.NotificationId)
            .HasColumnName("notificacion_id");

        builder.Property(x => x.ActionScope)
            .HasColumnName("action_scope")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(x => x.UsedAt)
            .HasColumnName("used_at")
            .IsConcurrencyToken();

        builder.Property(x => x.UsedAction)
            .HasColumnName("used_action")
            .HasMaxLength(PatientActionToken.MaxUsedActionLength);

        builder.Property(x => x.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(x => x.NotificationId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.TokenHash)
            .HasDatabaseName("UX_patient_action_tokens_hash")
            .IsUnique();

        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("IX_patient_action_tokens_expires_at");
    }
}
