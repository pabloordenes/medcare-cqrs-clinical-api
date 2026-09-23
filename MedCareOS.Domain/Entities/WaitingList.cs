namespace MedCareOS.Domain.Entities;

public class WaitingList
{
    public Guid Id { get; private set; }
    public Guid PatientId { get; private set; }
    public string Specialty { get; private set; } = string.Empty;
    public DateTime RegistrationDate { get; private set; }
    public int Priority { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? ClinicalContext { get; private set; }

    private WaitingList() { } // ef

    public static WaitingList Create(Guid patientId, string specialty, string clinicalContext)
    {
        return new WaitingList
        {
            Id =  Guid.NewGuid(), PatientId = patientId, Specialty =  specialty, RegistrationDate = DateTime.UtcNow,
            Priority = 0, Status = "activa", ClinicalContext =  clinicalContext
        };
    }

}