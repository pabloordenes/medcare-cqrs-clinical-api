using MassTransit;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Services;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Application.Resolutions.Services;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Exceptions;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.Appointments.Commands.RescheduleAppointment;

public class RescheduleAppointmentCommandHandler : IRequestHandler<RescheduleAppointmentCommand, RescheduleAppointmentResult>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IStaffRepository _staffRepository;
    private readonly IBoxRepository _boxRepository;
    private readonly IWaitingListOfferRepository _waitingListOfferRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IAppointmentRescheduleHistoryRepository _rescheduleHistoryRepository;
    private readonly IAppointmentLifecyclePlanner _lifecyclePlanner;
    private readonly IAiResolutionSupersedingService _resolutionSupersedingService;
    private readonly ISlotReleaseService _slotReleaseService;
    private readonly INotificationOutboxService _notificationOutboxService;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _notificationOptions;
    private readonly ILogger<RescheduleAppointmentCommandHandler> _logger;

    public RescheduleAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IPatientRepository patientRepository,
        IStaffRepository staffRepository,
        IBoxRepository boxRepository,
        IWaitingListOfferRepository waitingListOfferRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IAppointmentRescheduleHistoryRepository rescheduleHistoryRepository,
        IAppointmentLifecyclePlanner lifecyclePlanner,
        IAiResolutionSupersedingService resolutionSupersedingService,
        ISlotReleaseService slotReleaseService,
        INotificationOutboxService notificationOutboxService,
        ITransactionalExecutor transactionalExecutor,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        IOptions<NotificationOptions> notificationOptions,
        ILogger<RescheduleAppointmentCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _patientRepository = patientRepository;
        _staffRepository = staffRepository;
        _boxRepository = boxRepository;
        _waitingListOfferRepository = waitingListOfferRepository;
        _policyRepository = policyRepository;
        _rescheduleHistoryRepository = rescheduleHistoryRepository;
        _lifecyclePlanner = lifecyclePlanner;
        _resolutionSupersedingService = resolutionSupersedingService;
        _slotReleaseService = slotReleaseService;
        _notificationOutboxService = notificationOutboxService;
        _transactionalExecutor = transactionalExecutor;
        _publishEndpoint = publishEndpoint;
        _unitOfWork = unitOfWork;
        _notificationOptions = notificationOptions.Value;
        _logger = logger;
    }

    public async Task<RescheduleAppointmentResult> Handle(
        RescheduleAppointmentCommand request,
        CancellationToken cancellationToken)
    {
        if (request.AppointmentId == Guid.Empty)
            throw new ArgumentException("AppointmentId no puede estar vacío.", nameof(request.AppointmentId));
        if (request.NewScheduleBlockId == Guid.Empty)
            throw new ArgumentException("NewScheduleBlockId no puede estar vacío.", nameof(request.NewScheduleBlockId));

        Guid? expectedPatientId = null;
        if (request.AuthenticatedUserId.HasValue)
        {
            var callerPatient = await _patientRepository.GetByUserId(request.AuthenticatedUserId.Value, cancellationToken)
                ?? throw new UnauthorizedAccessException("El usuario autenticado no tiene perfil de paciente.");
            expectedPatientId = callerPatient.Id;
        }
        else if (request.ExpectedPatientId.HasValue && request.ExpectedPatientId.Value != Guid.Empty)
        {
            expectedPatientId = request.ExpectedPatientId.Value;
        }

        var initialAppointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no existe.");

        if (expectedPatientId.HasValue && initialAppointment.PatientId != expectedPatientId.Value)
            throw new UnauthorizedAccessException("No tienes permiso para reagendar la cita de otro paciente.");

        var lockKeys = new[]
        {
            $"block:{initialAppointment.ScheduleBlockId:N}",
            $"block:{request.NewScheduleBlockId:N}",
            $"overbook-source:{initialAppointment.Id:N}"
        };

        return await _transactionalExecutor.ExecuteOrderedAsync(
            lockKeys,
            async ct =>
            {
                var utcNow = DateTimeOffset.UtcNow;
                var correlationId = Guid.NewGuid();

                var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, ct)
                    ?? throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no existe.");

                if (expectedPatientId.HasValue && appointment.PatientId != expectedPatientId.Value)
                    throw new UnauthorizedAccessException("No tienes permiso para reagendar la cita de otro paciente.");

                var oldBlockId = appointment.ScheduleBlockId;
                var oldBlock = await _scheduleBlockRepository.GetByIdForUpdateAsync(oldBlockId, ct)
                    ?? await _scheduleBlockRepository.GetByIdAsync(oldBlockId, ct);

                var newBlock = await _scheduleBlockRepository.GetByIdForUpdateAsync(request.NewScheduleBlockId, ct)
                    ?? await _scheduleBlockRepository.GetByIdAsync(request.NewScheduleBlockId, ct)
                    ?? throw new KeyNotFoundException($"El bloque horario {request.NewScheduleBlockId} no existe.");

                if (newBlock.IsOverbook)
                    throw new InvalidOperationException("No se puede reagendar directamente hacia un bloque de sobrecupo.");

                if (newBlock.StartTime <= utcNow.UtcDateTime)
                    throw new InvalidOperationException("No se puede reagendar hacia un bloque horario pasado.");

                var newBox = await _boxRepository.GetByIdAsync(newBlock.BoxId, ct);
                if (newBox is null || !newBox.IsActive)
                    throw new InvalidOperationException("El box del nuevo horario no existe o está inactivo.");

                if (await _appointmentRepository.HasActiveAppointmentForBlockAsync(newBlock.Id, ct))
                    throw new DuplicateAppointmentException("El nuevo bloque horario ya se encuentra reservado.");

                if (await _waitingListOfferRepository.HasActivePendingOfferForBlockAsync(newBlock.Id, utcNow, ct))
                    throw new DuplicateAppointmentException("El nuevo bloque horario tiene una reserva temporal activa para lista de espera.");

                var policy = await _policyRepository.GetOrDefaultAsync(appointment.TenantId, ct);
                var wasDisplaced = appointment.IsDisplacedByOverbook || appointment.WasEverDisplacedByOverbook;

                var patient = await _patientRepository.GetPatientByIdAsync(appointment.PatientId, ct)
                    ?? throw new KeyNotFoundException($"El paciente {appointment.PatientId} no existe.");
                var newStaff = await _staffRepository.GetByIdAsync(newBlock.StaffId, ct)
                    ?? throw new KeyNotFoundException($"El profesional {newBlock.StaffId} no existe.");

                var releasePlan = await _slotReleaseService.PrepareSlotVacatedAsync(
                    appointment,
                    oldBlock,
                    "Rescheduled",
                    correlationId,
                    utcNow,
                    ct);

                appointment.Reschedule(
                    newBlock.Id,
                    utcNow,
                    policy.MaxReschedulesPerAppointment,
                    bypassLimitIfDisplaced: true);

                var history = AppointmentRescheduleHistory.Create(
                    tenantId: appointment.TenantId,
                    appointmentId: appointment.Id,
                    patientId: appointment.PatientId,
                    fromScheduleBlockId: oldBlockId,
                    toScheduleBlockId: newBlock.Id,
                    triggeredByUserId: request.AuthenticatedUserId,
                    channel: request.Channel,
                    reason: request.Reason,
                    wasDisplacedPriority: wasDisplaced,
                    rescheduleNumber: appointment.ReschedulesCount,
                    rescheduledAtUtc: utcNow);

                await _rescheduleHistoryRepository.AddAsync(history, ct);
                await _lifecyclePlanner.RecalculateForRescheduleAsync(appointment, newBlock, correlationId, utcNow, ct);
                await _resolutionSupersedingService.SupersedePendingForResourceAsync(appointment.Id, "AppointmentRescheduled", ct);

                var (appointmentDate, appointmentTime) = NotificationFormattingHelper.FormatAppointmentDateTime(
                    newBlock.StartTime,
                    policy.TimeZoneId);
                var specialtyDisplay = NotificationFormattingHelper.ResolveSpecialtyDisplay(
                    newStaff.Specialty,
                    _notificationOptions.IncludeSpecialtyInMessages,
                    _notificationOptions.SensitiveSpecialtiesExcluded);

                var notifResult = await _notificationOutboxService.EnqueueAsync(
                    new EnqueueNotificationRequest(
                        TenantId: appointment.TenantId,
                        AppointmentId: appointment.Id,
                        Patient: patient,
                        Type: NotificationType.AppointmentCreated,
                        TemplateKey: DefaultNotificationTemplateCatalog.Keys.AppointmentCreated,
                        Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                        ScheduledFor: utcNow,
                        Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["patientFirstName"] = patient.FirstName,
                            ["clinicName"] = _notificationOptions.ClinicName,
                            ["appointmentDate"] = appointmentDate,
                            ["appointmentTime"] = appointmentTime,
                            ["specialtyDisplay"] = specialtyDisplay
                        },
                        CorrelationId: correlationId,
                        ForceImmediateDispatch: true,
                        DeferPublishToCaller: true),
                    ct);

                try
                {
                    await _unitOfWork.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (DbExceptionHelper.IsUniqueConstraintViolation(ex, "IX_citas_schedule_block_id"))
                {
                    throw new DuplicateAppointmentException("El nuevo bloque horario fue reservado concurrentemente.");
                }

                var slotReleasedTriggered = false;
                if (releasePlan.SlotReleasedEvent is not null)
                {
                    await _publishEndpoint.Publish(releasePlan.SlotReleasedEvent, ct);
                    slotReleasedTriggered = true;
                }

                if (releasePlan.OverbookOpenedEvent is not null)
                {
                    await _publishEndpoint.Publish(releasePlan.OverbookOpenedEvent, ct);
                }

                if (notifResult.PendingDispatchEvent is not null)
                {
                    await _publishEndpoint.Publish(notifResult.PendingDispatchEvent, ct);
                }

                if (slotReleasedTriggered || releasePlan.OverbookOpenedEvent is not null || notifResult.PendingDispatchEvent is not null)
                {
                    await _unitOfWork.SaveChangesAsync(ct);
                }

                _logger.LogInformation(
                    "Cita {AppointmentId} reagendada de bloque {OldBlockId} a {NewBlockId} (ReschedulesCount={Count}, SlotReleasedTriggered={Released}).",
                    appointment.Id,
                    oldBlockId,
                    newBlock.Id,
                    appointment.ReschedulesCount,
                    slotReleasedTriggered);

                return new RescheduleAppointmentResult(
                    AppointmentId: appointment.Id,
                    PreviousScheduleBlockId: oldBlockId,
                    NewScheduleBlockId: newBlock.Id,
                    Status: appointment.Status,
                    ConfirmationState: appointment.ConfirmationState,
                    ReschedulesCount: appointment.ReschedulesCount,
                    SlotReleasedTriggered: slotReleasedTriggered,
                    RescheduledAtUtc: utcNow);
            },
            cancellationToken);
    }
}
