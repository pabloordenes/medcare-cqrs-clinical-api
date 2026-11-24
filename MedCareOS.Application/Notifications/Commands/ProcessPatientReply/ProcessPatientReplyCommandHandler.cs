using MassTransit;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Application.Patients.Commands.RecordPatientOptOut;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Notifications.Commands.ProcessPatientReply;

public class ProcessPatientReplyCommandHandler : IRequestHandler<ProcessPatientReplyCommand>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ISender _sender;
    private readonly ILogger<ProcessPatientReplyCommandHandler> _logger;

    public ProcessPatientReplyCommandHandler(
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint,
        ISender sender,
        ILogger<ProcessPatientReplyCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _publishEndpoint = publishEndpoint;
        _sender = sender;
        _logger = logger;
    }

    public async Task Handle(ProcessPatientReplyCommand request, CancellationToken cancellationToken)
    {
        if (request.Intent == PatientReplyIntent.OptOut)
        {
            await _sender.Send(
                new RecordPatientOptOutCommand(
                    PatientId: request.PatientId,
                    AuthenticatedUserId: null,
                    Source: $"{request.Channel.ToString().ToLowerInvariant()}_reply"),
                cancellationToken);
            return;
        }

        if (request.Intent is not PatientReplyIntent.Confirm and not PatientReplyIntent.Cancel)
            return;

        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"La cita {request.AppointmentId} no existe.");

        if (appointment.PatientId != request.PatientId)
            throw new InvalidOperationException("La cita no pertenece al paciente indicado.");

        if (request.Intent == PatientReplyIntent.Confirm)
        {
            if (appointment.Status == AppointmentStatus.Scheduled)
            {
                appointment.Confirm();
            }
            else if (appointment.Status == AppointmentStatus.Confirmed)
            {
                _logger.LogInformation(
                    "Confirmación repetida ignorada idempotente para cita {AppointmentId}.",
                    appointment.Id);
            }
            else
            {
                _logger.LogWarning(
                    "Confirmación recibida para cita {AppointmentId} en estado no confirmable '{Status}'.",
                    appointment.Id,
                    appointment.Status);
                return;
            }
        }
        else if (request.Intent == PatientReplyIntent.Cancel)
        {
            if (appointment.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed)
            {
                appointment.Cancel($"Cancelada por paciente vía {request.Channel}");
            }
            else if (appointment.Status == AppointmentStatus.Cancelled)
            {
                _logger.LogInformation(
                    "Cancelación repetida ignorada idempotente para cita {AppointmentId}.",
                    appointment.Id);
            }
            else
            {
                _logger.LogWarning(
                    "Cancelación recibida para cita {AppointmentId} en estado no cancelable '{Status}'.",
                    appointment.Id,
                    appointment.Status);
                return;
            }
        }

        await _publishEndpoint.Publish(
            new PatientAppointmentReplyReceived(
                AppointmentId: appointment.Id,
                PatientId: request.PatientId,
                NotificationId: request.NotificationId,
                Intent: request.Intent,
                Channel: request.Channel,
                ReceivedAtUtc: request.ReceivedAtUtc,
                NormalizedKeyword: request.NormalizedKeyword),
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
