using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.ValueObjects;

public enum ConfirmationTransitionOutcome
{
    ConfirmedEarly,
    FinalConfirmed,
    AlreadyConfirmed,
    FinalRequested,
    AlreadyFinalRequested,
    MarkedUnresponsive,
    AlreadyUnresponsive,
    DeclinedByPatient,
    AlreadyDeclined
}

public sealed record ConfirmationTransitionResult(
    ConfirmationTransitionOutcome Outcome,
    ConfirmationState PreviousState,
    ConfirmationState NewState,
    bool StateChanged,
    bool ShouldSupersedeHitlTasks,
    bool ShouldCancelPendingScheduledActions);
