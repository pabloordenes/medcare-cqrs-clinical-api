using System.Text.Json;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Patients.Commands.RecordPatientOptOut;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Notifications.Commands.ProcessProviderDeliveryStatus;

public class ProcessProviderDeliveryStatusCommandHandler : IRequestHandler<ProcessProviderDeliveryStatusCommand>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly INotificationRepository _notificationRepository;
    private readonly IRiskSignalProcessor _riskSignalProcessor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISender _sender;
    private readonly ILogger<ProcessProviderDeliveryStatusCommandHandler> _logger;

    public ProcessProviderDeliveryStatusCommandHandler(
        IApplicationDbContext dbContext,
        INotificationRepository notificationRepository,
        IRiskSignalProcessor riskSignalProcessor,
        IUnitOfWork unitOfWork,
        ISender sender,
        ILogger<ProcessProviderDeliveryStatusCommandHandler> logger)
    {
        _dbContext = dbContext;
        _notificationRepository = notificationRepository;
        _riskSignalProcessor = riskSignalProcessor;
        _unitOfWork = unitOfWork;
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(ProcessProviderDeliveryStatusCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderMessageId) || string.IsNullOrWhiteSpace(request.EventId))
            return;

        var rawKey = $"webhook:{request.Provider.ToLowerInvariant()}:status:{request.EventId.Trim()}";
        var dedupKey = rawKey.Length <= 200 ? rawKey : rawKey[..200];

        var alreadyProcessed = await _dbContext.ProcessedWebhookKeys
            .AsNoTracking()
            .AnyAsync(k => k.Key == dedupKey, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation("Evento webhook de estado duplicado ignorado: {DedupKey}", dedupKey);
            return;
        }

        var notification = await _notificationRepository.GetByProviderMessageIdAsync(
            request.ProviderMessageId.Trim(),
            cancellationToken);

        if (notification is null)
        {
            _logger.LogWarning(
                "Webhook {Provider} recibido para ProviderMessageId desconocido: {ProviderMessageId}",
                request.Provider,
                request.ProviderMessageId);
        }
        else
        {
            notification.ApplyDeliveryStatus(request.Status, request.EventTimestampUtc, request.Error);

            if (notification.AppointmentId.HasValue && request.Status == NotificationStatus.Failed)
            {
                var errorSummary = string.IsNullOrWhiteSpace(request.Error)
                    ? $"Entrega fallida reportada por {request.Provider}."
                    : request.Error.Trim();

                await _riskSignalProcessor.RecordAsync(
                    new RecordRiskSignalRequest(
                        AppointmentId: notification.AppointmentId.Value,
                        Kind: RiskSignalKind.DeliveryFailed,
                        Source: $"webhook:{request.Provider.ToLowerInvariant()}",
                        IdempotencyKey: $"delivery_failed:{notification.Id}",
                        CorrelationId: notification.CorrelationId,
                        OccurredAtUtc: request.EventTimestampUtc,
                        MetadataJson: JsonSerializer.Serialize(new
                        {
                            notificationId = notification.Id,
                            provider = request.Provider,
                            eventId = request.EventId,
                            channel = notification.Channel.ToString(),
                            error = errorSummary
                        }),
                        CreateFailedDeliveryReceptionTask: true,
                        ReceptionTaskReason: $"Fallo entrega {notification.Channel} vía {request.Provider}: {errorSummary}"),
                    cancellationToken);
            }
        }

        _dbContext.ProcessedWebhookKeys.Add(
            ProcessedWebhookKey.Create(dedupKey, $"/api/webhooks/notifications/{request.Provider.ToLowerInvariant()}"));

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return;
        }

        if (notification is not null && request.TriggerPatientOptOut)
        {
            await _sender.Send(
                new RecordPatientOptOutCommand(
                    PatientId: notification.PatientId,
                    AuthenticatedUserId: null,
                    Source: $"{request.Provider.ToLowerInvariant()}_complaint"),
                cancellationToken);
        }
    }
}
