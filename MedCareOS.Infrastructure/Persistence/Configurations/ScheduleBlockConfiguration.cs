using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class ScheduleBlockConfiguration : IEntityTypeConfiguration<ScheduleBlock>
{
    public void Configure(EntityTypeBuilder<ScheduleBlock> builder)
    {
        builder.ToTable("schedule_blocks");
        
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id");
        
        builder.Property(x => x.DoctorId)
            .HasColumnName("medico_id");
        
        builder.Property(x => x.BoxId)
            .HasColumnName("box_id");
        
        builder.Property(x => x.StartTime)
            .HasColumnName("fecha_inicio");
        
        builder.Property(x => x.EndTime)
            .HasColumnName("fecha_fin");
    }
}