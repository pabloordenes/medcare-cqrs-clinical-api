using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Services;

public sealed record PlannedScheduledAction(
    ScheduledActionKind Kind,
    DateTimeOffset DueAt,
    bool IsCompressed);

public static class ConfirmationTimelinePlanner
{
    private static readonly TimeSpan CompressedFinalConfirmationMaxDelay = TimeSpan.FromHours(2);
    private static readonly TimeSpan CompressedFinalConfirmationMinDeadlineMargin = TimeSpan.FromMinutes(30);

    public static IReadOnlyList<PlannedScheduledAction> Plan(
        DateTimeOffset appointmentStartUtc,
        DateTimeOffset createdAtUtc,
        TenantConfirmationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var start = appointmentStartUtc.ToUniversalTime();
        var created = createdAtUtc.ToUniversalTime();

        if (start <= created)
            return [];

        var lead = start - created;
        var tR1 = start - policy.Reminder1Offset;
        var tR2 = start - policy.Reminder2Offset;
        var tFC = start - policy.FinalConfirmationOffset;
        var tDL = start - policy.DeadlineOffset;
        var earliestAllowed = created + policy.MinimumDelayAfterCreation;

        // Cita del mismo día o creada ya vencido el deadline: nunca disparar ConfirmationDeadline retroactivo.
        if (tDL <= created)
        {
            if (policy.SameDayReminderMinLead <= TimeSpan.Zero || lead < policy.SameDayReminderMinLead)
                return [];

            var candidate = created + TimeSpan.FromTicks(lead.Ticks / 3);
            if (earliestAllowed < start && candidate < earliestAllowed)
            {
                candidate = earliestAllowed;
            }

            if (candidate >= start)
                return [];

            var minBound = earliestAllowed < start ? earliestAllowed : created.AddSeconds(1);
            var adjusted = AdjustOutOfQuietHoursIfWithinWindow(candidate, policy, minBound, start);

            return [new PlannedScheduledAction(ScheduledActionKind.FinalConfirmation, adjusted, IsCompressed: true)];
        }

        var actions = new List<PlannedScheduledAction>(4);
        DateTimeOffset? previousDue = null;

        if (tR1 > earliestAllowed && tR1 < tR2)
        {
            var r1Due = AdjustOutOfQuietHoursIfWithinWindow(tR1, policy, earliestAllowed, tR2);
            actions.Add(new PlannedScheduledAction(ScheduledActionKind.Reminder1, r1Due, IsCompressed: false));
            previousDue = r1Due;
        }

        var r2Min = previousDue ?? earliestAllowed;
        if (tR2 > r2Min && tR2 < tFC)
        {
            var r2Due = AdjustOutOfQuietHoursIfWithinWindow(tR2, policy, r2Min, tFC);
            actions.Add(new PlannedScheduledAction(ScheduledActionKind.Reminder2AndRescore, r2Due, IsCompressed: false));
            previousDue = r2Due;
        }

        var fcMin = previousDue ?? earliestAllowed;
        if (tFC > fcMin && tFC < tDL)
        {
            var fcDue = AdjustOutOfQuietHoursIfWithinWindow(tFC, policy, fcMin, tDL);
            actions.Add(new PlannedScheduledAction(ScheduledActionKind.FinalConfirmation, fcDue, IsCompressed: false));
        }
        else if (tFC <= earliestAllowed)
        {
            var marginToDeadline = tDL - created;
            if (marginToDeadline > CompressedFinalConfirmationMinDeadlineMargin)
            {
                var halfMargin = TimeSpan.FromTicks(marginToDeadline.Ticks / 2);
                var delay = halfMargin <= CompressedFinalConfirmationMaxDelay
                    ? halfMargin
                    : CompressedFinalConfirmationMaxDelay;
                var candidate = created + delay;
                var compressedMin = previousDue ?? created.AddSeconds(1);

                if (candidate > compressedMin && candidate < tDL)
                {
                    var fcDue = AdjustOutOfQuietHoursIfWithinWindow(candidate, policy, compressedMin, tDL);
                    actions.Add(new PlannedScheduledAction(ScheduledActionKind.FinalConfirmation, fcDue, IsCompressed: true));
                }
            }
        }

        // El deadline siempre se respeta exactamente en T - DeadlineOffset (no se desplaza por quiet hours).
        actions.Add(new PlannedScheduledAction(ScheduledActionKind.ConfirmationDeadline, tDL, IsCompressed: false));

        return actions;
    }

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var candidate = string.IsNullOrWhiteSpace(timeZoneId) ? "America/Santiago" : timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(candidate);
        }
        catch (TimeZoneNotFoundException)
        {
            if (string.Equals(candidate, "America/Santiago", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById("Pacific SA Standard Time");
                }
                catch (TimeZoneNotFoundException)
                {
                    return TimeZoneInfo.Utc;
                }
            }

            return TimeZoneInfo.Utc;
        }
    }

    private static DateTimeOffset AdjustOutOfQuietHoursIfWithinWindow(
        DateTimeOffset candidateUtc,
        TenantConfirmationPolicy policy,
        DateTimeOffset minInclusiveOrAllowed,
        DateTimeOffset maxExclusive)
    {
        if (!policy.QuietHoursStartLocal.HasValue || !policy.QuietHoursEndLocal.HasValue)
            return candidateUtc;

        var startQuiet = policy.QuietHoursStartLocal.Value;
        var endQuiet = policy.QuietHoursEndLocal.Value;
        var tz = ResolveTimeZone(policy.TimeZoneId);

        var localDateTime = TimeZoneInfo.ConvertTimeFromUtc(candidateUtc.UtcDateTime, tz);
        var localTime = TimeOnly.FromDateTime(localDateTime);
        var localDate = DateOnly.FromDateTime(localDateTime);

        bool isOvernight = startQuiet > endQuiet;
        bool inQuietHours = isOvernight
            ? (localTime >= startQuiet || localTime < endQuiet)
            : (localTime >= startQuiet && localTime < endQuiet);

        if (!inQuietHours)
            return candidateUtc;

        var targetDate = isOvernight && localTime >= startQuiet
            ? localDate.AddDays(1)
            : localDate;

        var targetLocal = targetDate.ToDateTime(endQuiet, DateTimeKind.Unspecified);
        while (tz.IsInvalidTime(targetLocal))
        {
            targetLocal = targetLocal.AddHours(1);
        }

        var shiftedUtc = new DateTimeOffset(targetLocal, tz.GetUtcOffset(targetLocal)).ToUniversalTime();

        return shiftedUtc >= minInclusiveOrAllowed && shiftedUtc < maxExclusive
            ? shiftedUtc
            : candidateUtc;
    }
}
