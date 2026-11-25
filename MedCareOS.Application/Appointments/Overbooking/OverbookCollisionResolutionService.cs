using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Application.WaitingLists.Services;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.Appointments.Overbooking;

public class OverbookCollisionResolutionService : IOverbookCollisionResolutionService
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IReceptionTaskRepository _receptionTaskRepository;
    private readonly IOverbookCollisionEventRepository _collisionEventRepository;
    private readonly IWaitingListOfferCancellationService _offerCancellationService;
    private readonly INotificationOutboxService _notificationOutboxService;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _notificationOptions;
    private readonly ILogger<OverbookCollisionResolutionService> _logger;

    public OverbookCollisionResolutionService(
        IAppointmentRepository appointmentRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IPatientRepository patientRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IReceptionTaskRepository receptionTaskRepository,
        IOverbookCollisionEventRepository collisionEventRepository,
        IWaitingListOfferCancellationService offerCancellationService,
        INotificationOutboxService notificationOutboxService,
        ITransactionalExecutor transactionalExecutor,
        IUnitOfWork unitOfWork,
        IOptions<NotificationOptions> notificationOptions,
        ILogger<OverbookCollisionResolutionService> logger)
    {
        _appointmentRepository = appointmentRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _patientRepository = patientRepository;
        _policyRepository = policyRepository;
        _receptionTaskRepository = receptionTaskRepository;
        _collisionEventRepository = collisionEventRepository;
        _offerCancellationService = offerCancellationService;
        _notificationOutboxService = notificationOutboxService;
        _transactionalExecutor = transactionalExecutor;
        _unitOfWork = unitOfWork;
        _notificationOptions = notificationOptions.Value;
        _logger = logger;
    }

    public async Task HandleLateConfirmationAsync(
        Guid originalAppointmentId,
        DateTimeOffset confirmedAtUtc,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        await _transactionalExecutor.ExecuteAsync(
            $"overbook-source:{originalAppointmentId:N}",
            async ct =>
            {
                var overbookBlock = await _scheduleBlockRepository.GetOverbookByAppointmentIdForUpdateAsync(
                    originalAppointmentId,
                    ct);

                if (overbookBlock is null)
                    return;

                if (overbookBlock.OverbookStatus is OverbookBlockStatus.Deactivated or OverbookBlockStatus.Expired)
                    return;

                var originalAppointment = await _appointmentRepository.GetAppointmentByIdAsync(originalAppointmentId, ct);
                if (originalAppointment is null)
                    return;

                var overbookAppointment = overbookBlock.OccupiedAppointmentId.HasValue
                    ? await _appointmentRepository.GetAppointmentByIdAsync(overbookBlock.OccupiedAppointmentId.Value, ct)
                    : await _appointmentRepository.GetActiveByScheduleBlockIdAsync(overbookBlock.Id, ct);

                var utcNow = DateTimeOffset.UtcNow;

                if (overbookAppointment is null &&
                    overbookBlock.OverbookStatus is OverbookBlockStatus.Open or OverbookBlockStatus.Offered)
                {
                    var cancelledOffers = await _offerCancellationService.CancelPendingOffersForOverbookUndoAsync(
                        overbookBlock.Id,
                        "LateConfirmationUndo",
                        utcNow,
                        correlationId,
                        ct);

                    if (overbookBlock.DeactivateOverbook("LateConfirmationUndo", utcNow))
                    {
                        await _collisionEventRepository.AddAsync(
                            OverbookCollisionEvent.CreateUndo(
                                originalAppointment.TenantId,
                                originalAppointment.Id,
                                overbookBlock.Id,
                                "LateConfirmationUndo",
                                utcNow),
                            ct);
                    }

                    await _unitOfWork.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "Ventana de deshacer aplicada: sobrecupo {OverbookBlockId} desactivado tras confirmación tardía de cita {AppointmentId}. Ofertas canceladas={CancelledOffers}.",
                        overbookBlock.Id,
                        originalAppointment.Id,
                        cancelledOffers);
                    return;
                }

                if (overbookAppointment is null)
                    return;

                // Colisión real: el sobrecupo ya fue aceptado antes de la confirmación tardía
                var decision = OverbookPriority.Resolve(
                    new OverbookParticipant(originalAppointment.Id, originalAppointment.OverbookConfirmationInstant, originalAppointment.CheckedInAt),
                    new OverbookParticipant(overbookAppointment.Id, overbookAppointment.OverbookConfirmationInstant, overbookAppointment.CheckedInAt));

                await ApplyCollisionOutcomeAsync(
                    originalAppointment,
                    overbookAppointment,
                    overbookBlock,
                    decision,
                    triggerSource: "LateConfirmation",
                    taskPriority: ReceptionTaskPriority.High,
                    correlationId: correlationId,
                    utcNow: utcNow,
                    cancellationToken: ct);

                await _unitOfWork.SaveChangesAsync(ct);
            },
            cancellationToken);
    }

    public async Task<OverbookPriorityDecision?> EvaluateDoubleCheckInAsync(
        Appointment checkedInAppointment,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkedInAppointment);

        var currentBlock = await _scheduleBlockRepository.GetByIdAsync(checkedInAppointment.ScheduleBlockId, cancellationToken);
        if (currentBlock is null)
            return null;

        ScheduleBlock? overbookBlock;
        Appointment? originalAppointment;
        Appointment? overbookAppointment;

        if (currentBlock.IsOverbook && currentBlock.SourceAppointmentId.HasValue)
        {
            overbookBlock = currentBlock;
            overbookAppointment = checkedInAppointment;
            originalAppointment = await _appointmentRepository.GetAppointmentByIdAsync(currentBlock.SourceAppointmentId.Value, cancellationToken);
        }
        else
        {
            overbookBlock = await _scheduleBlockRepository.GetOverbookByAppointmentIdForUpdateAsync(
                checkedInAppointment.Id,
                cancellationToken);
            if (overbookBlock is null)
                return null;

            originalAppointment = checkedInAppointment;
            overbookAppointment = overbookBlock.OccupiedAppointmentId.HasValue
                ? await _appointmentRepository.GetAppointmentByIdAsync(overbookBlock.OccupiedAppointmentId.Value, cancellationToken)
                : await _appointmentRepository.GetActiveByScheduleBlockIdAsync(overbookBlock.Id, cancellationToken);
        }

        if (originalAppointment is null ||
            overbookBlock.OverbookStatus is OverbookBlockStatus.Deactivated or OverbookBlockStatus.Expired)
        {
            return null;
        }

        // Si el paciente original hace check-in antes de que alguien acepte la oferta de sobrecupo, se desactiva el sobrecupo
        if (overbookAppointment is null &&
            overbookBlock.OverbookStatus is OverbookBlockStatus.Open or OverbookBlockStatus.Offered)
        {
            var correlationId = Guid.NewGuid();
            await _offerCancellationService.CancelPendingOffersForOverbookUndoAsync(
                overbookBlock.Id,
                "OriginalCheckedInUndo",
                utcNow,
                correlationId,
                cancellationToken);

            if (overbookBlock.DeactivateOverbook("OriginalCheckedInUndo", utcNow))
            {
                await _collisionEventRepository.AddAsync(
                    OverbookCollisionEvent.CreateUndo(
                        originalAppointment.TenantId,
                        originalAppointment.Id,
                        overbookBlock.Id,
                        "OriginalCheckedInUndo",
                        utcNow),
                    cancellationToken);
            }

            return null;
        }

        if (overbookAppointment is null)
            return null;

        bool originalArrived = originalAppointment.CheckedInAt.HasValue ||
                               originalAppointment.Status is AppointmentStatus.InProgress or AppointmentStatus.Finished;
        bool overbookArrived = overbookAppointment.CheckedInAt.HasValue ||
                               overbookAppointment.Status is AppointmentStatus.InProgress or AppointmentStatus.Finished;

        if (!originalArrived || !overbookArrived)
            return null;

        var decision = OverbookPriority.Resolve(
            new OverbookParticipant(originalAppointment.Id, originalAppointment.OverbookConfirmationInstant, originalAppointment.CheckedInAt),
            new OverbookParticipant(overbookAppointment.Id, overbookAppointment.OverbookConfirmationInstant, overbookAppointment.CheckedInAt));

        await ApplyCollisionOutcomeAsync(
            originalAppointment,
            overbookAppointment,
            overbookBlock,
            decision,
            triggerSource: "DoubleCheckIn",
            taskPriority: ReceptionTaskPriority.Urgent,
            correlationId: Guid.NewGuid(),
            utcNow: utcNow,
            cancellationToken: cancellationToken);

        return decision;
    }

    private async Task ApplyCollisionOutcomeAsync(
        Appointment originalAppointment,
        Appointment overbookAppointment,
        ScheduleBlock overbookBlock,
        OverbookPriorityDecision decision,
        string triggerSource,
        ReceptionTaskPriority taskPriority,
        Guid correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        Appointment? displacedAppointment = decision.DisplacedAppointmentId switch
        {
            var id when id == originalAppointment.Id => originalAppointment,
            var id when id == overbookAppointment.Id => overbookAppointment,
            _ => null
        };

        if (displacedAppointment is not null &&
            displacedAppointment.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed)
        {
            displacedAppointment.MarkDisplacedByOverbook(utcNow);
        }

        var alreadyLogged = await _collisionEventRepository.ExistsForTriggerAsync(
            originalAppointment.Id,
            overbookBlock.Id,
            triggerSource,
            cancellationToken);

        if (!alreadyLogged)
        {
            await _collisionEventRepository.AddAsync(
                OverbookCollisionEvent.CreateCollision(
                    originalAppointment.TenantId,
                    originalAppointment.Id,
                    overbookAppointment.Id,
                    overbookBlock.Id,
                    triggerSource,
                    decision,
                    utcNow),
                cancellationToken);
        }

        var taskAppointment = displacedAppointment ?? originalAppointment;
        var existingTask = await _receptionTaskRepository.GetActiveTaskAsync(
            taskAppointment.Id,
            ReceptionTaskType.OverbookConflict,
            cancellationToken);

        if (existingTask is null)
        {
            var task = ReceptionTask.Create(
                taskAppointment.Id,
                taskAppointment.PatientId,
                ReceptionTaskType.OverbookConflict,
                taskPriority,
                decision.ReceptionInstruction);

            await _receptionTaskRepository.AddAsync(task, cancellationToken);
        }
        else
        {
            existingTask.Escalate(taskPriority, decision.ReceptionInstruction, utcNow);
        }

        if (displacedAppointment is not null)
        {
            await EnqueueRescheduleHelpIfNeededAsync(displacedAppointment, correlationId, utcNow, cancellationToken);
        }

        _logger.LogWarning(
            "Colisión de sobrecupo resuelta ({TriggerSource}): Original={OriginalId}, Overbook={OverbookId}, Winner={Winner}, Rule={Rule}",
            triggerSource,
            originalAppointment.Id,
            overbookAppointment.Id,
            decision.Winner,
            decision.RuleApplied);
    }

    private async Task EnqueueRescheduleHelpIfNeededAsync(
        Appointment displacedAppointment,
        Guid correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetPatientByIdAsync(displacedAppointment.PatientId, cancellationToken);
        var block = await _scheduleBlockRepository.GetByIdAsync(displacedAppointment.ScheduleBlockId, cancellationToken);
        if (patient is null || block is null)
            return;

        var policy = await _policyRepository.GetOrDefaultAsync(displacedAppointment.TenantId, cancellationToken);
        var (appointmentDate, appointmentTime) = NotificationFormattingHelper.FormatAppointmentDateTime(
            block.StartTime,
            policy.TimeZoneId);

        await _notificationOutboxService.EnqueueAsync(
            new EnqueueNotificationRequest(
                TenantId: displacedAppointment.TenantId,
                AppointmentId: displacedAppointment.Id,
                Patient: patient,
                Type: NotificationType.RescheduleHelp,
                TemplateKey: DefaultNotificationTemplateCatalog.Keys.RescheduleHelp,
                Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                ScheduledFor: displacedAppointment.DisplacedAt ?? utcNow,
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["patientFirstName"] = patient.FirstName,
                    ["clinicName"] = _notificationOptions.ClinicName,
                    ["appointmentDate"] = appointmentDate,
                    ["appointmentTime"] = appointmentTime
                },
                CorrelationId: correlationId,
                ForceImmediateDispatch: true),
            cancellationToken);
    }
}
