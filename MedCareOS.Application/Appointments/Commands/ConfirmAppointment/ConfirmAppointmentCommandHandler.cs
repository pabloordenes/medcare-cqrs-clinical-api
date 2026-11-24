using System.Text.Json;
using MassTransit;
using MedCareOS.Application.Appointments.Lifecycle;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Notifications.Events;
using MedCareOS.Application.Resolutions.Services;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MedCareOS.Application.Appointments.Commands.ConfirmAppointment;

public class ConfirmAppointmentCommandHandler : IRequestHandler<ConfirmAppointmentCommand, ConfirmAppointmentResult>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IAppointmentLifecyclePlanner _lifecyclePlanner;
    private readonly IRiskSignalProcessor _riskSignalProcessor;
    private readonly IAiResolutionSupersedingService _resolutionSupersedingService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmAppointmentCommandHandler> _logger;

    public ConfirmAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IAppointmentLifecyclePlanner lifecyclePlanner,
        IRiskSignalProcessor riskSignalProcessor,
        IAiResolutionSupersedingService resolutionSupersedingService,
        IPublishEndpoint publishEndpoint,
        IUnitOfWork unitOfWork,
        ILogger<ConfirmAppointmentCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _lifecyclePlanner = lifecyclePlanner;
        _riskSignalProcessor = riskSignalProcessor;
        _resolutionSupersedingService = resolutionSupersedingService;
        _publishEndpoint = publishEndpoint;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ConfirmAppointmentResult> Handle(
        ConfirmAppointmentCommand request,
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
        var transition = appointment.RegisterPatientConfirmation(utcNow);

        if (transition.StateChanged)
        {
            var isEarly = transition.Outcome == ConfirmationTransitionOutcome.ConfirmedEarly;
            var signalKind = isEarly ? RiskSignalKind.ConfirmedEarly : RiskSignalKind.FinalConfirmed;
            var idempotencyKey = isEarly
                ? $"confirmed_early:{appointment.Id:N}"
                : $"final_confirmed:{appointment.Id:N}";
            var source = string.IsNullOrWhiteSpace(request.Source) ? "staff_manual" : request.Source.Trim();

            var metadataJson = JsonSerializer.Serialize(new
            {
                channel = request.Channel?.ToString(),
                notificationId = request.NotificationId,
                normalizedKeyword = request.NormalizedKeyword,
                previousState = transition.PreviousState.ToString(),
                newState = transition.NewState.ToString(),
                orchestrationSuppressed = appointment.OrchestrationSuppressed
            });

            await _riskSignalProcessor.RecordAsync(
                new RecordRiskSignalRequest(
                    AppointmentId: appointment.Id,
                    Kind: signalKind,
                    Source: source,
                    IdempotencyKey: idempotencyKey,
                    CorrelationId: correlationId,
                    OccurredAtUtc: utcNow,
                    MetadataJson: metadataJson),
                cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Confirmación repetida ignorada idempotente para cita {AppointmentId} (ConfirmationState={ConfirmationState}).",
                appointment.Id,
                appointment.ConfirmationState);
        }

        var cancelReason = transition.NewState == ConfirmationState.ConfirmedEarly
            ? "PatientConfirmedEarly"
            : "PatientFinalConfirmed";

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
                    Intent: PatientReplyIntent.Confirm,
                    Channel: request.Channel ?? NotificationChannel.Email,
                    ReceivedAtUtc: utcNow,
                    NormalizedKeyword: string.IsNullOrWhiteSpace(request.NormalizedKeyword)
                        ? "CONFIRM"
                        : request.NormalizedKeyword.Trim()),
                cancellationToken);
        }

        if (transition.StateChanged || cancelledActions > 0 || supersededTasks > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ConfirmAppointmentResult(
            AppointmentId: appointment.Id,
            Outcome: transition.Outcome,
            AppointmentStatus: appointment.Status,
            ConfirmationState: appointment.ConfirmationState,
            StateChanged: transition.StateChanged,
            WasIdempotentReplay: !transition.StateChanged,
            CancelledScheduledActionsCount: cancelledActions,
            SupersededHitlTasksCount: supersededTasks,
            EffectiveRiskScore: appointment.EffectiveRiskScore ?? appointment.CurrentBaseModelScore,
            EffectiveBand: appointment.EffectiveBand?.ToLabel() ?? appointment.RiskBand);
    }
}
