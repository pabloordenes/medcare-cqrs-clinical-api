using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CreateAppointment;

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Guid>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateAppointmentCommandHandler(IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetPatientByIdAsync(request.PatientId, cancellationToken);
        
        if (patient == null)
            throw new Exception("El paciente no existe."); // futura excepcion personalizada

        var newAppointment = Appointment.Create(
            request.ScheduleBlockId,
            request.PatientId,
            request.SymptomsRaw,
            request.SchedulingSource);

        await _appointmentRepository.AddAppointmentAsync(newAppointment, cancellationToken);
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return newAppointment.Id;
    }
}