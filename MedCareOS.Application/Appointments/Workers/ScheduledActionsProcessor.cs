using System.Text.Json;
using MedCareOS.Application.Common.Interfaces.Notifications;
using MedCareOS.Application.Common.Options;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Application.Notifications.Templates;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
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
                        notificationRepository,
                        notificationOutbox,
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
                        notificationOutbox,
                        now,
                        cancellationToken);
                    break;

                case ScheduledActionKind.ConfirmationDeadline:
                    ExecuteConfirmationDeadline(action, appointment, now);
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
        INotificationRepository notificationRepository,
        INotificationOutboxService notificationOutbox,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previousReminder = await notificationRepository.GetLatestByAppointmentAndTypeAsync(
            appointment.Id,
            NotificationType.Reminder,
            cancellationToken);

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
            previousChannel = previousReminder?.Channel.ToString()
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
        INotificationOutboxService notificationOutbox,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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

    private static void ExecuteConfirmationDeadline(
        AppointmentScheduledAction action,
        Appointment appointment,
        DateTimeOffset now)
    {
        appointment.MarkUnresponsive(now);

        action.MarkCompleted(now, JsonSerializer.Serialize(new
        {
            confirmationState = appointment.ConfirmationState.ToString(),
            deadlineMissedAt = appointment.DeadlineMissedAt
        }));
    }

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
