using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Resolutions.Services;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CancelAppointment;

public class CancelAppointmentCommandHandler : IRequestHandler<CancelAppointmentCommand>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IAppointmentLifecyclePlanner _lifecyclePlanner;
    private readonly IAiResolutionSupersedingService _resolutionSupersedingService;
    private readonly IUnitOfWork _unitOfWork;

    public CancelAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IAppointmentLifecyclePlanner lifecyclePlanner,
        IAiResolutionSupersedingService resolutionSupersedingService,
        IUnitOfWork unitOfWork)
    {
        _appointmentRepository = appointmentRepository;
        _lifecyclePlanner = lifecyclePlanner;
        _resolutionSupersedingService = resolutionSupersedingService;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(CancelAppointmentCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no se encuentra.");

        appointment.Cancel(request.Reason);
        await _lifecyclePlanner.CancelPendingActionsAsync(appointment.Id, "AppointmentCancelled", cancellationToken: cancellationToken);
        await _resolutionSupersedingService.SupersedePendingForResourceAsync(appointment.Id, "AppointmentCancelled", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
