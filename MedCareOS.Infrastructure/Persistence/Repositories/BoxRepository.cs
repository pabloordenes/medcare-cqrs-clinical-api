using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class BoxRepository : IBoxRepository
{
    private readonly ApplicationDbContext _dbContext;
    
    public BoxRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(Box box)
    {
        _dbContext.Boxes.Add(box);
    }

    public async Task<bool> ExistsByNameAndFloorAsync(string name, string floor, CancellationToken cancellationToken)
    {
        return await _dbContext.Boxes.AnyAsync(x => x.Name == name && x.Floor == floor, cancellationToken);
    }
    
    public async Task<bool> ExistsByNameAndFloorExceptIdAsync(Guid boxId, string name, string floor, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Boxes.AnyAsync(x => x.Id != boxId && x.Name == name && x.Floor == floor, cancellationToken);
    }
    
    public async Task<Box?> GetByIdAsync(Guid boxId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Boxes.FirstOrDefaultAsync(x => x.Id == boxId, cancellationToken);
    }
}