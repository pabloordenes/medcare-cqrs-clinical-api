namespace MedCareOS.Domain.Entities;

public class Staff
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } =  string.Empty;
    public string Rut { get; private set; } = string.Empty;
    public string? Specialty { get; private set; }
    public string RoleName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    
    private Staff() {} // ef

    public static Staff Create(Guid userId, string firstName, string lastName, string rut, 
        string? specialty, string roleName, string? phone)
    {
        return new Staff
        {
            Id =  Guid.NewGuid(), UserId = userId, FirstName = firstName, LastName = lastName,
            Rut = rut, Specialty = specialty, RoleName = roleName, Phone = phone
        };
    }

    public void UpdateProfile(string firstName, string lastName, string rut,
        string? phone, string? specialty, string roleName)
    {
        FirstName = firstName;
        LastName = lastName;
        Rut = rut;
        Phone = phone;
        Specialty = specialty;
        RoleName = roleName;
    }
}