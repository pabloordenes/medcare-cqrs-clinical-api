using MassTransit;
using MedCareOS.Application.Appointments.Events;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Appointments.Commands.GenerateOverbook;

public class GenerateOverbookCommandHandler : IRequestHandler<GenerateOverbookCommand, GenerateOverbookResult>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IBoxRepository _boxRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IReceptionTaskRepository _receptionTaskRepository;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GenerateOverbookCommandHandler> _logger;

    public GenerateOverbookCommandHandler(
        IAppointmentRepository appointmentRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IBoxRepository boxRepository,
        IPatientRepository patientRepository,
        IStaffRepository staffRepository,
        INotificationRepository notificationRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IReceptionTaskRepository receptionTaskRepository,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        ILogger<GenerateOverbookCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _boxRepository = boxRepository;
        _patientRepository = patientRepository;
        _staffRepository = staffRepository;
        _notificationRepository = notificationRepository;
        _policyRepository = policyRepository;
        _receptionTaskRepository = receptionTaskRepository;
        _publishEndpoint = publishEndpoint;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<GenerateOverbookResult> Handle(
        GenerateOverbookCommand request,
        CancellationToken cancellationToken)
    {
        var correlationId = request.CorrelationId.HasValue && request.CorrelationId.Value != Guid.Empty
            ? request.CorrelationId.Value
            : Guid.NewGuid();
        var utcNow = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Iniciando generación de SOBRECUPO para cita {ResourceId}. DecisionMode={DecisionMode}, CorrelationId={CorrelationId}",
            request.ResourceId,
            request.DecisionMode,
            correlationId);

        // 1. Idempotencia dura por SourceAppointmentId
        var existingOverbook = await _scheduleBlockRepository.GetOverbookByAppointmentIdAsync(request.ResourceId, cancellationToken);
        if (existingOverbook is not null)
        {
            if (existingOverbook.OverbookStatus == OverbookBlockStatus.Deactivated)
            {
                return new GenerateOverbookResult(
                    Outcome: GenerateOverbookOutcome.OverbookBlockDeactivated,
                    SourceAppointmentId: request.ResourceId,
                    OverbookBlockId: existingOverbook.Id,
                    ErrorCode: "OVERBOOK_BLOCK_DEACTIVATED",
                    Message: "El bloque de sobrecupo para esta cita fue desactivado previamente.",
                    DecisionMode: request.DecisionMode,
                    CorrelationId: correlationId);
            }

            _logger.LogInformation(
                "Sobrecupo ya existente ({OverbookBlockId}, Status={Status}) para cita {ResourceId}. Replay idempotente.",
                existingOverbook.Id,
                existingOverbook.OverbookStatus,
                request.ResourceId);

            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.AlreadyExists,
                SourceAppointmentId: request.ResourceId,
                OverbookBlockId: existingOverbook.Id,
                Message: "Sobrecupo ya existente para la cita indicada.",
                IsIdempotentReplay: true,
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        // 2. Existencia y estado de la cita origen (NO se cancela)
        var originalAppointment = await _appointmentRepository.GetAppointmentByIdAsync(request.ResourceId, cancellationToken);
        if (originalAppointment is null)
        {
            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.AppointmentNotFound,
                SourceAppointmentId: request.ResourceId,
                ErrorCode: "APPOINTMENT_NOT_FOUND",
                Message: $"La cita {request.ResourceId} no fue encontrada.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        // 3. No sobrecupar citas ya confirmadas por el paciente
        if (originalAppointment.IsPatientConfirmed)
        {
            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.AppointmentAlreadyConfirmed,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "APPOINTMENT_ALREADY_CONFIRMED",
                Message: "La cita ya cuenta con confirmación del paciente.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        // 4. Estado clínico válido (Scheduled/Confirmed sin confirmación final)
        if (originalAppointment.Status is not AppointmentStatus.Scheduled and not AppointmentStatus.Confirmed)
        {
            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.AppointmentInvalidState,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "APPOINTMENT_INVALID_STATE",
                Message: $"No se puede generar sobrecupo para una cita en estado '{originalAppointment.Status}'.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        // 5. Bloque futuro
        var originalBlock = await _scheduleBlockRepository.GetByIdAsync(originalAppointment.ScheduleBlockId, cancellationToken);
        if (originalBlock is null || originalBlock.StartTime <= utcNow.UtcDateTime)
        {
            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.BlockNotFuture,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "BLOCK_NOT_FUTURE",
                Message: "No se puede generar sobrecupo porque el bloque horario no existe o ya inició.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        // 6. Capacidad y disponibilidad del Box
        var box = await _boxRepository.GetByIdAsync(originalBlock.BoxId, cancellationToken);
        if (box is null || !box.IsActive || box.Capacity <= 0)
        {
            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.BoxInactiveOrAtCapacity,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "BOX_INACTIVE_OR_AT_CAPACITY",
                Message: "El box asignado está inactivo o no tiene capacidad habilitada.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        var activeOverbooksOnBox = await _scheduleBlockRepository.CountActiveOverbooksForBoxSlotAsync(
            box.Id,
            originalBlock.StartTime,
            originalBlock.EndTime,
            cancellationToken);

        if (activeOverbooksOnBox >= box.Capacity)
        {
            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.BoxInactiveOrAtCapacity,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "BOX_INACTIVE_OR_AT_CAPACITY",
                Message: $"El box {box.Name} alcanzó su capacidad máxima de sobrecupo ({box.Capacity}) para ese horario.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        var policy = await _policyRepository.GetOrDefaultAsync(originalAppointment.TenantId, cancellationToken);

        // 7. Salvaguarda (c): Deadline realmente vencido y cita sin confirmación
        if (!originalAppointment.DeadlineMissedAt.HasValue &&
            originalAppointment.ConfirmationState != ConfirmationState.Unresponsive)
        {
            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.DeadlineNotPassed,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "DEADLINE_NOT_PASSED",
                Message: "El deadline de confirmación aún no ha vencido para esta cita.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        // 8. Salvaguarda (a): Notificación de confirmación final en estado Delivered/Read (nunca solo Sent)
        var finalNotification = await _notificationRepository.GetLatestByAppointmentAndTypeAsync(
            originalAppointment.Id,
            NotificationType.FinalConfirmation,
            cancellationToken);

        if (finalNotification is null ||
            finalNotification.Status is not (NotificationStatus.Delivered or NotificationStatus.Read))
        {
            var existingTask = await _receptionTaskRepository.GetActiveTaskAsync(
                originalAppointment.Id,
                ReceptionTaskType.FailedNotificationEscalation,
                cancellationToken);

            Guid? taskId = existingTask?.Id;
            if (existingTask is null)
            {
                var statusLabel = finalNotification?.Status.ToString() ?? "Missing";
                var task = ReceptionTask.Create(
                    originalAppointment.Id,
                    originalAppointment.PatientId,
                    ReceptionTaskType.FailedNotificationEscalation,
                    ReceptionTaskPriority.High,
                    $"Sobrecupo bloqueado: confirmación final no alcanzó estado Delivered (estado actual: {statusLabel}). Contactar al paciente manualmente.");

                await _receptionTaskRepository.AddAsync(task, cancellationToken);
                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    taskId = task.Id;
                }
                catch (DbUpdateException)
                {
                    var racedTask = await _receptionTaskRepository.GetActiveTaskAsync(
                        originalAppointment.Id,
                        ReceptionTaskType.FailedNotificationEscalation,
                        cancellationToken);
                    taskId = racedTask?.Id;
                }
            }

            _logger.LogWarning(
                "Sobrecupo rechazado para cita {AppointmentId}: FinalConfirmation en estado {Status}. ReceptionTask={TaskId}",
                originalAppointment.Id,
                finalNotification?.Status.ToString() ?? "Missing",
                taskId);

            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.FinalNotificationNotDelivered,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "FINAL_NOTIFICATION_NOT_DELIVERED",
                Message: "La notificación de confirmación final no alcanzó estado Delivered. Se escaló a recepción.",
                ReceptionTaskId: taskId,
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        // 9. Salvaguarda de modo autónomo (E4-10)
        if (request.DecisionMode == OverbookDecisionMode.Autonomous && !policy.AutoOverbookEnabled)
        {
            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.AutonomousModeDisabled,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "AUTONOMOUS_MODE_DISABLED",
                Message: "El sobrecupo autónomo está deshabilitado para este tenant (modo sombra activo). Requiere aprobación HITL.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        // 10. Salvaguarda (b): Exclusión clínica en modo autónomo (HITL aprobado sí puede autorizarlo)
        var patient = await _patientRepository.GetPatientByIdAsync(originalAppointment.PatientId, cancellationToken);
        var staff = await _staffRepository.GetByIdAsync(originalBlock.StaffId, cancellationToken);
        if (patient is null || staff is null)
        {
            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.BlockNotFuture,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "CLINICAL_CONTEXT_INCOMPLETE",
                Message: "No se pudo resolver el paciente o el profesional asociado a la cita.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        var exclusion = ClinicalExclusionEvaluator.Evaluate(patient, staff, originalAppointment, policy, utcNow);
        if (exclusion.IsExcluded && request.DecisionMode == OverbookDecisionMode.Autonomous)
        {
            _logger.LogWarning(
                "Sobrecupo autónomo bloqueado por exclusión clínica ({Code}) en cita {AppointmentId}: {Reason}",
                exclusion.Code,
                originalAppointment.Id,
                exclusion.Reason);

            return new GenerateOverbookResult(
                Outcome: GenerateOverbookOutcome.ClinicalExclusion,
                SourceAppointmentId: originalAppointment.Id,
                ErrorCode: "CLINICAL_EXCLUSION",
                Message: exclusion.Reason ?? "La cita posee una exclusión clínica que requiere aprobación HITL.",
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId);
        }

        // 11. Creación atómica del bloque de sobrecupo + evento OverbookOpened vía Bus Outbox
        var overbookBlock = ScheduleBlock.CreateOverbook(originalBlock, originalAppointment.Id);
        var specialty = string.IsNullOrWhiteSpace(staff.Specialty) ? "Medicina General" : staff.Specialty.Trim();
        var startUtc = DateTime.SpecifyKind(overbookBlock.StartTime, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(overbookBlock.EndTime, DateTimeKind.Utc);

        _scheduleBlockRepository.AddRange([overbookBlock]);
        await _publishEndpoint.Publish(
            new OverbookOpened(
                OverbookBlockId: overbookBlock.Id,
                TenantId: originalAppointment.TenantId,
                Specialty: specialty,
                StaffId: overbookBlock.StaffId,
                BoxId: overbookBlock.BoxId,
                StartTimeUtc: startUtc,
                EndTimeUtc: endUtc,
                SourceAppointmentId: originalAppointment.Id,
                DecisionMode: request.DecisionMode,
                CorrelationId: correlationId,
                OccurredAtUtc: utcNow),
            cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSourceAppointmentUniqueViolation(ex))
        {
            var racedOverbook = await _scheduleBlockRepository.GetOverbookByAppointmentIdAsync(request.ResourceId, cancellationToken);
            if (racedOverbook is not null)
            {
                return new GenerateOverbookResult(
                    Outcome: GenerateOverbookOutcome.AlreadyExists,
                    SourceAppointmentId: request.ResourceId,
                    OverbookBlockId: racedOverbook.Id,
                    Message: "Sobrecupo ya existente para la cita indicada.",
                    IsIdempotentReplay: true,
                    DecisionMode: request.DecisionMode,
                    CorrelationId: correlationId);
            }

            throw;
        }

        _logger.LogInformation(
            "Sobrecupo {OverbookBlockId} generado exitosamente para cita {AppointmentId} (Mode={DecisionMode}, Exclusion={ExclusionCode}).",
            overbookBlock.Id,
            originalAppointment.Id,
            request.DecisionMode,
            exclusion.Code);

        return new GenerateOverbookResult(
            Outcome: GenerateOverbookOutcome.Created,
            SourceAppointmentId: originalAppointment.Id,
            OverbookBlockId: overbookBlock.Id,
            Message: "Sobrecupo generado exitosamente en MedCareOS.",
            IsIdempotentReplay: false,
            DecisionMode: request.DecisionMode,
            CorrelationId: correlationId);
    }

    private static bool IsSourceAppointmentUniqueViolation(DbUpdateException ex) =>
        DbExceptionHelper.IsUniqueConstraintViolation(ex, "IX_schedule_blocks_source_appointment_id");
}
