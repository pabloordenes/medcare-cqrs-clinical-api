using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class WaitingListConfiguration : IEntityTypeConfiguration<WaitingList>
{
    public void Configure(EntityTypeBuilder<WaitingList> builder)
    {
        builder.ToTable("lista_espera");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(x => x.PatientId).HasColumnName("paciente_id").IsRequired();

        builder.Property(x => x.Specialty)
            .HasColumnName("especialidad")
            .HasMaxLength(WaitingList.MaxSpecialtyLength)
            .IsRequired();

        builder.Property(x => x.SpecialtyNormalized)
            .HasColumnName("especialidad_normalizada")
            .HasMaxLength(WaitingList.MaxSpecialtyLength)
            .IsRequired();

        builder.Property(x => x.RegistrationDate).HasColumnName("fecha_inscripcion").IsRequired();

        builder.Property(x => x.Priority).HasColumnName("prioridad").IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ClinicalContext)
            .HasColumnName("contexto_clinico")
            .HasColumnType("jsonb");

        builder.Property(x => x.PreferredStaffId).HasColumnName("preferred_staff_id");
        builder.Property(x => x.PreferredFromDate).HasColumnName("preferred_from_date");
        builder.Property(x => x.PreferredToDate).HasColumnName("preferred_to_date");
        builder.Property(x => x.PreferredTimeStart).HasColumnName("preferred_time_start");
        builder.Property(x => x.PreferredTimeEnd).HasColumnName("preferred_time_end");
        builder.Property(x => x.IsDisplacedBonus).HasColumnName("is_displaced_bonus").IsRequired();
        builder.Property(x => x.DisplacedSourceAppointmentId).HasColumnName("displaced_source_appointment_id");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(WaitingList.MaxCancellationReasonLength);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.PreferredStaffId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.DisplacedSourceAppointmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Mantener índice explícito sobre paciente_id para no perder el índice FK al agregar índices parciales
        builder.HasIndex(x => x.PatientId)
            .HasDatabaseName("IX_lista_espera_paciente_id");

        builder.HasIndex(x => new { x.TenantId, x.PatientId, x.SpecialtyNormalized })
            .IsUnique()
            .HasDatabaseName("UX_lista_espera_active_patient_specialty")
            .HasFilter("estado IN ('Active', 'Offered')");

        builder.HasIndex(x => new { x.TenantId, x.SpecialtyNormalized, x.Status })
            .HasDatabaseName("IX_lista_espera_tenant_specialty_status");
    }
}