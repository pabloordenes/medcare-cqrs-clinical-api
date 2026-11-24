using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Interfaces;
using MedCareOS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Notifications.Templates;

public partial class NotificationTemplateRenderer : INotificationTemplateRenderer
{
    private const string RedactedActionLinkPlaceholder = "[ACTION_LINK_REDACTED]";

    private static readonly HashSet<string> ForbiddenClinicalParameterKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "symptoms",
        "symptomsraw",
        "sintomas",
        "sintomas_texto",
        "processedsymptoms",
        "sintomas_procesados",
        "nlpsummary",
        "resumen_nlp",
        "diagnosis",
        "diagnostico",
        "clinicalcontext",
        "contexto_clinico",
        "motivo_clinico",
        "enfermedad",
        "patologia",
        "rut"
    };

    private readonly IApplicationDbContext _dbContext;

    public NotificationTemplateRenderer(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RenderedNotification> RenderAsync(
        TemplateRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateNoClinicalParameters(request.Parameters);
        if (request.ActionUrls is not null)
            ValidateNoClinicalParameters(request.ActionUrls);

        var defaultDef = DefaultNotificationTemplateCatalog.Resolve(
            request.TemplateKey,
            request.Channel,
            request.Locale);

        var localeToSearch = string.IsNullOrWhiteSpace(request.Locale)
            ? DefaultNotificationTemplateCatalog.DefaultLocale
            : request.Locale.Trim();

        var customTemplate = await _dbContext.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.TenantId == request.TenantId
                        && t.TemplateKey == request.TemplateKey
                        && t.Channel == request.Channel
                        && t.Locale == localeToSearch
                        && t.IsActive)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);

        var subjectTemplate = customTemplate?.SubjectTemplate ?? defaultDef.SubjectTemplate;
        var bodyTemplate = customTemplate?.BodyTemplate ?? defaultDef.BodyTemplate;
        var metaTemplateName = customTemplate?.MetaTemplateName ?? defaultDef.MetaTemplateName;

        var fullParams = new Dictionary<string, string>(request.Parameters, StringComparer.OrdinalIgnoreCase);
        var sanitizedParams = new Dictionary<string, string>(request.Parameters, StringComparer.OrdinalIgnoreCase);

        if (request.ActionUrls is not null)
        {
            foreach (var kvp in request.ActionUrls)
            {
                fullParams[kvp.Key] = kvp.Value;
                sanitizedParams[kvp.Key] = RedactedActionLinkPlaceholder;
            }
        }

        string? renderedSubject = subjectTemplate is null
            ? null
            : RenderPlaceholders(subjectTemplate, fullParams, request.TemplateKey);

        string plainTextBody = RenderPlaceholders(bodyTemplate, fullParams, request.TemplateKey);
        string sanitizedStorageBody = RenderPlaceholders(bodyTemplate, sanitizedParams, request.TemplateKey);

        string? htmlBody = request.Channel == NotificationChannel.Email
            ? BuildHtmlBody(renderedSubject, plainTextBody)
            : null;

        var metaOrderedParams = new List<string>(defaultDef.MetaOrderedParameterKeys.Count);
        foreach (var paramKey in defaultDef.MetaOrderedParameterKeys)
        {
            if (!fullParams.TryGetValue(paramKey, out var val))
            {
                throw new InvalidOperationException(
                    $"Falta el parámetro obligatorio '{paramKey}' para la plantilla Meta '{metaTemplateName}'.");
            }
            metaOrderedParams.Add(val);
        }

        var buttons = defaultDef.BuildButtons(request.AppointmentId, request.RescheduleTokenSuffix);

        return new RenderedNotification(
            Subject: renderedSubject,
            PlainTextBody: plainTextBody,
            HtmlBody: htmlBody,
            SanitizedContentForStorage: sanitizedStorageBody,
            MetaTemplateName: metaTemplateName,
            MetaOrderedBodyParameters: metaOrderedParams,
            Buttons: buttons);
    }

    private static void ValidateNoClinicalParameters(IReadOnlyDictionary<string, string> parameters)
    {
        foreach (var key in parameters.Keys)
        {
            var normalized = key.Trim();
            if (ForbiddenClinicalParameterKeys.Contains(normalized))
            {
                throw new ClinicalPrivacyViolationException(
                    $"El parámetro '{key}' está prohibido en notificaciones al paciente por privacidad clínica.");
            }
        }
    }

    private static string RenderPlaceholders(
        string template,
        IReadOnlyDictionary<string, string> parameters,
        string templateKey)
    {
        var rendered = PlaceholderRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (ForbiddenClinicalParameterKeys.Contains(key))
            {
                throw new ClinicalPrivacyViolationException(
                    $"La plantilla '{templateKey}' referencia el marcador clínico prohibido '{{{{{key}}}}}'.");
            }

            if (!parameters.TryGetValue(key, out var value))
            {
                throw new InvalidOperationException(
                    $"Falta el parámetro '{{{{{key}}}}}' al renderizar la plantilla '{templateKey}'.");
            }

            return value;
        });

        return rendered;
    }

    private static string BuildHtmlBody(string? subject, string plainTextBody)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"es-CL\"><body style=\"font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.5;\">");
        if (!string.IsNullOrWhiteSpace(subject))
        {
            sb.Append("<h2 style=\"color:#0f172a;font-size:18px;margin-bottom:12px;\">")
              .Append(WebUtility.HtmlEncode(subject))
              .Append("</h2>");
        }

        var lines = plainTextBody.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.Append("<br/>");
                continue;
            }

            sb.Append("<p style=\"margin:0 0 8px 0;\">")
              .Append(WebUtility.HtmlEncode(line))
              .Append("</p>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
