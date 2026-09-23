using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("pacientes");
        
        builder.HasKey(p => p.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.UserId)
            .HasColumnName("usuario_id");

        builder.Property(x => x.FirstName)
            .HasColumnName("nombre");
        
        builder.Property(x => x.LastName)
            .HasColumnName("apellido");
        
        builder.Property(x => x.Rut)
            .HasColumnName("rut");
        builder.HasIndex(x => x.Rut).IsUnique(); // rut unico
        
        builder.Property(x => x.DateOfBirth)
            .HasColumnName("fecha_nacimiento");
        
        builder.Property(x => x.Gender)
            .HasColumnName("genero");
        
        builder.Property(x => x.Phone)
            .HasColumnName("telefono");
        
        builder.Property(x => x.Email)
            .HasColumnName("email");
        
        builder.Property(x => x.Address)
            .HasColumnName("direccion");
        
        builder.Property(x => x.Neighbourhood)
            .HasColumnName("barrio");
        
        
    }
}