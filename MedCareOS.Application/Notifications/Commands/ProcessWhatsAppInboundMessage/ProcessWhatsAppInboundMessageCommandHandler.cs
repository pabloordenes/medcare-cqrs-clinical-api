using System.Text.Json;
using MedCareOS.Application.Appointments.Commands.ConfirmAppointment;
using MedCareOS.Application.Appointments.Commands.DeclineAppointment;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Patients.Commands.RecordPatientOptOut;
using MedCareOS.Application.WaitingLists.Commands.AcceptWaitingListOffer;
using MedCareOS.Application.WaitingLists.Commands.DeclineWaitingListOffer;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Notifications.Commands.ProcessWhatsAppInboundMessage;

public class ProcessWhatsAppInboundMessageCommandHandler : IRequestHandler<ProcessWhatsAppInboundMessageCommand>
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IRiskSignalProcessor _riskSignalProcessor;
    private readonly ISender _sender;
    private readonly ILogger<ProcessWhatsAppInboundMessageCommandHandler> _logger;

    public ProcessWhatsAppInboundMessageCommandHandler(
        IApplicationDbContext dbContext,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        INotificationRepository notificationRepository,
        IRiskSignalProcessor riskSignalProcessor,
        ISender sender,
        ILogger<ProcessWhatsAppInboundMessageCommandHandler> logger)
    {
        _dbContext = dbContext;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _notificationRepository = notificationRepository;
        _riskSignalProcessor = riskSignalProcessor;
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(ProcessWhatsAppInboundMessageCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InboundMessageId) || string.IsNullOrWhiteSpace(request.FromPhone))
            return;

        var rawKey = $"webhook:whatsapp:msg:{request.InboundMessageId.Trim()}";
        var dedupKey = rawKey.Length <= 200 ? rawKey : rawKey[..200];

        var alreadyProcessed = await _dbContext.ProcessedWebhookKeys
            .AsNoTracking()
            .AnyAsync(k => k.Key == dedupKey, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation("Mensaje entrante WhatsApp duplicado ignorado: {DedupKey}", dedupKey);
            return;
        }

        _dbContext.ProcessedWebhookKeys.Add(
            ProcessedWebhookKey.Create(dedupKey, "/api/webhooks/notifications/whatsapp"));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return;
        }

        var patient = await _patientRepository.GetByNormalizedPhoneAsync(request.FromPhone, cancellationToken);
        if (patient is null)
        {
            _logger.LogWarning(
                "Mensaje entrante WhatsApp desde número no registrado: {FromPhone} (MsgId={MessageId})",
                request.FromPhone,
                request.InboundMessageId);
            return;
        }

        var parsed = PatientReplyKeywordParser.Parse(request.ButtonPayload, request.TextBody);
        if (parsed.Intent == PatientReplyIntent.Unknown)
        {
            var latestForInquiry = await _notificationRepository.GetLatestActionableByPatientAsync(patient.Id, cancellationToken);
            if (latestForInquiry?.AppointmentId is Guid inquiryAppointmentId)
            {
                await _riskSignalProcessor.RecordAsync(
                    new RecordRiskSignalRequest(
                        AppointmentId: inquiryAppointmentId,
                        Kind: RiskSignalKind.RepliedWithoutConfirmation,
                        Source: "whatsapp_inbound",
                        IdempotencyKey: $"replied_neutral:{request.InboundMessageId.Trim()}",
                        CorrelationId: latestForInquiry.CorrelationId,
                        OccurredAtUtc: request.ReceivedAtUtc,
                        MetadataJson: JsonSerializer.Serialize(new
                        {
                            channel = NotificationChannel.WhatsApp.ToString(),
                            inboundMessageId = request.InboundMessageId.Trim(),
                            notificationId = latestForInquiry.Id
                        })),
                    cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Respuesta WhatsApp no reconocida para paciente {PatientId}. Señal neutra registrada si había cita accionable.",
                patient.Id);
            return;
        }

        if (parsed.Intent == PatientReplyIntent.OptOut)
        {
            await _sender.Send(
                new RecordPatientOptOutCommand(
                    PatientId: patient.Id,
                    AuthenticatedUserId: null,
                    Source: "whatsapp_stop"),
                cancellationToken);
            return;
        }

        Guid? targetAppointmentId = parsed.AppointmentId;
        Guid? targetOfferId = parsed.WaitingListOfferId;
        Guid? sourceNotificationId = null;
        Guid? sourceCorrelationId = null;

        if (targetAppointmentId.HasValue)
        {
            var appointment = await _appointmentRepository.GetAppointmentByIdAsync(targetAppointmentId.Value, cancellationToken);
            if (appointment is null || appointment.PatientId != patient.Id)
            {
                _logger.LogWarning(
                    "Payload WhatsApp con AppointmentId {AppointmentId} no pertenece al paciente {PatientId}.",
                    targetAppointmentId,
                    patient.Id);
                return;
            }
        }
        else if (!targetOfferId.HasValue)
        {
            var latestNotification = await _notificationRepository.GetLatestActionableByPatientAsync(patient.Id, cancellationToken);
            targetAppointmentId = latestNotification?.AppointmentId;
            if (!targetAppointmentId.HasValue && latestNotification?.Type == NotificationType.WaitingListOffer)
            {
                targetOfferId = latestNotification.WaitingListOfferId;
            }
            sourceNotificationId = latestNotification?.Id;
            sourceCorrelationId = latestNotification?.CorrelationId;
        }

        if (targetOfferId.HasValue)
        {
            try
            {
                if (parsed.Intent == PatientReplyIntent.Confirm)
                {
                    await _sender.Send(
                        new AcceptWaitingListOfferCommand(
                            OfferId: targetOfferId.Value,
                            RawToken: null,
                            AuthenticatedUserId: null,
                            ExpectedPatientId: patient.Id,
                            Channel: "whatsapp"),
                        cancellationToken);
                }
                else if (parsed.Intent == PatientReplyIntent.Cancel)
                {
                    await _sender.Send(
                        new DeclineWaitingListOfferCommand(
                            OfferId: targetOfferId.Value,
                            RawToken: null,
                            AuthenticatedUserId: null,
                            ExpectedPatientId: patient.Id,
                            Reason: "Rechazada por candidato vía WhatsApp."),
                        cancellationToken);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    ex,
                    "Respuesta WhatsApp '{Intent}' ignorada para oferta {OfferId}.",
                    parsed.Intent,
                    targetOfferId.Value);
            }
            return;
        }

        if (!targetAppointmentId.HasValue)
        {
            _logger.LogInformation(
                "Respuesta '{Intent}' de paciente {PatientId} sin cita ni oferta accionable reciente asociada.",
                parsed.Intent,
                patient.Id);
            return;
        }

        try
        {
            if (parsed.Intent == PatientReplyIntent.Confirm)
            {
                await _sender.Send(
                    new ConfirmAppointmentCommand(
                        AppointmentId: targetAppointmentId.Value,
                        ExpectedPatientId: patient.Id,
                        Channel: NotificationChannel.WhatsApp,
                        NotificationId: sourceNotificationId,
                        Source: "whatsapp_inbound",
                        NormalizedKeyword: parsed.NormalizedKeyword,
                        OccurredAtUtc: request.ReceivedAtUtc,
                        CorrelationId: sourceCorrelationId,
                        PublishPatientReplyEvent: true),
                    cancellationToken);
            }
            else if (parsed.Intent == PatientReplyIntent.Cancel)
            {
                await _sender.Send(
                    new DeclineAppointmentCommand(
                        AppointmentId: targetAppointmentId.Value,
                        ExpectedPatientId: patient.Id,
                        Channel: NotificationChannel.WhatsApp,
                        NotificationId: sourceNotificationId,
                        Source: "whatsapp_inbound",
                        Reason: "Declinada por paciente vía WhatsApp.",
                        NormalizedKeyword: parsed.NormalizedKeyword,
                        OccurredAtUtc: request.ReceivedAtUtc,
                        CorrelationId: sourceCorrelationId,
                        PublishPatientReplyEvent: true),
                    cancellationToken);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Respuesta WhatsApp '{Intent}' ignorada para cita {AppointmentId} en estado terminal o inválido.",
                parsed.Intent,
                targetAppointmentId.Value);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "Respuesta WhatsApp '{Intent}' ignorada porque la cita {AppointmentId} no existe.",
                parsed.Intent,
                targetAppointmentId.Value);
        }
    }
}
