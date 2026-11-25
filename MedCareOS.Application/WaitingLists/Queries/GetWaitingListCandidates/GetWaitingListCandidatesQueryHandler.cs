using MedCareOS.Application.WaitingLists.Ranking;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Repositories;
using MediatR;

namespace MedCareOS.Application.WaitingLists.Queries.GetWaitingListCandidates;

public class GetWaitingListCandidatesQueryHandler
    : IRequestHandler<GetWaitingListCandidatesQuery, List<WaitingListEntryDto>>
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IWaitingListRanker _ranker;

    public GetWaitingListCandidatesQueryHandler(
        IWaitingListRepository waitingListRepository,
        IWaitingListRanker ranker)
    {
        _waitingListRepository = waitingListRepository;
        _ranker = ranker;
    }

    public async Task<List<WaitingListEntryDto>> Handle(
        GetWaitingListCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = request.TenantId ?? RiskThresholds.DefaultTenantId;
        var entries = await _waitingListRepository.QueryAsync(
            tenantId,
            request.Specialty,
            request.Status,
            request.FromDate,
            request.ToDate,
            cancellationToken);

        if (entries.Count == 0)
            return [];

        var utcNow = DateTimeOffset.UtcNow;
        var slotContext = new WaitingListSlotContext(
            ScheduleBlockId: null,
            TenantId: tenantId,
            Specialty: request.Specialty ?? string.Empty,
            StaffId: request.StaffId,
            StartTimeUtc: utcNow.UtcDateTime,
            EndTimeUtc: utcNow.UtcDateTime.AddHours(1));

        var snapshots = entries.Select(e => new WaitingListCandidateSnapshot(
            e.Id,
            e.PatientId,
            e.Specialty,
            e.RegistrationDate,
            e.Priority,
            e.PreferredStaffId,
            e.IsDisplacedBonus,
            e.ClinicalContext)).ToList();

        var ranked = _ranker.Rank(slotContext, snapshots, utcNow);
        var byId = entries.ToDictionary(e => e.Id);

        return ranked.Select(r =>
        {
            var e = byId[r.Candidate.WaitingListId];
            return new WaitingListEntryDto(
                e.Id,
                e.TenantId,
                e.PatientId,
                e.Specialty,
                e.RegistrationDate,
                e.Priority,
                e.Status,
                e.ClinicalContext,
                e.PreferredStaffId,
                e.PreferredFromDate,
                e.PreferredToDate,
                e.PreferredTimeStart,
                e.PreferredTimeEnd,
                e.IsDisplacedBonus,
                e.DisplacedSourceAppointmentId,
                e.ExpiresAt,
                r.Score,
                r.ScoreBreakdown);
        }).ToList();
    }
}
