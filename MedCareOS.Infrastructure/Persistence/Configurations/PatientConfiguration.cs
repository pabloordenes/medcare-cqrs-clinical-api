using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
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
            .HasColumnName("fecha_nacimiento")
            .HasColumnType("date");

        builder.Property(x => x.Gender)
            .HasColumnName("genero");

        builder.Property(x => x.Phone)
            .HasColumnName("telefono");
        builder.HasIndex(x => x.Phone)
            .HasDatabaseName("IX_pacientes_telefono");

        builder.Property(x => x.Email)
            .HasColumnName("email");

        builder.Property(x => x.Address)
            .HasColumnName("direccion");

        builder.Property(x => x.Neighbourhood)
            .HasColumnName("barrio");

        builder.Property(x => x.PreferredChannel)
            .HasColumnName("canal_preferido")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(NotificationChannel.Email)
            .HasSentinel((NotificationChannel)(-1))
            .IsRequired();

        builder.Property(x => x.WhatsAppOptIn)
            .HasColumnName("whatsapp_opt_in")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.WhatsAppOptInAt)
            .HasColumnName("whatsapp_opt_in_at");

        builder.Property(x => x.WhatsAppOptInSource)
            .HasColumnName("whatsapp_opt_in_source")
            .HasMaxLength(Patient.MaxConsentSourceLength);

        builder.Property(x => x.OptOutAt)
            .HasColumnName("opt_out_at");

        builder.Property(x => x.OptOutSource)
            .HasColumnName("opt_out_source")
            .HasMaxLength(Patient.MaxConsentSourceLength);

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        // fk
        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<Patient>(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
