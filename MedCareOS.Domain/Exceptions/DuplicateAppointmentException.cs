namespace MedCareOS.Domain.Exceptions;

public class DuplicateAppointmentException : InvalidOperationException
{
    public DuplicateAppointmentException(string message) : base(message)
    {
    }
}
