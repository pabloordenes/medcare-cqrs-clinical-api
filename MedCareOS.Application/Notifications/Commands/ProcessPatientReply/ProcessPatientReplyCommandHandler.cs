using MedCareOS.Application.Appointments.Commands.ConfirmAppointment;
using MedCareOS.Application.Appointments.Commands.DeclineAppointment;
using MedCareOS.Application.Patients.Commands.RecordPatientOptOut;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Notifications.Commands.ProcessPatientReply;

public class ProcessPatientReplyCommandHandler : IRequestHandler<ProcessPatientReplyCommand>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ISender _sender;
    private readonly ILogger<ProcessPatientReplyCommandHandler> _logger;

    public ProcessPatientReplyCommandHandler(
        IAppointmentRepository appointmentRepository,
        ISender sender,
        ILogger<ProcessPatientReplyCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(ProcessPatientReplyCommand request, CancellationToken cancellationToken)
    {
        var sourcePrefix = request.Channel.ToString().ToLowerInvariant();

        if (request.Intent == PatientReplyIntent.OptOut)
        {
            await _sender.Send(
                new RecordPatientOptOutCommand(
                    PatientId: request.PatientId,
                    AuthenticatedUserId: null,
                    Source: $"{sourcePrefix}_reply"),
                cancellationToken);
            return;
        }

        if (request.Intent is not PatientReplyIntent.Confirm and not PatientReplyIntent.Cancel)
            return;

        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"La cita {request.AppointmentId} no existe.");

        if (appointment.PatientId != request.PatientId)
            throw new InvalidOperationException("La cita no pertenece al paciente indicado.");

        try
        {
            if (request.Intent == PatientReplyIntent.Confirm)
            {
                await _sender.Send(
                    new ConfirmAppointmentCommand(
                        AppointmentId: request.AppointmentId,
                        ExpectedPatientId: request.PatientId,
                        Channel: request.Channel,
                        NotificationId: request.NotificationId,
                        Source: $"{sourcePrefix}_reply",
                        NormalizedKeyword: request.NormalizedKeyword,
                        OccurredAtUtc: request.ReceivedAtUtc,
                        PublishPatientReplyEvent: true),
                    cancellationToken);
            }
            else if (request.Intent == PatientReplyIntent.Cancel)
            {
                await _sender.Send(
                    new DeclineAppointmentCommand(
                        AppointmentId: request.AppointmentId,
                        ExpectedPatientId: request.PatientId,
                        Channel: request.Channel,
                        NotificationId: request.NotificationId,
                        Source: $"{sourcePrefix}_reply",
                        Reason: $"Declinada por paciente vía {request.Channel}",
                        NormalizedKeyword: request.NormalizedKeyword,
                        OccurredAtUtc: request.ReceivedAtUtc,
                        PublishPatientReplyEvent: true),
                    cancellationToken);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Acción '{Intent}' recibida para cita {AppointmentId} en estado terminal o no accionable.",
                request.Intent,
                request.AppointmentId);
        }
    }
}
