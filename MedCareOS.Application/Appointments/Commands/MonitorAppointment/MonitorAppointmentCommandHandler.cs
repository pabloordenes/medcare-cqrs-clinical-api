using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Application.Appointments.Commands.MonitorAppointment;

public class MonitorAppointmentCommandHandler : IRequestHandler<MonitorAppointmentCommand>
{
    private const string DefaultManualConfirmationReason =
        "Riesgo moderado detectado por IA. Contactar al paciente para re-confirmar asistencia.";

    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IReceptionTaskRepository _receptionTaskRepository;
    private readonly IUnitOfWork _unitOfWork;

    public MonitorAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IReceptionTaskRepository receptionTaskRepository,
        IUnitOfWork unitOfWork)
    {
        _appointmentRepository = appointmentRepository;
        _receptionTaskRepository = receptionTaskRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(MonitorAppointmentCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.ResourceId, cancellationToken);

        if (appointment == null)
            throw new KeyNotFoundException("La cita no existe.");

        if (!request.RequiresAction)
            return;

        var alreadyExists = await _receptionTaskRepository.HasActiveTaskAsync(
            appointment.Id,
            ReceptionTaskType.ManualConfirmation,
            cancellationToken);

        if (alreadyExists)
            return;

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? DefaultManualConfirmationReason
            : request.Reason.Trim();

        var task = ReceptionTask.Create(
            appointmentId: appointment.Id,
            patientId: appointment.PatientId,
            taskType: ReceptionTaskType.ManualConfirmation,
            priority: request.Priority,
            reason: reason);

        await _receptionTaskRepository.AddAsync(task, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await _receptionTaskRepository.HasActiveTaskAsync(
                    appointment.Id,
                    ReceptionTaskType.ManualConfirmation,
                    cancellationToken))
            {
                return;
            }

            throw;
        }
    }
}
