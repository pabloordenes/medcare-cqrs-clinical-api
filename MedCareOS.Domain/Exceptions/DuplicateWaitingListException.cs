namespace MedCareOS.Domain.Exceptions;

public class DuplicateWaitingListException : Exception
{
    public DuplicateWaitingListException(string message) : base(message)
    {
    }
}
