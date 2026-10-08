using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IStaffRepository
{
    Task<Staff?> FindByRutAsync(string staffRut, CancellationToken cancellationToken = default);
    void Add(Staff staff);
    Task<Staff?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Staff?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}