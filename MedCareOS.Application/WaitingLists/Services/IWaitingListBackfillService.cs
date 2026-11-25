namespace MedCareOS.Application.WaitingLists.Services;

public sealed record WaitingListBackfillAttemptResult(
    string Outcome,
    Guid? OfferId = null,
    Guid? WaitingListId = null,
    Guid? PatientId = null);

public interface IWaitingListBackfillService
{
    Task<WaitingListBackfillAttemptResult> OfferNextCandidateAsync(
        Guid scheduleBlockId,
        Guid tenantId,
        string sourceType,
        Guid? sourceAppointmentId,
        Guid correlationId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
