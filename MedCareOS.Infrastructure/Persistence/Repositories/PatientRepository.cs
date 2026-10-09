using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class PatientRepository : IPatientRepository
{
    private readonly ApplicationDbContext _dbContext;
    
    public PatientRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Patient?> GetPatientByIdAsync(Guid patientId, CancellationToken cancellationToken)
    {
        return await _dbContext.Patients.FirstOrDefaultAsync(x => x.Id == patientId, cancellationToken);
    }
    
    public async Task<bool> ExistsByRutAsync(string rut, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Patients.AnyAsync(x => x.Rut == rut, cancellationToken);
    }
    
    public void Add(Patient patient)
    {
        _dbContext.Patients.Add(patient);
    }
    
    public async Task<Patient?> GetByUserId(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Patients.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }
}