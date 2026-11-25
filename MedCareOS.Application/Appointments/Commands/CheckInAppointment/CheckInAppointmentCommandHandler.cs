using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Overbooking;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.Resolutions.Services;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.Appointments.Commands.CheckInAppointment;

public class CheckInAppointmentCommandHandler : IRequestHandler<CheckInAppointmentCommand>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IAppointmentLifecyclePlanner _lifecyclePlanner;
    private readonly IAiResolutionSupersedingService _resolutionSupersedingService;
    private readonly IOverbookCollisionResolutionService _collisionResolutionService;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IUnitOfWork _unitOfWork;

    public CheckInAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IAppointmentLifecyclePlanner lifecyclePlanner,
        IAiResolutionSupersedingService resolutionSupersedingService,
        IOverbookCollisionResolutionService collisionResolutionService,
        ITransactionalExecutor transactionalExecutor,
        IUnitOfWork unitOfWork)
    {
        _appointmentRepository = appointmentRepository;
        _lifecyclePlanner = lifecyclePlanner;
        _resolutionSupersedingService = resolutionSupersedingService;
        _collisionResolutionService = collisionResolutionService;
        _transactionalExecutor = transactionalExecutor;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(CheckInAppointmentCommand request, CancellationToken cancellationToken)
    {
        await _transactionalExecutor.ExecuteAsync(
            $"checkin:{request.AppointmentId:N}",
            async ct =>
            {
                var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, ct)
                    ?? throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no se encuentra.");

                var utcNow = DateTimeOffset.UtcNow;
                appointment.CheckIn(utcNow);
                await _lifecyclePlanner.CancelPendingActionsAsync(appointment.Id, "AppointmentCheckedIn", utcNow, ct);
                await _resolutionSupersedingService.SupersedePendingForResourceAsync(appointment.Id, "AppointmentCheckedIn", ct);
                await _collisionResolutionService.EvaluateDoubleCheckInAsync(appointment, utcNow, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            },
            cancellationToken);
    }
}
