using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IBoxRepository
{
    void Add(Box box);
    Task<bool> ExistsByNameAndFloorAsync(string name, string floor, CancellationToken cancellationToken);
    Task<bool> ExistsByNameAndFloorExceptIdAsync(Guid boxId, string name, string floor, CancellationToken cancellationToken = default);
    Task<Box?> GetByIdAsync(Guid boxId, CancellationToken cancellationToken = default);
}