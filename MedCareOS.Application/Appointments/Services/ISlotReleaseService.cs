using MedCareOS.Application.Appointments.Events;
using MedCareOS.Domain.Entities;

namespace MedCareOS.Application.Appointments.Services;

public sealed record SlotReleasePreparationResult(
    bool SuppressedByOccupiedOverbook,
    SlotReleased? SlotReleasedEvent = null,
    OverbookOpened? OverbookOpenedEvent = null);

public interface ISlotReleaseService
{
    Task<SlotReleasePreparationResult> PrepareSlotVacatedAsync(
        Appointment vacatedAppointment,
        ScheduleBlock? vacatedBlock,
        string triggerReason,
        Guid correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
