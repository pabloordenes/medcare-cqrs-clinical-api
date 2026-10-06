using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MedCareOS.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _dbContext;
    
    public UserRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void AddUser(User user)
    {
        _dbContext.Users.Add(user);
    }

    public async Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
    }
    
    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users.AnyAsync(x => x.Email == email, cancellationToken);
    } 
}