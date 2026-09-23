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
        
        builder.Property(x => x.AppointmentId).HasColumnName("cita_id");
        
        builder.Property(x => x.PatientId).HasColumnName("paciente_id");
        
        builder.Property(x => x.Type).HasColumnName("tipo");
        
        builder.Property(x => x.Channel).HasColumnName("canal");
        
        builder.Property(x => x.Content).HasColumnName("contenido");
        
        builder.Property(x => x.Status).HasColumnName("estado");
    }
}