using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class AppointmentRescheduleHistoryRepository : IAppointmentRescheduleHistoryRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AppointmentRescheduleHistoryRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AppointmentRescheduleHistory history, CancellationToken cancellationToken = default)
        => await _dbContext.AppointmentRescheduleHistories.AddAsync(history, cancellationToken);

    public async Task<List<AppointmentRescheduleHistory>> GetByAppointmentIdAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
        => await _dbContext.AppointmentRescheduleHistories
            .AsNoTracking()
            .Where(h => h.AppointmentId == appointmentId)
            .OrderByDescending(h => h.RescheduledAtUtc)
            .ToListAsync(cancellationToken);
}
