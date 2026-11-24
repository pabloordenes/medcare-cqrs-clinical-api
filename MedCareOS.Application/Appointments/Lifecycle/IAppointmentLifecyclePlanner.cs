using MedCareOS.Domain.Entities;

namespace MedCareOS.Application.Appointments.Lifecycle;

public interface IAppointmentLifecyclePlanner
{
    Task<int> ScheduleForAppointmentAsync(
        Appointment appointment,
        ScheduleBlock scheduleBlock,
        Guid correlationId,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default);

    Task<int> CancelPendingActionsAsync(
        Guid appointmentId,
        string reason,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default);

    Task<int> RecalculateForRescheduleAsync(
        Appointment appointment,
        ScheduleBlock newScheduleBlock,
        Guid correlationId,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default);
}
