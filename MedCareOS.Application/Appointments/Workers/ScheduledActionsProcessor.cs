using System.Text.Json;
using MedCareOS.Application.Appointments.Risk;
using MedCareOS.Application.Common.Exceptions;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.Appointments.Workers;

public class ScheduledActionsProcessor : IScheduledActionsProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScheduledActionsOptions _options;
    private readonly NotificationOptions _notificationOptions;
    private readonly ILogger<ScheduledActionsProcessor> _logger;

    public ScheduledActionsProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<ScheduledActionsOptions> options,
        IOptions<NotificationOptions> notificationOptions,
        ILogger<ScheduledActionsProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _notificationOptions = notificationOptions.Value;
        _logger = logger;
    }

    public async Task<ScheduledActionsBatchResult> ProcessDueBatchAsync(
        string? workerId = null,
        DateTimeOffset? utcNowOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWorkerId = string.IsNullOrWhiteSpace(workerId)
            ? $"{Environment.MachineName}:{Guid.NewGuid():N}"
            : workerId.Trim();

        var claimNow = utcNowOverride ?? DateTimeOffset.UtcNow;
        var leaseDuration = TimeSpan.FromSeconds(Math.Max(10, _options.LeaseDurationSeconds));
        var batchSize = Math.Max(1, _options.BatchSize);

        List<AppointmentScheduledAction> claimed;
        using (var claimScope = _scopeFactory.CreateScope())
        {
            var actionRepo = claimScope.ServiceProvider.GetRequiredService<IAppointmentScheduledActionRepository>();
            claimed = await actionRepo.ClaimDueBatchAsync(
                effectiveWorkerId,
                claimNow,
                leaseDuration,
                batchSize,
                cancellationToken);
        }

        if (claimed.Count == 0)
            return new ScheduledActionsBatchResult(0, 0, 0, 0, 0);

        int completed = 0;
        int skipped = 0;
        int retried = 0;
        int failed = 0;

        foreach (var item in claimed)
        {
            var outcome = await ProcessSingleClaimedActionAsync(
                item,
                effectiveWorkerId,
                utcNowOverride,
                cancellationToken);

            switch (outcome)
            {
                case ItemOutcome.Completed:
                    completed++;
                    break;
                case ItemOutcome.Skipped:
                    skipped++;
                    break;
                case ItemOutcome.Retried:
                    retried++;
                    break;
                case ItemOutcome.Failed:
                    failed++;
                    break;
            }
        }

        return new ScheduledActionsBatchResult(claimed.Count, completed, skipped, retried, failed);
    }

    private async Task<ItemOutcome> ProcessSingleClaimedActionAsync(
        AppointmentScheduledAction claimedItem,
        string effectiveWorkerId,
        DateTimeOffset? utcNowOverride,
        CancellationToken cancellationToken)
    {
        var now = utcNowOverride ?? DateTimeOffset.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var actionRepository = scope.ServiceProvider.GetRequiredService<IAppointmentScheduledActionRepository>();
            var appointmentRepository = scope.ServiceProvider.GetRequiredService<IAppointmentRepository>();
            var patientRepository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
            var scheduleBlockRepository = scope.ServiceProvider.GetRequiredService<IScheduleBlockRepository>();
            var staffRepository = scope.ServiceProvider.GetRequiredService<IStaffRepository>();
            var policyRepository = scope.ServiceProvider.GetRequiredService<ITenantConfirmationPolicyRepository>();
            var notificationRepository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var notificationOutbox = scope.ServiceProvider.GetRequiredService<INotificationOutboxService>();
            var riskSignalProcessor = scope.ServiceProvider.GetRequiredService<IRiskSignalProcessor>();
            var riskSignalRepository = scope.ServiceProvider.GetRequiredService<IRiskSignalRepository>();
            var rescoreService = scope.ServiceProvider.GetRequiredService<IAppointmentRescoreService>();
            var anomalyPublisher = scope.ServiceProvider.GetRequiredService<IOmroAnomalyPublisher>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var action = await actionRepository.GetByIdAsync(claimedItem.Id, cancellationToken);
            if (action is null || !action.IsOwnedBy(effectiveWorkerId, claimedItem.LockToken))
            {
                _logger.LogWarning(
                    "Acción programada {ActionId} perdió ownership (WorkerId={WorkerId}). Se omite commit.",
                    claimedItem.Id,
                    effectiveWorkerId);
                return ItemOutcome.None;
            }

            if (action.Attempts > action.MaxAttempts)
            {
                action.MarkFailed("Máximo de intentos excedido tras expiración de lease.", now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return ItemOutcome.Failed;
            }

            var appointment = await appointmentRepository.GetAppointmentByIdAsync(action.AppointmentId, cancellationToken);
            if (appointment is null)
            {
                action.MarkSkipped("Cita asociada no encontrada.", now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return ItemOutcome.Skipped;
            }

            if (appointment.Status is AppointmentStatus.Cancelled
                or AppointmentStatus.NoShow
                or AppointmentStatus.Finished
                or AppointmentStatus.InProgress)
            {
                action.MarkSkipped($"Cita en estado clínico '{appointment.Status}'.", now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return ItemOutcome.Skipped;
            }

            if (appointment.IsPatientConfirmed || appointment.ConfirmationState == ConfirmationState.DeclinedByPatient)
            {
                action.MarkSkipped($"Cita en estado de confirmación '{appointment.ConfirmationState}'.", now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return ItemOutcome.Skipped;
            }

            var block = await scheduleBlockRepository.GetByIdAsync(appointment.ScheduleBlockId, cancellationToken);
            if (block is null || block.StartTime <= now.UtcDateTime)
            {
                action.MarkSkipped("El bloque horario no existe o la cita ya inició.", now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return ItemOutcome.Skipped;
            }

            var patient = await patientRepository.GetPatientByIdAsync(appointment.PatientId, cancellationToken);
            var staff = await staffRepository.GetByIdAsync(block.StaffId, cancellationToken);
            if (patient is null || staff is null)
            {
                action.MarkSkipped("Paciente o profesional no encontrado.", now);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return ItemOutcome.Skipped;
            }

            var policy = await policyRepository.GetOrDefaultAsync(appointment.TenantId, cancellationToken);

            switch (action.Kind)
            {
                case ScheduledActionKind.Reminder1:
                    await ExecuteReminder1Async(
                        action,
                        appointment,
                        patient,
                        block,
                        staff,
                        policy,
                        notificationOutbox,
                        now,
                        cancellationToken);
                    break;

                case ScheduledActionKind.Reminder2AndRescore:
                    await ExecuteReminder2Async(
                        action,
                        appointment,
                        patient,
                        block,
                        staff,
                        policy,
                        appointmentRepository,
                        notificationRepository,
                        notificationOutbox,
                        riskSignalProcessor,
                        riskSignalRepository,
                        rescoreService,
                        anomalyPublisher,
                        now,
                        cancellationToken);
                    break;

                case ScheduledActionKind.FinalConfirmation:
                    await ExecuteFinalConfirmationAsync(
                        action,
                        appointment,
                        patient,
                        block,
                        staff,
                        policy,
                        actionRepository,
                        notificationRepository,
                        notificationOutbox,
                        riskSignalProcessor,
                        now,
                        cancellationToken);
                    break;

                case ScheduledActionKind.ConfirmationDeadline:
                    await ExecuteConfirmationDeadlineAsync(
                        action,
                        appointment,
                        patient,
                        block,
                        staff,
                        policy,
                        notificationRepository,
                        riskSignalProcessor,
                        anomalyPublisher,
                        scope.ServiceProvider.GetService<IOverbookShadowDecisionRepository>(),
                        now,
                        cancellationToken);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(action.Kind), action.Kind, "Tipo de acción programada no soportado.");
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return ItemOutcome.Completed;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(
                ex,
                "Fencing conflict al completar acción {ActionId} (WorkerId={WorkerId}). Otro worker reclamó el lease.",
                claimedItem.Id,
                effectiveWorkerId);
            return ItemOutcome.None;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error al ejecutar acción programada {ActionId} (Kind={Kind}, AppointmentId={AppointmentId}).",
                claimedItem.Id,
                claimedItem.Kind,
                claimedItem.AppointmentId);

            return await TryScheduleRetryAsync(
                claimedItem,
                effectiveWorkerId,
                ex.Message,
                now,
                cancellationToken);
        }
    }

    private async Task ExecuteReminder1Async(
        AppointmentScheduledAction action,
        Appointment appointment,
        Patient patient,
        ScheduleBlock block,
        Staff staff,
        TenantConfirmationPolicy policy,
        INotificationOutboxService notificationOutbox,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var targetChannel = patient.ResolvePrimaryChannel(policy.PrimaryChannelStrategy);
        var parameters = BuildBaseNotificationParameters(patient, block, staff, policy);

        var enqueueResult = await notificationOutbox.EnqueueAsync(
            new EnqueueNotificationRequest(
                TenantId: appointment.TenantId,
                AppointmentId: appointment.Id,
                Patient: patient,
                Type: NotificationType.Reminder,
                TemplateKey: DefaultNotificationTemplateCatalog.Keys.Reminder,
                Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                ScheduledFor: action.DueAt,
                Parameters: parameters,
                CorrelationId: action.CorrelationId,
                ChannelOverride: targetChannel,
                ForceImmediateDispatch: true),
            cancellationToken);

        action.MarkCompleted(now, JsonSerializer.Serialize(new
        {
            outcome = enqueueResult.Outcome.ToString(),
            notificationId = enqueueResult.NotificationId,
            channel = enqueueResult.Channel?.ToString()
        }));
    }

    private async Task ExecuteReminder2Async(
        AppointmentScheduledAction action,
        Appointment appointment,
        Patient patient,
        ScheduleBlock block,
        Staff staff,
        TenantConfirmationPolicy policy,
        IAppointmentRepository appointmentRepository,
        INotificationRepository notificationRepository,
        INotificationOutboxService notificationOutbox,
        IRiskSignalProcessor riskSignalProcessor,
        IRiskSignalRepository riskSignalRepository,
        IAppointmentRescoreService rescoreService,
        IOmroAnomalyPublisher anomalyPublisher,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var bandBeforeReminder2 = appointment.EffectiveBand;

        var previousReminder = await notificationRepository.GetLatestByAppointmentAndTypeAsync(
            appointment.Id,
            NotificationType.Reminder,
            cancellationToken);

        await EvaluateUnansweredNotificationAsync(
            previousReminder,
            appointment,
            policy,
            action,
            now,
            riskSignalProcessor,
            suppressBandEscalationEvent: true,
            cancellationToken);

        bool rescored = false;
        bool rescoreSkipped = false;
        decimal? rescoredModelScore = null;
        string? rescoreError = null;

        if (policy.EnableRescoreAtReminder2)
        {
            try
            {
                var predictResult = await rescoreService.PredictForAppointmentAsync(
                    appointment,
                    patient,
                    block,
                    staff,
                    now,
                    cancellationToken);

                rescoredModelScore = predictResult.RiskScore;

                var daysOfWaiting = Math.Max(0, (block.StartTime.Date - now.UtcDateTime.Date).Days);
                var previousAbsences = await appointmentRepository.GetNoShowCountByPatientId(patient.Id, cancellationToken);

                await riskSignalProcessor.RecordAsync(
                    new RecordRiskSignalRequest(
                        AppointmentId: appointment.Id,
                        Kind: RiskSignalKind.ModelRescored,
                        Source: $"scheduled_action:{action.Kind}",
                        IdempotencyKey: $"model_rescored:{action.Id}",
                        CorrelationId: action.CorrelationId,
                        OccurredAtUtc: now,
                        WeightOverride: 0m,
                        MetadataJson: JsonSerializer.Serialize(new
                        {
                            previousModelScore = appointment.ModelRiskScore,
                            latestModelScore = predictResult.RiskScore,
                            daysOfWaiting,
                            previousAbsences,
                            channel = patient.ToPredictReminderChannel()
                        }),
                        SuppressBandEscalationEvent: true),
                    cancellationToken);

                var allSignals = await riskSignalRepository.GetByAppointmentIdAsync(appointment.Id, cancellationToken);
                appointment.ApplyModelRescore(predictResult.RiskScore, allSignals, policy, now, bandBeforeReminder2);
                rescored = true;
            }
            catch (Exception ex) when (IsTransientAiFailure(ex))
            {
                if (action.Attempts < action.MaxAttempts)
                    throw;

                rescoreSkipped = true;
                rescoreError = ex.Message;
                _logger.LogWarning(
                    ex,
                    "Re-score T-48h omitido por degradación graciosa tras agotar intentos ({Attempts}/{MaxAttempts}) para cita {AppointmentId}.",
                    action.Attempts,
                    action.MaxAttempts,
                    appointment.Id);
            }
            catch (AiServicePermanentException ex)
            {
                rescoreSkipped = true;
                rescoreError = ex.Message;
                _logger.LogError(
                    ex,
                    "Re-score T-48h omitido por error permanente de IA para cita {AppointmentId}. Se continúa con Reminder 2.",
                    appointment.Id);
            }
        }

        bool riskEscalatedPublished = false;
        if (RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, bandBeforeReminder2, appointment.EffectiveBand))
        {
            riskEscalatedPublished = await anomalyPublisher.PublishAsync(
                new PublishAnomalyContext(
                    Appointment: appointment,
                    AnomalyType: RiskThresholds.RiskEscalatedAnomalyType,
                    CorrelationId: action.CorrelationId,
                    OccurredAtUtc: now,
                    TriggerSource: $"scheduled_action:{action.Kind}",
                    PreviousBand: bandBeforeReminder2,
                    Patient: patient,
                    ScheduleBlock: block,
                    Staff: staff),
                cancellationToken);
        }

        var targetChannel = previousReminder is not null
            ? patient.ResolveAlternateChannel(previousReminder.Channel, policy.AlternateChannelStrategy)
            : patient.ResolvePrimaryChannel(policy.PrimaryChannelStrategy);

        var parameters = BuildBaseNotificationParameters(patient, block, staff, policy);

        var enqueueResult = await notificationOutbox.EnqueueAsync(
            new EnqueueNotificationRequest(
                TenantId: appointment.TenantId,
                AppointmentId: appointment.Id,
                Patient: patient,
                Type: NotificationType.Reminder,
                TemplateKey: DefaultNotificationTemplateCatalog.Keys.Reminder,
                Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                ScheduledFor: action.DueAt,
                Parameters: parameters,
                CorrelationId: action.CorrelationId,
                ChannelOverride: targetChannel,
                ForceImmediateDispatch: true),
            cancellationToken);

        action.MarkCompleted(now, JsonSerializer.Serialize(new
        {
            outcome = enqueueResult.Outcome.ToString(),
            notificationId = enqueueResult.NotificationId,
            channel = enqueueResult.Channel?.ToString(),
            previousChannel = previousReminder?.Channel.ToString(),
            rescored,
            rescoredModelScore,
            rescoreSkipped,
            rescoreError,
            effectiveScore = appointment.EffectiveRiskScore,
            effectiveBand = appointment.EffectiveBand?.ToString(),
            riskEscalatedPublished
        }));
    }

    private async Task ExecuteFinalConfirmationAsync(
        AppointmentScheduledAction action,
        Appointment appointment,
        Patient patient,
        ScheduleBlock block,
        Staff staff,
        TenantConfirmationPolicy policy,
        IAppointmentScheduledActionRepository actionRepository,
        INotificationRepository notificationRepository,
        INotificationOutboxService notificationOutbox,
        IRiskSignalProcessor riskSignalProcessor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previousReminder = await notificationRepository.GetLatestByAppointmentAndTypeAsync(
            appointment.Id,
            NotificationType.Reminder,
            cancellationToken);

        await EvaluateUnansweredNotificationAsync(
            previousReminder,
            appointment,
            policy,
            action,
            now,
            riskSignalProcessor,
            suppressBandEscalationEvent: false,
            cancellationToken);

        var allActions = await actionRepository.GetByAppointmentIdAsync(appointment.Id, cancellationToken);
        var deadlineAction = allActions
            .Where(a => a.Kind == ScheduledActionKind.ConfirmationDeadline &&
                        a.Status != ScheduledActionStatus.Cancelled)
            .OrderByDescending(a => a.PlanVersion)
            .FirstOrDefault();

        var deadlineUtc = deadlineAction?.DueAt ?? new DateTimeOffset(
            block.StartTime.Kind == DateTimeKind.Utc
                ? block.StartTime
                : DateTime.SpecifyKind(block.StartTime, DateTimeKind.Utc),
            TimeSpan.Zero);

        appointment.MarkFinalConfirmationRequested(now);

        var primaryChannel = patient.ResolvePrimaryChannel(policy.PrimaryChannelStrategy);
        var parameters = BuildBaseNotificationParameters(patient, block, staff, policy);
        parameters["deadline"] = NotificationFormattingHelper.FormatDeadline(deadlineUtc, policy.TimeZoneId);

        var enqueueResult = await notificationOutbox.EnqueueAsync(
            new EnqueueNotificationRequest(
                TenantId: appointment.TenantId,
                AppointmentId: appointment.Id,
                Patient: patient,
                Type: NotificationType.FinalConfirmation,
                TemplateKey: DefaultNotificationTemplateCatalog.Keys.FinalConfirmation,
                Locale: DefaultNotificationTemplateCatalog.DefaultLocale,
                ScheduledFor: action.DueAt,
                Parameters: parameters,
                CorrelationId: action.CorrelationId,
                ChannelOverride: primaryChannel,
                ForceImmediateDispatch: true),
            cancellationToken);

        action.MarkCompleted(now, JsonSerializer.Serialize(new
        {
            outcome = enqueueResult.Outcome.ToString(),
            notificationId = enqueueResult.NotificationId,
            channel = enqueueResult.Channel?.ToString(),
            deadlineUtc
        }));
    }

    private static async Task ExecuteConfirmationDeadlineAsync(
        AppointmentScheduledAction action,
        Appointment appointment,
        Patient patient,
        ScheduleBlock block,
        Staff staff,
        TenantConfirmationPolicy policy,
        INotificationRepository notificationRepository,
        IRiskSignalProcessor riskSignalProcessor,
        IOmroAnomalyPublisher anomalyPublisher,
        IOverbookShadowDecisionRepository? shadowDecisionRepository,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previousFinal = await notificationRepository.GetLatestByAppointmentAndTypeAsync(
            appointment.Id,
            NotificationType.FinalConfirmation,
            cancellationToken);

        await EvaluateUnansweredNotificationAsync(
            previousFinal,
            appointment,
            policy,
            action,
            now,
            riskSignalProcessor,
            suppressBandEscalationEvent: true,
            cancellationToken);

        var transition = appointment.MarkUnresponsive(now);

        if (shadowDecisionRepository is not null &&
            transition.StateChanged &&
            !policy.AutoOverbookEnabled)
        {
            var effectiveScore = appointment.EffectiveRiskScore ?? appointment.CurrentBaseModelScore ?? 0m;
            if (effectiveScore >= policy.HitlOverbookMinEffectiveScore &&
                !await shadowDecisionRepository.ExistsForAppointmentAsync(appointment.Id, cancellationToken))
            {
                var delivered = previousFinal?.Status is NotificationStatus.Delivered or NotificationStatus.Read;
                var exclusion = Domain.Services.ClinicalExclusionEvaluator.Evaluate(patient, staff, appointment, policy, now);
                var wouldAutoOverbook = delivered && !exclusion.IsExcluded && effectiveScore >= policy.AutoOverbookMinEffectiveScore;
                var decisionLabel = !delivered
                    ? "ShadowSuppressedDelivery"
                    : exclusion.IsExcluded
                        ? "ShadowClinicalExclusion"
                        : wouldAutoOverbook
                            ? "ShadowWouldAutoOverbook"
                            : "ShadowHitlProposal";

                await shadowDecisionRepository.AddAsync(
                    OverbookShadowDecision.Create(
                        tenantId: appointment.TenantId,
                        appointmentId: appointment.Id,
                        scheduleBlockId: appointment.ScheduleBlockId,
                        effectiveRiskScore: effectiveScore,
                        thresholdUsed: policy.AutoOverbookMinEffectiveScore,
                        decision: decisionLabel,
                        wouldHaveOverbooked: wouldAutoOverbook,
                        finalNotificationDelivered: delivered,
                        hasClinicalExclusion: exclusion.IsExcluded,
                        clinicalExclusionCode: exclusion.Code,
                        isSynthetic: false,
                        evaluatedAtUtc: now,
                        correlationId: action.CorrelationId),
                    cancellationToken);
            }
        }

        bool deadlineMissedPublished = false;
        if (RiskEscalationPolicy.ShouldEmitConfirmationDeadlineMissed(appointment, transition.StateChanged))
        {
            deadlineMissedPublished = await anomalyPublisher.PublishAsync(
                new PublishAnomalyContext(
                    Appointment: appointment,
                    AnomalyType: RiskThresholds.ConfirmationDeadlineMissedAnomalyType,
                    CorrelationId: action.CorrelationId,
                    OccurredAtUtc: now,
                    TriggerSource: $"scheduled_action:{action.Kind}",
                    PreviousBand: appointment.EffectiveBand,
                    Patient: patient,
                    ScheduleBlock: block,
                    Staff: staff),
                cancellationToken);
        }

        action.MarkCompleted(now, JsonSerializer.Serialize(new
        {
            confirmationState = appointment.ConfirmationState.ToString(),
            deadlineMissedAt = appointment.DeadlineMissedAt,
            effectiveScore = appointment.EffectiveRiskScore,
            effectiveBand = appointment.EffectiveBand?.ToString(),
            deadlineMissedPublished
        }));
    }

    private static async Task EvaluateUnansweredNotificationAsync(
        Notification? previousNotification,
        Appointment appointment,
        TenantConfirmationPolicy policy,
        AppointmentScheduledAction action,
        DateTimeOffset now,
        IRiskSignalProcessor riskSignalProcessor,
        bool suppressBandEscalationEvent,
        CancellationToken cancellationToken)
    {
        if (previousNotification is null)
            return;

        bool isConsideredDelivered =
            previousNotification.Status is NotificationStatus.Delivered or NotificationStatus.Read ||
            (policy.CountSentAsDeliveredForUnanswered && previousNotification.Status == NotificationStatus.Sent);

        if (!isConsideredDelivered)
            return;

        if (appointment.IsPatientConfirmed || appointment.ConfirmationState == ConfirmationState.DeclinedByPatient)
            return;

        await riskSignalProcessor.RecordAsync(
            new RecordRiskSignalRequest(
                AppointmentId: appointment.Id,
                Kind: RiskSignalKind.ReminderUnanswered,
                Source: $"scheduled_action:{action.Kind}",
                IdempotencyKey: $"reminder_unanswered:{previousNotification.Id}",
                CorrelationId: action.CorrelationId,
                OccurredAtUtc: now,
                MetadataJson: JsonSerializer.Serialize(new
                {
                    notificationId = previousNotification.Id,
                    notificationType = previousNotification.Type.ToString(),
                    notificationStatus = previousNotification.Status.ToString(),
                    channel = previousNotification.Channel.ToString()
                }),
                SuppressBandEscalationEvent: suppressBandEscalationEvent),
            cancellationToken);
    }

    private static bool IsTransientAiFailure(Exception ex) =>
        ex is AiServiceTransientException or HttpRequestException;

    private async Task<ItemOutcome> TryScheduleRetryAsync(
        AppointmentScheduledAction claimedItem,
        string effectiveWorkerId,
        string errorMessage,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            using var retryScope = _scopeFactory.CreateScope();
            var actionRepository = retryScope.ServiceProvider.GetRequiredService<IAppointmentScheduledActionRepository>();
            var unitOfWork = retryScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var action = await actionRepository.GetByIdAsync(claimedItem.Id, cancellationToken);
            if (action is null || !action.IsOwnedBy(effectiveWorkerId, claimedItem.LockToken))
                return ItemOutcome.None;

            var retryAt = now.Add(_options.ComputeBackoff(action.Attempts));
            action.ScheduleRetry(retryAt, errorMessage, now);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return action.Status == ScheduledActionStatus.Failed ? ItemOutcome.Failed : ItemOutcome.Retried;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ItemOutcome.None;
        }
    }

    private Dictionary<string, string> BuildBaseNotificationParameters(
        Patient patient,
        ScheduleBlock block,
        Staff staff,
        TenantConfirmationPolicy policy)
    {
        var (appointmentDate, appointmentTime) = NotificationFormattingHelper.FormatAppointmentDateTime(
            block.StartTime,
            policy.TimeZoneId);
        var specialtyDisplay = NotificationFormattingHelper.ResolveSpecialtyDisplay(
            staff.Specialty,
            _notificationOptions.IncludeSpecialtyInMessages,
            _notificationOptions.SensitiveSpecialtiesExcluded);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["patientFirstName"] = patient.FirstName,
            ["clinicName"] = _notificationOptions.ClinicName,
            ["appointmentDate"] = appointmentDate,
            ["appointmentTime"] = appointmentTime,
            ["specialtyDisplay"] = specialtyDisplay
        };
    }

    private enum ItemOutcome
    {
        None,
        Completed,
        Skipped,
        Retried,
        Failed
    }
}
