using MassTransit;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Services;
using MedCareOS.Application.Common.Persistence;
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
    private readonly IPatientRepository? _patientRepository;
    private readonly IScheduleBlockRepository? _scheduleBlockRepository;
    private readonly ISlotReleaseService? _slotReleaseService;
    private readonly IPublishEndpoint? _publishEndpoint;
    private readonly ITransactionalExecutor? _transactionalExecutor;

    public CancelAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IAppointmentLifecyclePlanner lifecyclePlanner,
        IAiResolutionSupersedingService resolutionSupersedingService,
        IUnitOfWork unitOfWork,
        IPatientRepository? patientRepository = null,
        IScheduleBlockRepository? scheduleBlockRepository = null,
        ISlotReleaseService? slotReleaseService = null,
        IPublishEndpoint? publishEndpoint = null,
        ITransactionalExecutor? transactionalExecutor = null)
    {
        _appointmentRepository = appointmentRepository;
        _lifecyclePlanner = lifecyclePlanner;
        _resolutionSupersedingService = resolutionSupersedingService;
        _unitOfWork = unitOfWork;
        _patientRepository = patientRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _slotReleaseService = slotReleaseService;
        _publishEndpoint = publishEndpoint;
        _transactionalExecutor = transactionalExecutor;
    }

    public async Task Handle(CancelAppointmentCommand request, CancellationToken cancellationToken)
    {
        if (request.AuthenticatedUserId.HasValue && _patientRepository is not null)
        {
            var callerPatient = await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken)
                ?? throw new UnauthorizedAccessException("El usuario autenticado no tiene perfil de paciente.");

            var ownershipCheck = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken)
                ?? throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no se encuentra.");

            if (ownershipCheck.PatientId != callerPatient.Id)
                throw new UnauthorizedAccessException("No tienes permiso para cancelar la cita de otro paciente.");
        }

        if (_transactionalExecutor is not null)
        {
            await _transactionalExecutor.ExecuteAsync(
                $"overbook-source:{request.AppointmentId:N}",
                ct => CancelInternalAsync(request, ct),
                cancellationToken);
            return;
        }

        await CancelInternalAsync(request, cancellationToken);
    }

    private async Task CancelInternalAsync(CancelAppointmentCommand request, CancellationToken ct)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, ct)
            ?? throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no se encuentra.");

        var utcNow = DateTimeOffset.UtcNow;
        var correlationId = Guid.NewGuid();
        SlotReleasePreparationResult? releasePlan = null;

        if (_slotReleaseService is not null && _scheduleBlockRepository is not null)
        {
            var block = await _scheduleBlockRepository.GetByIdForUpdateAsync(appointment.ScheduleBlockId, ct)
                ?? await _scheduleBlockRepository.GetByIdAsync(appointment.ScheduleBlockId, ct);

            releasePlan = await _slotReleaseService.PrepareSlotVacatedAsync(
                appointment,
                block,
                "Cancelled",
                correlationId,
                utcNow,
                ct);
        }

        appointment.Cancel(request.Reason, utcNow);
        await _lifecyclePlanner.CancelPendingActionsAsync(appointment.Id, "AppointmentCancelled", utcNow, ct);
        await _resolutionSupersedingService.SupersedePendingForResourceAsync(appointment.Id, "AppointmentCancelled", ct);
        await _unitOfWork.SaveChangesAsync(ct);

        if (_publishEndpoint is not null && releasePlan is not null)
        {
            var published = false;
            if (releasePlan.SlotReleasedEvent is not null)
            {
                await _publishEndpoint.Publish(releasePlan.SlotReleasedEvent, ct);
                published = true;
            }
            if (releasePlan.OverbookOpenedEvent is not null)
            {
                await _publishEndpoint.Publish(releasePlan.OverbookOpenedEvent, ct);
                published = true;
            }
            if (published)
            {
                await _unitOfWork.SaveChangesAsync(ct);
            }
        }
    }
}
