using System.Text.Json;
using MassTransit;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Notifications.Services;

public class NotificationOutboxService : INotificationOutboxService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<NotificationOutboxService> _logger;

    public NotificationOutboxService(
        INotificationRepository notificationRepository,
        IPublishEndpoint publishEndpoint,
        ILogger<NotificationOutboxService> logger)
    {
        _notificationRepository = notificationRepository;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task<NotificationEnqueueResult> EnqueueAsync(
        EnqueueNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Patient.HasOptedOut())
        {
            _logger.LogInformation(
                "Notificación {Type} omitida para paciente {PatientId} por opt-out activo.",
                request.Type,
                request.Patient.Id);
            return new NotificationEnqueueResult(NotificationEnqueueOutcome.SkippedOptOut);
        }

        var effectiveChannel = request.ChannelOverride.HasValue
            ? (request.Patient.CanReceiveChannel(request.ChannelOverride.Value)
                ? request.ChannelOverride
                : request.Patient.GetEffectiveReminderChannel())
            : request.Patient.GetEffectiveReminderChannel();
        if (!effectiveChannel.HasValue)
        {
            _logger.LogInformation(
                "Notificación {Type} omitida para paciente {PatientId}: sin canal de contacto habilitado.",
                request.Type,
                request.Patient.Id);
            return new NotificationEnqueueResult(NotificationEnqueueOutcome.SkippedNoChannel);
        }

        if (request.AppointmentId.HasValue)
        {
            var duplicate = await _notificationRepository.ExistsByIdempotencyKeyAsync(
                request.AppointmentId.Value,
                request.Type,
                request.ScheduledFor,
                cancellationToken);

            if (duplicate)
            {
                _logger.LogInformation(
                    "Notificación duplicada omitida por idempotencia ({AppointmentId}, {Type}, {ScheduledFor}).",
                    request.AppointmentId.Value,
                    request.Type,
                    request.ScheduledFor);
                return new NotificationEnqueueResult(NotificationEnqueueOutcome.DuplicateSkipped);
            }
        }

        var utcNow = DateTimeOffset.UtcNow;
        var paramsJson = JsonSerializer.Serialize(request.Parameters);
        var locale = string.IsNullOrWhiteSpace(request.Locale) ? "es-CL" : request.Locale.Trim();

        var notification = Notification.Create(
            tenantId: request.TenantId,
            appointmentId: request.AppointmentId,
            patientId: request.Patient.Id,
            type: request.Type,
            channel: effectiveChannel.Value,
            templateKey: request.TemplateKey,
            templateVersion: 1,
            locale: locale,
            templateParamsJson: paramsJson,
            scheduledFor: request.ScheduledFor,
            correlationId: request.CorrelationId,
            utcNow: utcNow);

        await _notificationRepository.AddAsync(notification, cancellationToken);

        if (request.ForceImmediateDispatch || request.ScheduledFor <= utcNow)
        {
            await _publishEndpoint.Publish(
                new SendNotificationRequested(
                    NotificationId: notification.Id,
                    CorrelationId: request.CorrelationId,
                    OccurredAtUtc: utcNow),
                cancellationToken);

            return new NotificationEnqueueResult(
                NotificationEnqueueOutcome.Enqueued,
                notification.Id,
                effectiveChannel.Value);
        }

        return new NotificationEnqueueResult(
            NotificationEnqueueOutcome.ScheduledWithoutImmediateDispatch,
            notification.Id,
            effectiveChannel.Value);
    }
}
