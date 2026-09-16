using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        // 1. Mapeo a la tabla exacta de la Biblia
        builder.ToTable("citas");

        // 2. Llave Primaria
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        // 3. Foreign Keys (Forzando el snake_case de Supabase)
        builder.Property(x => x.ScheduleBlockId).HasColumnName("schedule_block_id");
        builder.Property(x => x.PatientId).HasColumnName("paciente_id");
        
        builder.Property(x => x.Status)
            .HasColumnName("estado")
            .HasConversion<string>()
            .IsRequired();
        
        builder.Property(x => x.SymptomsRaw)
            .HasColumnName("sintomas_texto");
        
        builder.Property(x => x.ProcessedSymptoms)
            .HasColumnName("sintomas_procesados")
            .HasColumnType("jsonb");
        
        builder.Property(x => x.NlpSummary)
            .HasColumnName("resumen_nlp");
        
        builder.Property(x => x.NoShowRiskScore)
            .HasColumnName("score_riesgo")
            .HasColumnType("numeric(5,4)");

        builder.Property(x => x.RiskBand)
            .HasColumnName("banda_riesgo");

        builder.Property(x => x.SchedulingSource)
            .HasColumnName("fuente_agendamiento");
        
        builder.Property(x => x.ActualAttendance)
            .HasColumnName("asistio");
    }
}