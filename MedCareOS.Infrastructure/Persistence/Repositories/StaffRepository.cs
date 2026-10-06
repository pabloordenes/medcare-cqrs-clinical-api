using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class StaffRepository : IStaffRepository
{
    private readonly ApplicationDbContext _dbContext;

    public StaffRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Staff?> FindByRutAsync(string staffRut, CancellationToken cancellationToken)
    {
        return await _dbContext.Staffs.FirstOrDefaultAsync(x => x.Rut == staffRut, cancellationToken);
    }

    public void AddStaff(Staff staff)
    {
        _dbContext.Staffs.Add(staff);
    }
    
    public async Task<Staff?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Staffs.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }
    
    public async Task<Staff?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Staffs.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}