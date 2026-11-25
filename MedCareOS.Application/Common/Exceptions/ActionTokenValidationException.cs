using MedCareOS.Application.Common.Interfaces.Notifications;

namespace MedCareOS.Application.Common.Exceptions;

public class ActionTokenValidationException : Exception
{
    public ActionTokenValidationStatus Status { get; }

    public ActionTokenValidationException(ActionTokenValidationStatus status, string message)
        : base(message)
    {
        Status = status;
    }
}
