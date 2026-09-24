using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;

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
}