namespace MedCareOS.Domain.Entities;

public class Staff
{
    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } =  string.Empty;
    public string Rut { get; private set; } = string.Empty;
    public string? Specialty { get; private set; }
    public string RoleName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; } = true;
    
    private Staff() {} // ef

    public static Staff Create(Guid? userId, string firstName, string lastName, string rut, 
        string? specialty, string roleName, string? phone)
    {
        return new Staff
        {
            Id =  Guid.NewGuid(), UserId = userId, FirstName = firstName, LastName = lastName,
            Rut = rut, Specialty = specialty, RoleName = roleName, Phone = phone, IsActive = true
        };
    }
}