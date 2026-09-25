using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.StartConsultation;

public class StartConsultationCommandHandler : IRequestHandler<StartConsultationCommand>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public StartConsultationCommandHandler(IAppointmentRepository appointmentRepository, IUnitOfWork unitOfWork)
    {
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(StartConsultationCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken);
        
        if (appointment is null)
            throw new Exception($"La cita con id {request.AppointmentId} no se encuentra.");
        
        appointment.StartConsultation();
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}