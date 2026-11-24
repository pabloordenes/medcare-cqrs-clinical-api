using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Resolutions.Services;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CheckInAppointment;

public class CheckInAppointmentCommandHandler : IRequestHandler<CheckInAppointmentCommand>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IAppointmentLifecyclePlanner _lifecyclePlanner;
    private readonly IAiResolutionSupersedingService _resolutionSupersedingService;
    private readonly IUnitOfWork _unitOfWork;

    public CheckInAppointmentCommandHandler(
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

    public async Task Handle(CheckInAppointmentCommand request, CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no se encuentra.");

        appointment.CheckIn();
        await _lifecyclePlanner.CancelPendingActionsAsync(appointment.Id, "AppointmentCheckedIn", cancellationToken: cancellationToken);
        await _resolutionSupersedingService.SupersedePendingForResourceAsync(appointment.Id, "AppointmentCheckedIn", cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
