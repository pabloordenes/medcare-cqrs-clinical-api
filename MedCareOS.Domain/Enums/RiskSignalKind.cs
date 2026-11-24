namespace MedCareOS.Domain.Enums;

public enum RiskSignalKind
{
    ConfirmedEarly,
    ReminderUnanswered,
    DeliveryFailed,
    RepliedWithoutConfirmation,
    FinalConfirmed,
    DeclinedByPatient,
    ModelRescored
}
