using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IAppointmentRepository
{
    Task AddAppointmentAsync(Appointment appointment, CancellationToken cancellationToken = default);
    Task<Appointment?> GetAppointmentByIdAsync(Guid appointmentId, CancellationToken cancellationToken = default);
    Task<int> GetNoShowCountByPatientId(Guid patientId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveAppointmentForBlockAsync(Guid scheduleBlockId, CancellationToken cancellationToken = default);
    Task<List<Appointment>> GetPastUnattendedAppointmentsAsync(DateTime utcNow, CancellationToken cancellationToken = default);
    Task<List<Appointment>> GetAppointmentsPendingScoringRetryAsync(
        DateTimeOffset utcNow,
        TimeSpan pendingStaleAge,
        int maxAttempts,
        int batchSize,
        CancellationToken cancellationToken = default);
}