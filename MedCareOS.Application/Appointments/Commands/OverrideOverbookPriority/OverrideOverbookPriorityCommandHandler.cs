using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;
using MediatR;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.Appointments.Commands.OverrideOverbookPriority;

public class OverrideOverbookPriorityCommandHandler
    : IRequestHandler<OverrideOverbookPriorityCommand, OverrideOverbookPriorityResult>
{
    public const int MinReasonLength = 10;

    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IScheduleBlockRepository _scheduleBlockRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly IReceptionTaskRepository _receptionTaskRepository;
    private readonly IOverbookCollisionEventRepository _collisionEventRepository;
    private readonly INotificationOutboxService _notificationOutboxService;
    private readonly ITransactionalExecutor _transactionalExecutor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _notificationOptions;

    public OverrideOverbookPriorityCommandHandler(
        IAppointmentRepository appointmentRepository,
        IScheduleBlockRepository scheduleBlockRepository,
        IPatientRepository patientRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IReceptionTaskRepository receptionTaskRepository,
        IOverbookCollisionEventRepository collisionEventRepository,
        INotificationOutboxService notificationOutboxService,
        ITransactionalExecutor transactionalExecutor,
        IUnitOfWork unitOfWork,
        IOptions<NotificationOptions> notificationOptions)
    {
        _appointmentRepository = appointmentRepository;
        _scheduleBlockRepository = scheduleBlockRepository;
        _patientRepository = patientRepository;
        _policyRepository = policyRepository;
        _receptionTaskRepository = receptionTaskRepository;
        _collisionEventRepository = collisionEventRepository;
        _notificationOutboxService = notificationOutboxService;
        _transactionalExecutor = transactionalExecutor;
        _unitOfWork = unitOfWork;
        _notificationOptions = notificationOptions.Value;
    }

    public async Task<OverrideOverbookPriorityResult> Handle(
        OverrideOverbookPriorityCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < MinReasonLength)
            throw new ArgumentException($"El motivo del override es obligatorio y debe tener al menos {MinReasonLength} caracteres.", nameof(request.Reason));

        if (request.WinnerAppointmentId == Guid.Empty)
            throw new ArgumentException("WinnerAppointmentId es obligatorio.", nameof(request.WinnerAppointmentId));

        if (request.OverriddenByUserId == Guid.Empty)
            throw new ArgumentException("El usuario autenticado que realiza el override es obligatorio.", nameof(request.OverriddenByUserId));

        return await _transactionalExecutor.ExecuteAsync(
            $"override-collision:{request.AppointmentId:N}",
            async ct =>
            {
                var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, ct)
                    ?? throw new KeyNotFoundException($"La cita {request.AppointmentId} no existe.");

                var currentBlock = await _scheduleBlockRepository.GetByIdAsync(appointment.ScheduleBlockId, ct)
                    ?? throw new KeyNotFoundException($"El bloque horario {appointment.ScheduleBlockId} no existe.");

                ScheduleBlock? overbookBlock;
                Appointment? originalAppointment;
                Appointment? overbookAppointment;

                if (currentBlock.IsOverbook && currentBlock.SourceAppointmentId.HasValue)
                {
                    overbookBlock = currentBlock;
                    overbookAppointment = appointment;
                    originalAppointment = await _appointmentRepository.GetAppointmentByIdAsync(currentBlock.SourceAppointmentId.Value, ct);
                }
                else
                {
                    overbookBlock = await _scheduleBlockRepository.GetOverbookByAppointmentIdForUpdateAsync(appointment.Id, ct);
                    originalAppointment = appointment;
                    overbookAppointment = overbookBlock is null
                        ? null
                        : (overbookBlock.OccupiedAppointmentId.HasValue
                            ? await _appointmentRepository.GetAppointmentByIdAsync(overbookBlock.OccupiedAppointmentId.Value, ct)
                            : await _appointmentRepository.GetActiveByScheduleBlockIdAsync(overbookBlock.Id, ct));
                }

                if (overbookBlock is null || originalAppointment is null || overbookAppointment is null)
                    throw new InvalidOperationException("No existe una colisión activa entre cita original y sobrecupo ocupado para sobrescribir.");

                if (request.WinnerAppointmentId != originalAppointment.Id &&
                    request.WinnerAppointmentId != overbookAppointment.Id)
                {
                    throw new ArgumentException("WinnerAppointmentId debe corresponder a la cita original o a la cita de sobrecupo.", nameof(request.WinnerAppointmentId));
                }

                var utcNow = DateTimeOffset.UtcNow;
                var winnerIsOriginal = request.WinnerAppointmentId == originalAppointment.Id;
                var winner = winnerIsOriginal ? originalAppointment : overbookAppointment;
                var displaced = winnerIsOriginal ? overbookAppointment : originalAppointment;
                var winnerEnum = winnerIsOriginal ? OverbookWinner.Original : OverbookWinner.Overbook;

                winner.ClearDisplacedByOverbook(utcNow);
                displaced.MarkDisplacedByOverbook(utcNow);

                var auditEvent = OverbookCollisionEvent.CreateManualOverride(
                    originalAppointment.TenantId,
                    originalAppointment.Id,
                    overbookAppointment.Id,
                    overbookBlock.Id,
                    winnerEnum,
                    winner.Id,
                    displaced.Id,
                    request.Reason,
                    request.OverriddenByUserId,
                    utcNow);

                await _collisionEventRepository.AddAsync(auditEvent, ct);

                await CompleteActiveConflictTaskAsync(originalAppointment.Id, request.OverriddenByUserId, request.Reason, ct);
                await CompleteActiveConflictTaskAsync(overbookAppointment.Id, request.OverriddenByUserId, request.Reason, ct);

                var displacedPatient = await _patientRepository.GetPatientByIdAsync(displaced.PatientId, ct);
                var displacedBlock = await _scheduleBlockRepository.GetByIdAsync(displaced.ScheduleBlockId, ct);
                if (displacedPatient is not null && displacedBlock is not null)
                {
                    var policy = await _policyRepository.GetOrDefaultAsync(displaced.TenantId, ct);
                    var (date, time) = NotificationFormattingHelper.FormatAppointmentDateTime(displacedBlock.StartTime, policy.TimeZoneId);

                    await _notificationOutboxService.EnqueueAsync(
                        new EnqueueNotificationRequest(
                            TenantId: displaced.TenantId,
                            AppointmentId: displaced.Id,
                            Patient: displacedPatient,
                            Type: NotificationType.RescheduleHelp,
                            TemplateKey: DefaultNotificationTemplateCatalog.Keys.RescheduleHelp,
                            Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                            ScheduledFor: utcNow,
                            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["patientFirstName"] = displacedPatient.FirstName,
                                ["clinicName"] = _notificationOptions.ClinicName,
                                ["appointmentDate"] = date,
                                ["appointmentTime"] = time
                            },
                            CorrelationId: auditEvent.Id,
                            ForceImmediateDispatch: true),
                        ct);
                }

                await _unitOfWork.SaveChangesAsync(ct);

                return new OverrideOverbookPriorityResult(
                    originalAppointment.Id,
                    overbookAppointment.Id,
                    overbookBlock.Id,
                    winnerEnum,
                    winner.Id,
                    displaced.Id,
                    auditEvent.Id);
            },
            cancellationToken);
    }

    private async Task CompleteActiveConflictTaskAsync(
        Guid appointmentId,
        Guid userId,
        string reason,
        CancellationToken ct)
    {
        var task = await _receptionTaskRepository.GetActiveTaskAsync(
            appointmentId,
            ReceptionTaskType.OverbookConflict,
            ct);
        task?.Complete(userId, $"Override manual de prioridad: {reason.Trim()}");
    }
}
