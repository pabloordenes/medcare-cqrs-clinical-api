namespace MedCareOS.Domain.Entities;

public class Patient
{
    public Guid Id { get; private set; }
    public Guid UserId { get; set; }
    public string Rut { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateTime DateOfBirth { get; private set; }
    public string Gender { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public string? Neighbourhood { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public int NoShowHistoryCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Patient() { } // ef core

    private Patient(Guid id, Guid userId, string rut, 
        string firstName, string lastName, DateTime dateOfBirth,
        string gender, string address, string neighbourhood, string phone, 
        string? email, int noShowHistoryCount)
    {
        Id = id;
        UserId = userId;
        Rut = rut;
        FirstName = firstName;
        LastName = lastName;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Phone = phone;
        Email = email;
        Address =  address;
        Neighbourhood = neighbourhood;
        NoShowHistoryCount = noShowHistoryCount;
        CreatedAt = DateTimeOffset.UtcNow;
    }
    
    public static Patient Create(Guid userId, string rut, string firstName, 
        string lastName, DateTime dateOfBirth, string gender, string phone, string? email = null,
        string? address = null, string? neighbourhood = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("Debe vincularse a un usuario de Supabase Auth.");
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Ambos nombres deben llenarse.");

        if (string.IsNullOrWhiteSpace(gender))
            throw new ArgumentException("Debe especificar su genero.");
        
        if (string.IsNullOrWhiteSpace(rut))
            throw new ArgumentException("Debe ingresar su RUT.");
        
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Debe ingresar su Numero Telefonico.");

        return new Patient(
            Guid.NewGuid(),
            userId,
            rut,
            firstName,
            lastName,
            dateOfBirth,
            gender,
            phone,
            email,
            address,
            neighbourhood,
            0);
    }

    public void IncrementNoShowHistory()
    {
        NoShowHistoryCount++;
    }
}