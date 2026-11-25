using System.Text.Json;
using MassTransit;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Appointments.Services;
using MedCareOS.Application.Common.Persistence;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Application.Resolutions.Services;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Appointments.Commands.DeclineAppointment;

public class DeclineAppointmentCommandHandler : IRequestHandler<DeclineAppointmentCommand, DeclineAppointmentResult>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IAppointmentLifecyclePlanner _lifecyclePlanner;
    private readonly IRiskSignalProcessor _riskSignalProcessor;
    private readonly IAiResolutionSupersedingService _resolutionSupersedingService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeclineAppointmentCommandHandler> _logger;
    private readonly IScheduleBlockRepository? _scheduleBlockRepository;
    private readonly ISlotReleaseService? _slotReleaseService;
    private readonly ITransactionalExecutor? _transactionalExecutor;

    public DeclineAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IAppointmentLifecyclePlanner lifecyclePlanner,
        IRiskSignalProcessor riskSignalProcessor,
        IAiResolutionSupersedingService resolutionSupersedingService,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        ILogger<DeclineAppointmentCommandHandler> logger,
        IScheduleBlockRepository? scheduleBlockRepository = null,
        ISlotReleaseService? slotReleaseService = null,
        ITransactionalExecutor? transactionalExecutor = null)
    {
        _appointmentRepository = appointmentRepository;
        _lifecyclePlanner = lifecyclePlanner;
        _riskSignalProcessor = riskSignalProcessor;
        _resolutionSupersedingService = resolutionSupersedingService;
        _publishEndpoint = publishEndpoint;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _scheduleBlockRepository = scheduleBlockRepository;
        _slotReleaseService = slotReleaseService;
        _transactionalExecutor = transactionalExecutor;
    }

    public async Task<DeclineAppointmentResult> Handle(
        DeclineAppointmentCommand request,
        CancellationToken cancellationToken)
    {
        if (_transactionalExecutor is not null)
        {
            return await _transactionalExecutor.ExecuteAsync(
                $"overbook-source:{request.AppointmentId:N}",
                ct => HandleInternalAsync(request, ct),
                cancellationToken);
        }

        return await HandleInternalAsync(request, cancellationToken);
    }

    private async Task<DeclineAppointmentResult> HandleInternalAsync(
        DeclineAppointmentCommand request,
        CancellationToken cancellationToken)
    {
        var appointment = await _appointmentRepository.GetAppointmentByIdAsync(request.AppointmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"La cita con id {request.AppointmentId} no se encuentra.");

        if (request.ExpectedPatientId.HasValue && appointment.PatientId != request.ExpectedPatientId.Value)
        {
            throw new InvalidOperationException("La cita no pertenece al paciente indicado.");
        }

        var utcNow = request.OccurredAtUtc ?? DateTimeOffset.UtcNow;
        var correlationId = request.CorrelationId ?? Guid.NewGuid();
        var effectiveReason = !string.IsNullOrWhiteSpace(request.Reason)
            ? request.Reason.Trim()
            : (request.Channel.HasValue
                ? $"Declinada por paciente vía {request.Channel.Value}."
                : "Declinada por el paciente.");

        SlotReleasePreparationResult? releasePlan = null;
        if (_slotReleaseService is not null && _scheduleBlockRepository is not null)
        {
            var vacatedBlock = await _scheduleBlockRepository.GetByIdForUpdateAsync(appointment.ScheduleBlockId, cancellationToken)
                ?? await _scheduleBlockRepository.GetByIdAsync(appointment.ScheduleBlockId, cancellationToken);

            if (appointment.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed &&
                appointment.ConfirmationState != ConfirmationState.DeclinedByPatient)
            {
                releasePlan = await _slotReleaseService.PrepareSlotVacatedAsync(
                    appointment,
                    vacatedBlock,
                    "DeclinedByPatient",
                    correlationId,
                    utcNow,
                    cancellationToken);
            }
        }

        var transition = appointment.DeclineByPatient(effectiveReason, utcNow);

        if (transition.StateChanged)
        {
            var source = string.IsNullOrWhiteSpace(request.Source) ? "patient_decline" : request.Source.Trim();
            var metadataJson = JsonSerializer.Serialize(new
            {
                channel = request.Channel?.ToString(),
                notificationId = request.NotificationId,
                normalizedKeyword = request.NormalizedKeyword,
                previousState = transition.PreviousState.ToString(),
                newState = transition.NewState.ToString()
            });

            await _riskSignalProcessor.RecordAsync(
                new RecordRiskSignalRequest(
                    AppointmentId: appointment.Id,
                    Kind: RiskSignalKind.DeclinedByPatient,
                    Source: source,
                    IdempotencyKey: $"declined_by_patient:{appointment.Id:N}",
                    CorrelationId: correlationId,
                    OccurredAtUtc: utcNow,
                    MetadataJson: metadataJson),
                cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Declinación repetida ignorada idempotente para cita {AppointmentId}.",
                appointment.Id);
        }

        const string cancelReason = "PatientDeclined";
        var cancelledActions = await _lifecyclePlanner.CancelPendingActionsAsync(
            appointment.Id,
            cancelReason,
            utcNow,
            cancellationToken);

        var supersededTasks = await _resolutionSupersedingService.SupersedePendingForResourceAsync(
            appointment.Id,
            cancelReason,
            cancellationToken);

        if (transition.StateChanged && request.PublishPatientReplyEvent)
        {
            await _publishEndpoint.Publish(
                new PatientAppointmentReplyReceived(
                    AppointmentId: appointment.Id,
                    PatientId: appointment.PatientId,
                    NotificationId: request.NotificationId,
                    Intent: PatientReplyIntent.Cancel,
                    Channel: request.Channel ?? NotificationChannel.Email,
                    ReceivedAtUtc: utcNow,
                    NormalizedKeyword: string.IsNullOrWhiteSpace(request.NormalizedKeyword)
                        ? "DECLINE"
                        : request.NormalizedKeyword.Trim()),
                cancellationToken);
        }

        if (transition.StateChanged || cancelledActions > 0 || supersededTasks > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (transition.StateChanged && releasePlan is not null)
        {
            var publishedRelease = false;
            if (releasePlan.SlotReleasedEvent is not null)
            {
                await _publishEndpoint.Publish(releasePlan.SlotReleasedEvent, cancellationToken);
                publishedRelease = true;
            }
            if (releasePlan.OverbookOpenedEvent is not null)
            {
                await _publishEndpoint.Publish(releasePlan.OverbookOpenedEvent, cancellationToken);
                publishedRelease = true;
            }
            if (publishedRelease)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        return new DeclineAppointmentResult(
            AppointmentId: appointment.Id,
            Outcome: transition.Outcome,
            AppointmentStatus: appointment.Status,
            ConfirmationState: appointment.ConfirmationState,
            StateChanged: transition.StateChanged,
            WasIdempotentReplay: !transition.StateChanged,
            CancelledScheduledActionsCount: cancelledActions,
            SupersededHitlTasksCount: supersededTasks);
    }
}
