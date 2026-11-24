using System.Text.Json;
using MassTransit;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Domain.Common;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.Notifications.Consumers;

public class SendNotificationConsumer : IConsumer<SendNotificationRequested>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly INotificationChannelResolver _channelResolver;
    private readonly INotificationTemplateRenderer _templateRenderer;
    private readonly IPatientActionTokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _options;
    private readonly ILogger<SendNotificationConsumer> _logger;

    public SendNotificationConsumer(
        INotificationRepository notificationRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        INotificationChannelResolver channelResolver,
        INotificationTemplateRenderer templateRenderer,
        IPatientActionTokenService tokenService,
        IUnitOfWork unitOfWork,
        IOptions<NotificationOptions> options,
        ILogger<SendNotificationConsumer> logger)
    {
        _notificationRepository = notificationRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _channelResolver = channelResolver;
        _templateRenderer = templateRenderer;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendNotificationRequested> context)
    {
        var ct = context.CancellationToken;
        var notificationId = context.Message.NotificationId;
        var utcNow = DateTimeOffset.UtcNow;

        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (notification is null)
        {
            _logger.LogWarning("SendNotificationRequested para notificación inexistente {NotificationId}. Se descarta.", notificationId);
            return;
        }

        if (notification.Status is NotificationStatus.Sent or NotificationStatus.Delivered or NotificationStatus.Read or NotificationStatus.Cancelled)
        {
            _logger.LogInformation("Notificación {NotificationId} ya está en estado '{Status}'. No-op.", notificationId, notification.Status);
            return;
        }

        var patient = await _patientRepository.GetPatientByIdAsync(notification.PatientId, ct);
        if (patient is null)
        {
            notification.RecordAttemptFailure("Paciente asociado no encontrado.", isTerminal: true, utcNow);
            await _unitOfWork.SaveChangesAsync(ct);
            throw new NotificationPermanentDeliveryException($"Paciente {notification.PatientId} no encontrado.");
        }

        if (patient.HasOptedOut())
        {
            notification.Cancel("Paciente registró opt-out antes del despacho.", utcNow);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Notificación {NotificationId} cancelada por opt-out de paciente {PatientId}.", notificationId, patient.Id);
            return;
        }

        if (notification.AppointmentId.HasValue &&
            notification.Type is not NotificationType.OverbookOfferCancelled and not NotificationType.RescheduleHelp)
        {
            var appointment = await _appointmentRepository.GetAppointmentByIdAsync(notification.AppointmentId.Value, ct);
            if (appointment is null || appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow or AppointmentStatus.Finished)
            {
                notification.Cancel("La cita ya no está activa.", utcNow);
                await _unitOfWork.SaveChangesAsync(ct);
                return;
            }
        }

        // Verificar disponibilidad del canal principal antes de enviar; fallback inmediato si WA sin opt-in/teléfono
        if (notification.Channel == NotificationChannel.WhatsApp &&
            (!patient.WhatsAppOptIn || PhoneNormalizer.ToWhatsAppDigits(patient.Phone) is null))
        {
            if (HasValidEmail(patient))
            {
                notification.FallbackToChannel(NotificationChannel.Email, "Paciente sin opt-in o teléfono válido para WhatsApp.", utcNow);
            }
            else
            {
                notification.Cancel("Sin canal de envío válido tras descartar WhatsApp.", utcNow);
                await _unitOfWork.SaveChangesAsync(ct);
                return;
            }
        }

        if (notification.Channel == NotificationChannel.Email && !HasValidEmail(patient))
        {
            notification.RecordAttemptFailure("Paciente sin dirección de email válida.", isTerminal: true, utcNow);
            await _unitOfWork.SaveChangesAsync(ct);
            throw new NotificationPermanentDeliveryException($"Paciente {patient.Id} sin email válido.");
        }

        var parameters = DeserializeParameters(notification.TemplateParamsJson);
        var (actionUrls, rescheduleTokenSuffix) = await BuildActionUrlsIfNeededAsync(notification, patient.Id, ct);

        await DispatchWithFallbackAsync(notification, patient, parameters, actionUrls, rescheduleTokenSuffix, utcNow, ct);
    }

    private async Task DispatchWithFallbackAsync(
        Notification notification,
        Patient patient,
        Dictionary<string, string> parameters,
        Dictionary<string, string>? actionUrls,
        string? rescheduleTokenSuffix,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var recipient = ResolveRecipientAddress(patient, notification.Channel);
        var rendered = await _templateRenderer.RenderAsync(
            new TemplateRenderRequest(
                notification.TenantId,
                notification.AppointmentId,
                notification.Type,
                notification.Channel,
                notification.TemplateKey,
                notification.Locale,
                parameters,
                actionUrls,
                rescheduleTokenSuffix),
            ct);

        var adapter = _channelResolver.Resolve(notification.Channel);
        DeliveryResult result;
        try
        {
            result = await adapter.SendAsync(
                new NotificationMessage(
                    notification.Id,
                    notification.TenantId,
                    notification.AppointmentId,
                    notification.PatientId,
                    notification.Type,
                    notification.Channel,
                    recipient,
                    notification.TemplateKey,
                    notification.TemplateVersion,
                    notification.Locale,
                    parameters,
                    rendered,
                    notification.CorrelationId),
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ClinicalPrivacyViolationException and not NotificationPermanentDeliveryException)
        {
            result = DeliveryResult.Failure(adapter.ProviderName, DeliveryFailureKind.Transient, ex.Message);
        }

        if (result.IsSuccess)
        {
            notification.MarkSent(
                result.ProviderName,
                result.ProviderMessageId ?? notification.Id.ToString("N"),
                rendered.SanitizedContentForStorage,
                DateTimeOffset.UtcNow);
            await _unitOfWork.SaveChangesAsync(ct);
            return;
        }

        var errorText = result.ErrorMessage ?? $"Fallo de envío en proveedor {result.ProviderName} ({result.FailureKind}).";

        // Fallback inmediato WhatsApp -> Email en fallos permanentes / destinatario inválido
        if (notification.Channel == NotificationChannel.WhatsApp &&
            result.ShouldFallbackImmediately &&
            HasValidEmail(patient))
        {
            _logger.LogWarning(
                "Fallback inmediato WhatsApp -> Email para notificación {NotificationId}: {Error}",
                notification.Id,
                errorText);

            notification.FallbackToChannel(NotificationChannel.Email, errorText, DateTimeOffset.UtcNow);
            await DispatchWithFallbackAsync(notification, patient, parameters, actionUrls, rescheduleTokenSuffix, DateTimeOffset.UtcNow, ct);
            return;
        }

        // Fallo transitorio o RateLimited
        if (result.FailureKind is DeliveryFailureKind.Transient or DeliveryFailureKind.RateLimited)
        {
            var maxPrimaryAttempts = Math.Max(1, _options.MaxPrimaryChannelAttempts);
            notification.RecordAttemptFailure(errorText, isTerminal: false, DateTimeOffset.UtcNow);

            if (notification.Channel == NotificationChannel.WhatsApp &&
                notification.Attempts >= maxPrimaryAttempts &&
                HasValidEmail(patient))
            {
                _logger.LogWarning(
                    "Agotados {Attempts} intentos WhatsApp para {NotificationId}. Se cambia canal a Email para el próximo reintento.",
                    notification.Attempts,
                    notification.Id);
                notification.FallbackToChannel(NotificationChannel.Email, errorText, DateTimeOffset.UtcNow);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            throw new NotificationTransientDeliveryException(errorText, result.HttpStatusCode, result.ErrorCode);
        }

        // Fallo permanente sin fallback disponible
        notification.RecordAttemptFailure(errorText, isTerminal: true, DateTimeOffset.UtcNow);
        await _unitOfWork.SaveChangesAsync(ct);
        throw new NotificationPermanentDeliveryException(errorText, result.HttpStatusCode, result.ErrorCode);
    }

    private async Task<(Dictionary<string, string>? ActionUrls, string? RescheduleTokenSuffix)> BuildActionUrlsIfNeededAsync(
        Notification notification,
        Guid patientId,
        CancellationToken ct)
    {
        if (!notification.AppointmentId.HasValue)
            return (null, null);

        PatientActionScope? scope = notification.Type switch
        {
            NotificationType.Reminder or NotificationType.FinalConfirmation or NotificationType.OverbookNotice or NotificationType.WaitingListOffer
                => PatientActionScope.ConfirmOrCancel,
            NotificationType.RescheduleHelp => PatientActionScope.Reschedule,
            _ => null
        };

        if (!scope.HasValue)
            return (null, null);

        var ttlHours = Math.Max(1, _options.ActionTokenDefaultTtlHours);
        var issued = await _tokenService.IssueAsync(
            notification.AppointmentId.Value,
            patientId,
            notification.Id,
            scope.Value,
            TimeSpan.FromHours(ttlHours),
            ct);

        var baseUrl = string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? "https://app.medcare.cl"
            : _options.PublicBaseUrl.TrimEnd('/');
        var encodedToken = Uri.EscapeDataString(issued.RawToken);

        if (scope.Value == PatientActionScope.Reschedule)
        {
            var suffix = $"?token={encodedToken}";
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["rescheduleUrl"] = $"{baseUrl}/appointments/reschedule{suffix}"
            }, suffix);
        }

        return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["confirmUrl"] = $"{baseUrl}/appointments/respond?token={encodedToken}&action=confirm",
            ["cancelUrl"] = $"{baseUrl}/appointments/respond?token={encodedToken}&action=cancel"
        }, null);
    }

    private static string ResolveRecipientAddress(Patient patient, NotificationChannel channel) => channel switch
    {
        NotificationChannel.WhatsApp => PhoneNormalizer.ToWhatsAppDigits(patient.Phone)
            ?? throw new NotificationPermanentDeliveryException($"Teléfono inválido para WhatsApp en paciente {patient.Id}."),
        NotificationChannel.Email => !string.IsNullOrWhiteSpace(patient.Email)
            ? patient.Email.Trim()
            : throw new NotificationPermanentDeliveryException($"Email faltante en paciente {patient.Id}."),
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    private static bool HasValidEmail(Patient patient) =>
        !string.IsNullOrWhiteSpace(patient.Email) && patient.Email.Contains('@');

    private static Dictionary<string, string> DeserializeParameters(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return parsed is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
    }
}
