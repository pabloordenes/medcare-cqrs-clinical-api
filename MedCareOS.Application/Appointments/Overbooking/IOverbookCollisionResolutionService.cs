using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Services;

namespace MedCareOS.Application.Appointments.Overbooking;

public interface IOverbookCollisionResolutionService
{
    Task HandleLateConfirmationAsync(
        Guid originalAppointmentId,
        DateTimeOffset confirmedAtUtc,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    Task<OverbookPriorityDecision?> EvaluateDoubleCheckInAsync(
        Appointment checkedInAppointment,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
