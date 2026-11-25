using MedCareOS.Application.Common.Options;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using MedCareOS.Domain.Services;
using Microsoft.Extensions.Options;

namespace MedCareOS.Application.Appointments.Lifecycle;

public class AppointmentLifecyclePlanner : IAppointmentLifecyclePlanner
{
    private readonly IAppointmentScheduledActionRepository _actionRepository;
    private readonly ITenantConfirmationPolicyRepository _policyRepository;
    private readonly ScheduledActionsOptions _options;

    public AppointmentLifecyclePlanner(
        IAppointmentScheduledActionRepository actionRepository,
        ITenantConfirmationPolicyRepository policyRepository,
        IOptions<ScheduledActionsOptions> options)
    {
        _actionRepository = actionRepository;
        _policyRepository = policyRepository;
        _options = options.Value;
    }

    public async Task<int> ScheduleForAppointmentAsync(
        Appointment appointment,
        ScheduleBlock scheduleBlock,
        Guid correlationId,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(scheduleBlock);

        if (appointment.Status != AppointmentStatus.Scheduled || appointment.IsPatientConfirmed)
            return 0;

        var now = utcNow ?? DateTimeOffset.UtcNow;
        return await PlanAndStageAsync(appointment, scheduleBlock, correlationId, planVersion: 1, now, cancellationToken);
    }

    public async Task<int> CancelPendingActionsAsync(
        Guid appointmentId,
        string reason,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var pending = await _actionRepository.GetPendingByAppointmentIdAsync(appointmentId, cancellationToken);
        if (pending.Count == 0)
            return 0;

        var now = utcNow ?? DateTimeOffset.UtcNow;
        foreach (var action in pending)
        {
            action.Cancel(reason, now);
        }

        return pending.Count;
    }

    public async Task<int> RecalculateForRescheduleAsync(
        Appointment appointment,
        ScheduleBlock newScheduleBlock,
        Guid correlationId,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(newScheduleBlock);

        var now = utcNow ?? DateTimeOffset.UtcNow;
        await CancelPendingActionsAsync(appointment.Id, "AppointmentRescheduled", now, cancellationToken);

        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow or AppointmentStatus.Finished or AppointmentStatus.InProgress ||
            appointment.IsPatientConfirmed ||
            appointment.ConfirmationState == ConfirmationState.DeclinedByPatient)
        {
            return 0;
        }

        var currentMaxVersion = await _actionRepository.GetMaxPlanVersionAsync(appointment.Id, cancellationToken);
        return await PlanAndStageAsync(appointment, newScheduleBlock, correlationId, currentMaxVersion + 1, now, cancellationToken);
    }

    private async Task<int> PlanAndStageAsync(
        Appointment appointment,
        ScheduleBlock scheduleBlock,
        Guid correlationId,
        int planVersion,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var policy = await _policyRepository.GetOrDefaultAsync(appointment.TenantId, cancellationToken);
        var startUtc = ToUtcOffset(scheduleBlock.StartTime);

        var planned = ConfirmationTimelinePlanner.Plan(startUtc, createdAtUtc, policy);
        if (planned.Count == 0)
            return 0;

        var maxAttempts = Math.Max(1, _options.MaxAttempts);
        var entities = planned.Select(p => AppointmentScheduledAction.Create(
            tenantId: appointment.TenantId,
            appointmentId: appointment.Id,
            kind: p.Kind,
            dueAt: p.DueAt,
            correlationId: correlationId,
            planVersion: planVersion,
            maxAttempts: maxAttempts,
            utcNow: createdAtUtc,
            metadataJson: p.IsCompressed ? "{\"compressed\":true}" : "{}")).ToList();

        await _actionRepository.AddRangeAsync(entities, cancellationToken);
        return entities.Count;
    }

    private static DateTimeOffset ToUtcOffset(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
