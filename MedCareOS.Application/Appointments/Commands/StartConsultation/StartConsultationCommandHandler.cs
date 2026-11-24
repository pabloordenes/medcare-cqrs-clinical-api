using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.StartConsultation;

public class StartConsultationCommandHandler : IRequestHandler<StartConsultationCommand>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IAppointmentLifecyclePlanner _lifecyclePlanner;
    private readonly IUnitOfWork _unitOfWork;

    public StartConsultationCommandHandler(
        IAppointmentRepository appointmentRepository,
        IAppointmentLifecyclePlanner lifecyclePlanner,
        IUnitOfWork unitOfWork)
    {
        _appointmentRepository = appointmentRepository;
        _lifecyclePlanner = lifecyclePlanner;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(StartConsultationCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken);
        
        if (appointment is null)
            throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no se encuentra.");
        
        appointment.StartConsultation();
        await _lifecyclePlanner.CancelPendingActionsAsync(appointment.Id, "ConsultationStarted", cancellationToken: cancellationToken);
        
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}