using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    
    private User() {} // ef

    public static User Create(string email, UserRole role)
    {
        return new User
        {
            Id =  Guid.NewGuid(), Email = email, Role =  role, CreatedAt =  DateTimeOffset.UtcNow
        };
    }
}   