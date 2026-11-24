using MedCareOS.Application.Interfaces;
using MedCareOS.Application.Notifications.Commands.ProcessPatientReply;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Patients.Commands.RecordPatientOptOut;
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
    private readonly ISender _sender;
    private readonly ILogger<ProcessWhatsAppInboundMessageCommandHandler> _logger;

    public ProcessWhatsAppInboundMessageCommandHandler(
        IApplicationDbContext dbContext,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        INotificationRepository notificationRepository,
        ISender sender,
        ILogger<ProcessWhatsAppInboundMessageCommandHandler> logger)
    {
        _dbContext = dbContext;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _notificationRepository = notificationRepository;
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
            _logger.LogInformation(
                "Respuesta WhatsApp no reconocida para paciente {PatientId}. Texto normalizado={Keyword}",
                patient.Id,
                parsed.NormalizedKeyword);
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
        Guid? sourceNotificationId = null;

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
        else
        {
            var latestNotification = await _notificationRepository.GetLatestActionableByPatientAsync(patient.Id, cancellationToken);
            targetAppointmentId = latestNotification?.AppointmentId;
            sourceNotificationId = latestNotification?.Id;
        }

        if (!targetAppointmentId.HasValue)
        {
            _logger.LogInformation(
                "Respuesta '{Intent}' de paciente {PatientId} sin cita accionable reciente asociada.",
                parsed.Intent,
                patient.Id);
            return;
        }

        await _sender.Send(
            new ProcessPatientReplyCommand(
                AppointmentId: targetAppointmentId.Value,
                PatientId: patient.Id,
                NotificationId: sourceNotificationId,
                Intent: parsed.Intent,
                Channel: NotificationChannel.WhatsApp,
                NormalizedKeyword: parsed.NormalizedKeyword,
                ReceivedAtUtc: request.ReceivedAtUtc),
            cancellationToken);
    }
}
