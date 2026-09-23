using MedCareOS.Domain.Entities;
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
        
        builder.Property(x => x.PatientId).HasColumnName("paciente_id");
        
        builder.Property(x => x.Specialty).HasColumnName("especialidad");
        
        builder.Property(x => x.RegistrationDate).HasColumnName("fecha_inscripcion");
        
        builder.Property(x => x.Priority).HasColumnName("prioridad");
        
        builder.Property(x => x.Status).HasColumnName("estado");

        builder.Property(x => x.ClinicalContext).HasColumnName("contexto_clinico")
            .HasColumnType("jsonb");
    }
}