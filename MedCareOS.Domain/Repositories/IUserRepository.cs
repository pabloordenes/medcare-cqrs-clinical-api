using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Repositories;

public interface IUserRepository
{
    void Add(User user);
    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);
}