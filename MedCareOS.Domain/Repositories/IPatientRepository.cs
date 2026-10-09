using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IPatientRepository
{
    Task<Patient?> GetPatientByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByRutAsync(string rut, CancellationToken cancellationToken = default);
    void Add(Patient patient);
    Task<Patient?> GetByUserId(Guid userId, CancellationToken cancellationToken = default);
    
}