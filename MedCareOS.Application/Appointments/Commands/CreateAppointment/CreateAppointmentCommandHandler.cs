using MassTransit;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Exceptions;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CreateAppointment;

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Guid>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IPublishEndpoint _publishEndpoint;

    public CreateAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork,
        IScheduleBlockRepository scheduleBlockRepository,
        IStaffRepository staffRepository,
        IPublishEndpoint publishEndpoint)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _scheduleBlockRepository = scheduleBlockRepository;
        _staffRepository = staffRepository;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<Guid> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        Guid effectivePatientId = request.PatientId;
        Patient? patient;

        if (request.AuthenticatedUserId.HasValue)
        {
            patient = await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken);
            if (patient == null)
                throw new InvalidOperationException("El paciente autenticado no tiene un perfil registrado.");

            effectivePatientId = patient.Id;
        }
        else
        {
            patient = await _patientRepository.GetPatientByIdAsync(effectivePatientId, cancellationToken);
            if (patient == null) throw new InvalidOperationException("El paciente no existe.");
        }

        var scheduleBlock = await _scheduleBlockRepository.GetByIdAsync(request.ScheduleBlockId, cancellationToken);
        if (scheduleBlock == null) throw new InvalidOperationException("El bloque horario no existe.");

        var blockOccupied = await _appointmentRepository.HasActiveAppointmentForBlockAsync(request.ScheduleBlockId, cancellationToken);
        if (blockOccupied)
            throw new DuplicateAppointmentException("El bloque horario seleccionado ya se encuentra reservado.");

        var staff = await _staffRepository.GetByIdAsync(scheduleBlock.StaffId, cancellationToken);
        if (staff == null) throw new InvalidOperationException("El staff no existe.");

        var newAppointment = Appointment.Create(
            request.ScheduleBlockId,
            effectivePatientId,
            request.SymptomsRaw,
            request.SchedulingSource);

        // Persistencia atómica: cita + mensaje AppointmentCreated en la misma transacción vía Bus Outbox
        await _appointmentRepository.AddAppointmentAsync(newAppointment, cancellationToken);
        await _publishEndpoint.Publish(
            new AppointmentCreated(
                AppointmentId: newAppointment.Id,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                CorrelationId: Guid.NewGuid()),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return newAppointment.Id;
    }
}
