using MedCareOS.Application.Common.Interfaces;
using MedCareOS.Domain.Entities;

namespace MedCareOS.Application.Appointments.Risk;

public interface IAppointmentRescoreService
{
    Task<PredictResponseDto> PredictForAppointmentAsync(
        Appointment appointment,
        Patient patient,
        ScheduleBlock scheduleBlock,
        Staff staff,
        DateTimeOffset referenceUtc,
        CancellationToken cancellationToken = default);
}
