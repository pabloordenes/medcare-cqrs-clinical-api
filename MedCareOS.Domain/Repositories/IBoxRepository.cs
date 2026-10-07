using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IBoxRepository
{
    void Add(Box box);
    Task<bool> ExistsByNameAndFloorAsync(string name, string floor, CancellationToken cancellationToken);
    
}