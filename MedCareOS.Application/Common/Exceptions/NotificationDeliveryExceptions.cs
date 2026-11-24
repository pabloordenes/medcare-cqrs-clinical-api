namespace MedCareOS.Application.Common.Exceptions;

public class NotificationTransientDeliveryException : Exception
{
    public int? StatusCode { get; }
    public string? ErrorCode { get; }

    public NotificationTransientDeliveryException(string message, int? statusCode = null, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

public class NotificationPermanentDeliveryException : Exception
{
    public int? StatusCode { get; }
    public string? ErrorCode { get; }

    public NotificationPermanentDeliveryException(string message, int? statusCode = null, string? errorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

public class ClinicalPrivacyViolationException : Exception
{
    public ClinicalPrivacyViolationException(string message)
        : base(message)
    {
    }
}
