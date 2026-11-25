namespace MedCareOS.Domain.Enums;

public enum ConfirmationState
{
    Pending,
    ConfirmedEarly,
    FinalRequested,
    FinalConfirmed,
    Unresponsive,
    DeclinedByPatient
}
