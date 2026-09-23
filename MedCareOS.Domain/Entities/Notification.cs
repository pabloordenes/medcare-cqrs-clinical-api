namespace MedCareOS.Domain.Entities;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid AppointmentId { get; private set; }
    public Guid PatientId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Channel { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    
    private Notification() {} // ef

    public static Notification Create(Guid appointmentId, Guid patientId, string type, string channel,
        string content)
    {
        return new Notification
        {
            Id = Guid.NewGuid(), AppointmentId = appointmentId, PatientId = patientId, Type = type,
            Channel = channel, Content = content, Status = "pendiente", CreatedAt = DateTimeOffset.UtcNow
        };
    }
}