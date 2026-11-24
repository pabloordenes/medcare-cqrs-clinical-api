using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class ReceptionTaskConfiguration : IEntityTypeConfiguration<ReceptionTask>
{
    public void Configure(EntityTypeBuilder<ReceptionTask> builder)
    {
        builder.ToTable("reception_tasks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.AppointmentId)
            .HasColumnName("cita_id")
            .IsRequired();

        builder.Property(x => x.PatientId)
            .HasColumnName("paciente_id")
            .IsRequired();

        builder.Property(x => x.TaskType)
            .HasColumnName("tipo_tarea")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Priority)
            .HasColumnName("prioridad")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("motivo")
            .HasMaxLength(ReceptionTask.MaxReasonLength)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("estado")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.AssignedToUserId)
            .HasColumnName("asignado_a_usuario_id");

        builder.Property(x => x.ResolutionNotes)
            .HasColumnName("notas_resolucion")
            .HasMaxLength(ReceptionTask.MaxResolutionNotesLength);

        builder.Property(x => x.ResolvedByUserId)
            .HasColumnName("resuelto_por_usuario_id");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(x => x.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AssignedToUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ResolvedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.AppointmentId, x.TaskType })
            .HasDatabaseName("UX_reception_tasks_active_appointment_type")
            .IsUnique()
            .HasFilter("estado IN ('Open', 'InProgress')");

        builder.HasIndex(x => new { x.Status, x.Priority, x.CreatedAt })
            .HasDatabaseName("IX_reception_tasks_status_priority_created");
    }
}
