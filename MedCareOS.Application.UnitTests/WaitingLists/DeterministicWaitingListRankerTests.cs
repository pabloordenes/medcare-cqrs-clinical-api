using FluentAssertions;
using MedCareOS.Application.WaitingLists.Ranking;
using MedCareOS.Domain.Constants;

namespace MedCareOS.Application.UnitTests.WaitingLists;

public class DeterministicWaitingListRankerTests
{
    private readonly DeterministicWaitingListRanker _ranker = new();

    [Fact]
    public void Rank_ShouldPrioritizeDisplacedBonusOverNormalPriority_ThenPriority_ThenStaffMatch_ThenWaitAge()
    {
        var utcNow = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var preferredStaffId = Guid.NewGuid();
        var otherStaffId = Guid.NewGuid();

        var slot = new WaitingListSlotContext(
            ScheduleBlockId: Guid.NewGuid(),
            TenantId: RiskThresholds.DefaultTenantId,
            Specialty: "Cardiología",
            StaffId: preferredStaffId,
            StartTimeUtc: utcNow.AddDays(1).UtcDateTime,
            EndTimeUtc: utcNow.AddDays(1).AddHours(1).UtcDateTime);

        var displaced = new WaitingListCandidateSnapshot(
            WaitingListId: Guid.NewGuid(),
            PatientId: Guid.NewGuid(),
            Specialty: "cardiologia",
            RegistrationDate: utcNow.AddHours(-1),
            Priority: 0,
            PreferredStaffId: otherStaffId,
            IsDisplacedBonus: true,
            ClinicalContextJson: null);

        var highPriorityWithStaff = new WaitingListCandidateSnapshot(
            WaitingListId: Guid.NewGuid(),
            PatientId: Guid.NewGuid(),
            Specialty: "Cardiologia",
            RegistrationDate: utcNow.AddDays(-2),
            Priority: 3,
            PreferredStaffId: preferredStaffId,
            IsDisplacedBonus: false,
            ClinicalContextJson: null);

        var highPriorityNoStaffOlder = new WaitingListCandidateSnapshot(
            WaitingListId: Guid.NewGuid(),
            PatientId: Guid.NewGuid(),
            Specialty: "CARDIOLOGÍA",
            RegistrationDate: utcNow.AddDays(-5),
            Priority: 3,
            PreferredStaffId: null,
            IsDisplacedBonus: false,
            ClinicalContextJson: null);

        var normalVeryOld = new WaitingListCandidateSnapshot(
            WaitingListId: Guid.NewGuid(),
            PatientId: Guid.NewGuid(),
            Specialty: "Cardiología",
            RegistrationDate: utcNow.AddDays(-120),
            Priority: 0,
            PreferredStaffId: null,
            IsDisplacedBonus: false,
            ClinicalContextJson: null);

        var differentSpecialty = new WaitingListCandidateSnapshot(
            WaitingListId: Guid.NewGuid(),
            PatientId: Guid.NewGuid(),
            Specialty: "Dermatología",
            RegistrationDate: utcNow.AddDays(-10),
            Priority: 10,
            PreferredStaffId: preferredStaffId,
            IsDisplacedBonus: true,
            ClinicalContextJson: null);

        var result = _ranker.Rank(
            slot,
            [normalVeryOld, highPriorityNoStaffOlder, highPriorityWithStaff, displaced, differentSpecialty],
            utcNow);

        result.Should().HaveCount(4);
        result[0].Candidate.WaitingListId.Should().Be(displaced.WaitingListId);
        result[1].Candidate.WaitingListId.Should().Be(highPriorityWithStaff.WaitingListId); // 300 + 25 + 4 = 329 > 300 + 10 = 310
        result[2].Candidate.WaitingListId.Should().Be(highPriorityNoStaffOlder.WaitingListId);
        result[3].Candidate.WaitingListId.Should().Be(normalVeryOld.WaitingListId);
        result[3].Score.Should().Be(DeterministicWaitingListRanker.MaxWaitDaysContribution);
    }

    [Fact]
    public void Rank_WhenScoresTie_ShouldBreakTiesDeterministicallyByEarlierRegistrationDateThenId()
    {
        var utcNow = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var slot = new WaitingListSlotContext(
            Guid.NewGuid(),
            RiskThresholds.DefaultTenantId,
            "Neurología",
            null,
            utcNow.AddDays(1).UtcDateTime,
            utcNow.AddDays(1).AddHours(1).UtcDateTime);

        var older = new WaitingListCandidateSnapshot(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            Guid.NewGuid(),
            "Neurologia",
            utcNow.AddDays(-60), // wait capped at 90
            1,
            null,
            false,
            null);

        var newerSameCappedScore = new WaitingListCandidateSnapshot(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.NewGuid(),
            "Neurologia",
            utcNow.AddDays(-50), // also capped at 90
            1,
            null,
            false,
            null);

        var result = _ranker.Rank(slot, [newerSameCappedScore, older], utcNow);

        result.Should().HaveCount(2);
        result[0].Candidate.WaitingListId.Should().Be(older.WaitingListId);
        result[1].Candidate.WaitingListId.Should().Be(newerSameCappedScore.WaitingListId);
    }
}
