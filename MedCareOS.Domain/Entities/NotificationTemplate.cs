using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class NotificationTemplate
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string TemplateKey { get; private set; } = string.Empty;
    public NotificationChannel Channel { get; private set; }
    public string Locale { get; private set; } = "es-CL";
    public int Version { get; private set; }
    public string? SubjectTemplate { get; private set; }
    public string BodyTemplate { get; private set; } = string.Empty;
    public string? MetaTemplateName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset UpdatedAt { get; private set; }

    private NotificationTemplate() { } // ef

    public static NotificationTemplate Create(
        Guid tenantId,
        string templateKey,
        NotificationChannel channel,
        string locale,
        int version,
        string? subjectTemplate,
        string bodyTemplate,
        string? metaTemplateName = null,
        bool isActive = true)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId es obligatorio.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(templateKey))
            throw new ArgumentException("TemplateKey es obligatorio.", nameof(templateKey));
        if (string.IsNullOrWhiteSpace(locale))
            throw new ArgumentException("Locale es obligatorio.", nameof(locale));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "Version debe ser >= 1.");
        if (string.IsNullOrWhiteSpace(bodyTemplate))
            throw new ArgumentException("BodyTemplate es obligatorio.", nameof(bodyTemplate));

        return new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TemplateKey = templateKey.Trim(),
            Channel = channel,
            Locale = locale.Trim(),
            Version = version,
            SubjectTemplate = string.IsNullOrWhiteSpace(subjectTemplate) ? null : subjectTemplate.Trim(),
            BodyTemplate = bodyTemplate,
            MetaTemplateName = string.IsNullOrWhiteSpace(metaTemplateName) ? null : metaTemplateName.Trim(),
            IsActive = isActive,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
