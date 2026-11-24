namespace MedCareOS.Application.Common.Exceptions;

public class AiServiceTransientException : Exception
{
    public int? StatusCode { get; }

    public AiServiceTransientException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

public class AiServicePermanentException : Exception
{
    public int? StatusCode { get; }

    public AiServicePermanentException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
