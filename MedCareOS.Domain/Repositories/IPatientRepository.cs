using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IPatientRepository
{
    Task<Patient?> GetPatientByIdAsync(Guid id, CancellationToken cancellationToken = default);
}