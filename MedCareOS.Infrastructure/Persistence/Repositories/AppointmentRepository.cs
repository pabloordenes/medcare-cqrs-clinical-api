using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class AppointmentRepository : IAppointmentRepository
{
    private readonly ApplicationDbContext _dbContext;
    
    public AppointmentRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAppointmentAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        await _dbContext.Appointments.AddAsync(appointment, cancellationToken);
        
    }

    public async Task<Appointment?> GetAppointmentByIdAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        return await _dbContext.Appointments.FirstOrDefaultAsync(x => x.Id == appointmentId, cancellationToken);
    }

    public async Task<Appointment?> GetActiveByScheduleBlockIdAsync(Guid scheduleBlockId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Appointments
            .FirstOrDefaultAsync(
                a => a.ScheduleBlockId == scheduleBlockId && a.Status != AppointmentStatus.Cancelled,
                cancellationToken);
    }

    public async Task<int> GetNoShowCountByPatientId(Guid patientId, CancellationToken cancellationToken)
    {
        return await _dbContext.Appointments.CountAsync(a => a.PatientId == patientId && a.Status == AppointmentStatus.NoShow, cancellationToken);
    }

    public async Task<bool> HasActiveAppointmentForBlockAsync(Guid scheduleBlockId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Appointments
            .AnyAsync(a => a.ScheduleBlockId == scheduleBlockId && a.Status != AppointmentStatus.Cancelled, cancellationToken);
    }

    public async Task<List<Appointment>> GetPastUnattendedAppointmentsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        return await (
            from app in _dbContext.Appointments
            join block in _dbContext.ScheduleBlocks on app.ScheduleBlockId equals block.Id
            where (app.Status == AppointmentStatus.Scheduled || app.Status == AppointmentStatus.Confirmed)
                  && block.EndTime < utcNow
            select app
        ).ToListAsync(cancellationToken);
    }

    public async Task<List<Appointment>> GetAppointmentsPendingScoringRetryAsync(
        DateTimeOffset utcNow,
        TimeSpan pendingStaleAge,
        int maxAttempts,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var staleCutoff = utcNow.Subtract(pendingStaleAge);
        var utcNowDateTime = utcNow.UtcDateTime;

        return await (
            from app in _dbContext.Appointments
            join block in _dbContext.ScheduleBlocks on app.ScheduleBlockId equals block.Id
            where (app.Status == AppointmentStatus.Scheduled || app.Status == AppointmentStatus.Confirmed)
                  && block.StartTime > utcNowDateTime
                  && (
                      ((app.ScoringStatus == ScoringStatus.Pending || app.NlpStatus == NlpStatus.Pending)
                          && app.CreatedAt <= staleCutoff
                          && (app.NextScoringRetryAt == null || app.NextScoringRetryAt <= utcNow))
                      ||
                      ((app.ScoringStatus == ScoringStatus.Failed || app.NlpStatus == NlpStatus.Failed)
                          && app.ScoringAttempts < maxAttempts
                          && (app.NextScoringRetryAt == null || app.NextScoringRetryAt <= utcNow))
                  )
            orderby app.NextScoringRetryAt ?? app.CreatedAt
            select app
        ).Take(batchSize).ToListAsync(cancellationToken);
    }
}