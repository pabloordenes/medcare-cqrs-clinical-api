namespace MedCareOS.Application.WaitingLists.Ranking;

public sealed record WaitingListSlotContext(
    Guid? ScheduleBlockId,
    Guid TenantId,
    string Specialty,
    Guid? StaffId,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc);

public sealed record WaitingListCandidateSnapshot(
    Guid WaitingListId,
    Guid PatientId,
    string Specialty,
    DateTimeOffset RegistrationDate,
    int Priority,
    Guid? PreferredStaffId,
    bool IsDisplacedBonus,
    string? ClinicalContextJson);

public sealed record RankedWaitingListCandidate(
    WaitingListCandidateSnapshot Candidate,
    decimal Score,
    string ScoreBreakdown);

/// <summary>
/// Estrategia de ranking para selección de candidatos en lista de espera.
/// El futuro ranker semántico/RAG con Qdrant implementará esta interfaz sin modificar consumidores ni handlers.
/// </summary>
public interface IWaitingListRanker
{
    IReadOnlyList<RankedWaitingListCandidate> Rank(
        WaitingListSlotContext slot,
        IReadOnlyList<WaitingListCandidateSnapshot> candidates,
        DateTimeOffset utcNow);
}
