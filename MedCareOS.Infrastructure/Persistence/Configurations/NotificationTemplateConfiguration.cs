using MedCareOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedCareOS.Infrastructure.Persistence.Configurations;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.TemplateKey)
            .HasColumnName("template_key")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Channel)
            .HasColumnName("channel")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Locale)
            .HasColumnName("locale")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(x => x.SubjectTemplate)
            .HasColumnName("subject_template")
            .HasMaxLength(200);

        builder.Property(x => x.BodyTemplate)
            .HasColumnName("body_template")
            .IsRequired();

        builder.Property(x => x.MetaTemplateName)
            .HasColumnName("meta_template_name")
            .HasMaxLength(120);

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TemplateKey, x.Channel, x.Locale, x.Version })
            .HasDatabaseName("UX_notification_templates_key")
            .IsUnique();
    }
}
