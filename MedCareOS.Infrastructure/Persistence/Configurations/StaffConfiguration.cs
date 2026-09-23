using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> builder)
    {
        builder.ToTable("trabajadores");
        
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id");
        
        builder.Property(x => x.UserId)
            .HasColumnName("usuario_id");

        builder.Property(x => x.FirstName)
            .HasColumnName("nombre");
        
        builder.Property(x => x.LastName)
            .HasColumnName("apellido");

        builder.Property(x => x.Rut)
            .HasColumnName("rut");
        builder.HasIndex(x => x.Rut).IsUnique();
        
        builder.Property(x => x.Specialty)
            .HasColumnName("especialidad");
        
        builder.Property(x => x.RoleName)
            .HasColumnName("cargo");
        
        builder.Property(x => x.Phone)
            .HasColumnName("telefono");
        
        builder.Property(x => x.IsActive)
            .HasColumnName("activo");
    }
    
}