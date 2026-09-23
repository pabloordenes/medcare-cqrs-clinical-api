using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class BoxConfiguration : IEntityTypeConfiguration<Box>
{
    public void Configure(EntityTypeBuilder<Box> builder)
    {
        builder.ToTable("boxes");
        
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        
        builder.Property(x => x.Name).HasColumnName("nombre");
        
        builder.Property(x => x.Type).HasColumnName("tipo");
        
        builder.Property(x => x.Capacity).HasColumnName("capacidad");
        
        builder.Property(x => x.Floor).HasColumnName("piso");
    }
}