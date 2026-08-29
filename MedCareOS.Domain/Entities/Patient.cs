namespace MedCareOS.Domain.Entities;

public class Patient
{
    public Guid Id { get; private set; }
    public string Rut { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string MedicalInsurance { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public int NoShowHistoryCount { get; private set; }

    private Patient() // para reflection con ef
    {
        
    }

    private Patient(Guid id, string rut, 
        string firstName, string lastName, 
        string medicalInsurance, string phone, 
        string? email, int noShowHistoryCount)
    {
        Id = id;
        Rut = rut;
        FirstName = firstName;
        LastName = lastName;
        MedicalInsurance = medicalInsurance;
        Phone = phone;
        Email = email;
        NoShowHistoryCount = noShowHistoryCount;
    }
    
    public static Patient Create(Guid id, string rut, string firstName, 
        string lastName, string medicalInsurance, string phone, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("Ambos nombres deben llenarse.");
        
        if (string.IsNullOrWhiteSpace(rut))
            throw new ArgumentException("Debe ingresar su RUT.");
        
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Debe ingresar su Numero Telefonico.");
        
        if (string.IsNullOrWhiteSpace(medicalInsurance))
            throw new ArgumentException("Debe especificar su seguro de salud.");

        return new Patient(
            id,
            rut,
            firstName,
            lastName,
            medicalInsurance, 
            phone,
            email,
            0);
    }

    public void IncrementNoShowHistory()
    {
        NoShowHistoryCount++;
    }
}