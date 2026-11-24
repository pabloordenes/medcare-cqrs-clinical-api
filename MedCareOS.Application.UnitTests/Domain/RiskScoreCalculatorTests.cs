using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Services;

namespace MedCareOS.Application.UnitTests.Domain;

public class RiskScoreCalculatorTests
{
    private static RiskSignal MakeSignal(RiskSignalKind kind, decimal weight, int index = 0)
        => RiskSignal.Create(
            RiskThresholds.DefaultTenantId,
            Guid.NewGuid(),
            kind,
            weight,
            "unit_test",
            $"{kind}:{index}:{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow.AddMinutes(index),
            Guid.NewGuid());

    [Fact]
    public void ApplyLogitDelta_ClampsBaseProbabilityTo001And099()
    {
        var atZero = RiskScoreCalculator.ApplyLogitDelta(0.00m, 0.4m);
        var atMin = RiskScoreCalculator.ApplyLogitDelta(0.01m, 0.4m);
        atZero.Should().Be(atMin);

        var atOne = RiskScoreCalculator.ApplyLogitDelta(1.00m, -1.5m);
        var atMax = RiskScoreCalculator.ApplyLogitDelta(0.99m, -1.5m);
        atOne.Should().Be(atMax);
    }

    [Fact]
    public void ComputeEffectiveScore_AppliesExactLogitTransformsForStandardWeights()
    {
        // p0 = 0.50 -> logit(0.50) = 0
        // ConfirmedEarly (-1.5) -> 1 / (1 + e^1.5) = 0.1824255... -> 0.1824
        var early = RiskScoreCalculator.ComputeEffectiveScore(
            0.50m,
            [MakeSignal(RiskSignalKind.ConfirmedEarly, -1.5m)],
            maxReminderUnansweredCumulative: 1.2m);
        early.Should().Be(0.1824m);

        // DeliveryFailed (+0.8) -> 1 / (1 + e^-0.8) = 0.689974... -> 0.6900
        var failed = RiskScoreCalculator.ComputeEffectiveScore(
            0.50m,
            [MakeSignal(RiskSignalKind.DeliveryFailed, 0.8m)],
            maxReminderUnansweredCumulative: 1.2m);
        failed.Should().Be(0.6900m);

        // Neutral signal (0.0) & ModelRescored (0.0) preserve exact model probability
        var neutral = RiskScoreCalculator.ComputeEffectiveScore(
            0.6234m,
            [
                MakeSignal(RiskSignalKind.RepliedWithoutConfirmation, 0.0m, 1),
                MakeSignal(RiskSignalKind.FinalConfirmed, 0.0m, 2),
                MakeSignal(RiskSignalKind.ModelRescored, 0.0m, 3)
            ],
            maxReminderUnansweredCumulative: 1.2m);
        neutral.Should().Be(0.6234m);
    }

    [Fact]
    public void ComputeEffectiveScore_CapsCumulativeReminderUnansweredAt1Point2()
    {
        var threeUnanswered = new[]
        {
            MakeSignal(RiskSignalKind.ReminderUnanswered, 0.4m, 1),
            MakeSignal(RiskSignalKind.ReminderUnanswered, 0.4m, 2),
            MakeSignal(RiskSignalKind.ReminderUnanswered, 0.4m, 3)
        };

        var fiveUnanswered = threeUnanswered.Concat([
            MakeSignal(RiskSignalKind.ReminderUnanswered, 0.4m, 4),
            MakeSignal(RiskSignalKind.ReminderUnanswered, 0.4m, 5)
        ]).ToArray();

        var scoreAt3 = RiskScoreCalculator.ComputeEffectiveScore(0.40m, threeUnanswered, 1.2m);
        var scoreAt5 = RiskScoreCalculator.ComputeEffectiveScore(0.40m, fiveUnanswered, 1.2m);

        scoreAt3.Should().Be(0.6888m);
        scoreAt5.Should().Be(scoreAt3);
    }

    [Fact]
    public void ComputeEffectiveScore_IsOrderCommutative()
    {
        var s1 = MakeSignal(RiskSignalKind.ReminderUnanswered, 0.4m, 1);
        var s2 = MakeSignal(RiskSignalKind.DeliveryFailed, 0.8m, 2);
        var s3 = MakeSignal(RiskSignalKind.ConfirmedEarly, -1.5m, 3);
        var s4 = MakeSignal(RiskSignalKind.RepliedWithoutConfirmation, 0.0m, 4);

        var forward = RiskScoreCalculator.ComputeEffectiveScore(0.55m, [s1, s2, s3, s4], 1.2m);
        var reverse = RiskScoreCalculator.ComputeEffectiveScore(0.55m, [s4, s3, s2, s1], 1.2m);

        forward.Should().Be(reverse);
    }

    [Fact]
    public void AppointmentApplyModelRescore_PreservesImmutableT0ModelScoreAndReappliesSignalsOnLatestModelScore()
    {
        var policy = TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId);
        var appointment = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control", "paciente");
        var t0 = DateTimeOffset.UtcNow;

        appointment.RecordInitialModelScore(0.35m, "Bajo", policy.GetHysteresisThresholds(), t0);
        appointment.ModelRiskScore.Should().Be(0.35m);
        appointment.EffectiveRiskScore.Should().Be(0.35m);

        var unansweredSignal = RiskSignal.Create(
            appointment.TenantId,
            appointment.Id,
            RiskSignalKind.ReminderUnanswered,
            0.4m,
            "scheduled_action:Reminder2AndRescore",
            $"reminder_unanswered:{Guid.NewGuid()}",
            t0.AddDays(2),
            Guid.NewGuid());

        appointment.ApplyModelRescore(0.60m, [unansweredSignal], policy, t0.AddDays(2));

        appointment.ModelRiskScore.Should().Be(0.35m, "el score_modelo T0 debe permanecer inmutable");
        appointment.LatestModelRiskScore.Should().Be(0.60m);
        appointment.RescoredAt.Should().Be(t0.AddDays(2));
        appointment.EffectiveRiskScore.Should().Be(RiskScoreCalculator.ApplyLogitDelta(0.60m, 0.4m));
        appointment.EffectiveBand.Should().Be(RiskBandLevel.Medium);
        appointment.NoShowRiskScore.Should().Be(appointment.EffectiveRiskScore);
    }
}
