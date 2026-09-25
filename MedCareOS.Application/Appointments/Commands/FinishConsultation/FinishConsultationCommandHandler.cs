using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.FinishConsultation;

public class FinishConsultationCommandHandler : IRequestHandler<FinishConsultationCommand>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public FinishConsultationCommandHandler(IAppointmentRepository appointmentRepository, IUnitOfWork unitOfWork)
    {
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(FinishConsultationCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId);
        
        if (appointment is null)
            throw new Exception("Cita no encontrada."); // TODO: crear excepcion NotFoundException personalizada
        
        appointment.FinishConsultation();
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
