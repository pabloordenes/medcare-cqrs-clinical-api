using MedCareOS.Domain.Entities;

namespace MedCareOS.Application.WaitingLists.Ranking;

public sealed class DeterministicWaitingListRanker : IWaitingListRanker
{
    public const decimal DisplacedBonusWeight = 1000m;
    public const decimal PriorityWeight = 100m;
    public const decimal PreferredStaffMatchWeight = 25m;
    public const decimal WaitDaysWeightPerDay = 2m;
    public const decimal MaxWaitDaysContribution = 90m;

    public IReadOnlyList<RankedWaitingListCandidate> Rank(
        WaitingListSlotContext slot,
        IReadOnlyList<WaitingListCandidateSnapshot> candidates,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
            return [];

        var normalizedSlotSpecialty = WaitingList.NormalizeSpecialty(slot.Specialty);

        var ranked = candidates
            .Where(c => string.IsNullOrEmpty(normalizedSlotSpecialty) ||
                        WaitingList.NormalizeSpecialty(c.Specialty) == normalizedSlotSpecialty)
            .Select(candidate =>
            {
                decimal displacedScore = candidate.IsDisplacedBonus ? DisplacedBonusWeight : 0m;
                decimal priorityScore = Math.Max(0, candidate.Priority) * PriorityWeight;
                decimal staffScore = slot.StaffId.HasValue && candidate.PreferredStaffId == slot.StaffId.Value
                    ? PreferredStaffMatchWeight
                    : 0m;

                double waitDaysRaw = Math.Max(0d, (utcNow - candidate.RegistrationDate).TotalDays);
                decimal waitDaysScore = Math.Min(
                    MaxWaitDaysContribution,
                    Math.Round((decimal)waitDaysRaw * WaitDaysWeightPerDay, 2, MidpointRounding.AwayFromZero));

                decimal total = displacedScore + priorityScore + staffScore + waitDaysScore;
                string breakdown = $"displaced={displacedScore:0.##},priority={priorityScore:0.##},staff={staffScore:0.##},wait={waitDaysScore:0.##}";

                return new RankedWaitingListCandidate(candidate, total, breakdown);
            })
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Candidate.RegistrationDate)
            .ThenBy(r => r.Candidate.WaitingListId)
            .ToList();

        return ranked;
    }
}
