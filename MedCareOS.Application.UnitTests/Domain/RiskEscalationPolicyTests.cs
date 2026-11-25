using FluentAssertions;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Services;

namespace MedCareOS.Application.UnitTests.Domain;

public class RiskEscalationPolicyTests
{
    [Fact]
    public void ShouldEmitRiskEscalated_ReturnsFalseAtT0InitialScoring_EvenWhenInitialBandIsHigh()
    {
        var appointment = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control");

        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, null, RiskBandLevel.High).Should().BeFalse();
        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, null, RiskBandLevel.Medium).Should().BeFalse();
        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, null, RiskBandLevel.Low).Should().BeFalse();
        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, RiskBandLevel.Medium, null).Should().BeFalse();
    }

    [Theory]
    [InlineData(RiskBandLevel.Low, RiskBandLevel.Low, false)]
    [InlineData(RiskBandLevel.Low, RiskBandLevel.Medium, false)]
    [InlineData(RiskBandLevel.Low, RiskBandLevel.High, true)]
    [InlineData(RiskBandLevel.Medium, RiskBandLevel.Medium, false)]
    [InlineData(RiskBandLevel.Medium, RiskBandLevel.Low, false)]
    [InlineData(RiskBandLevel.Medium, RiskBandLevel.High, true)]
    [InlineData(RiskBandLevel.High, RiskBandLevel.High, false)]
    [InlineData(RiskBandLevel.High, RiskBandLevel.Medium, false)]
    [InlineData(RiskBandLevel.High, RiskBandLevel.Low, false)]
    public void ShouldEmitRiskEscalated_OnlyEmitsWhenCrossingIntoHigh(
        RiskBandLevel previousBand,
        RiskBandLevel newBand,
        bool expected)
    {
        var appointment = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control");

        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, previousBand, newBand)
            .Should().Be(expected);
    }

    [Fact]
    public void ShouldEmitRiskEscalated_HysteresisPreventsFlappingAroundThresholdSequence()
    {
        var policy = TenantConfirmationPolicy.CreateDefault(Guid.NewGuid());
        var thresholds = policy.GetHysteresisThresholds(); // MediumEnter=0.50, HighExit=0.55, HighEnter=0.70
        var appointment = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control");

        // 1. Score inicial 0.65 (Medium) -> T0 nunca emite
        var band = RiskBandEvaluator.EvaluateInitial(0.65m, thresholds);
        band.Should().Be(RiskBandLevel.Medium);
        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, null, band).Should().BeFalse();

        // 2. Sube a 0.72 (>= 0.70) -> cruza a High -> DEBE emitir
        var next = RiskBandEvaluator.Evaluate(0.72m, band, thresholds);
        next.Should().Be(RiskBandLevel.High);
        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, band, next).Should().BeTrue();
        band = next;

        // 3. Fluctúa a 0.60 (>= 0.55 HighExit) -> histéresis lo mantiene en High -> NO emite
        next = RiskBandEvaluator.Evaluate(0.60m, band, thresholds);
        next.Should().Be(RiskBandLevel.High);
        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, band, next).Should().BeFalse();
        band = next;

        // 4. Vuelve a subir a 0.74 -> ya estaba en High -> NO emite (anti-flapping)
        next = RiskBandEvaluator.Evaluate(0.74m, band, thresholds);
        next.Should().Be(RiskBandLevel.High);
        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, band, next).Should().BeFalse();
        band = next;

        // 5. Baja a 0.50 (< 0.55 HighExit y >= 0.40 MediumExit) -> baja a Medium -> NO emite
        next = RiskBandEvaluator.Evaluate(0.50m, band, thresholds);
        next.Should().Be(RiskBandLevel.Medium);
        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, band, next).Should().BeFalse();
        band = next;

        // 6. Vuelve a cruzar >= 0.70 (0.71) -> cruza de Medium a High -> DEBE emitir
        next = RiskBandEvaluator.Evaluate(0.71m, band, thresholds);
        next.Should().Be(RiskBandLevel.High);
        RiskEscalationPolicy.ShouldEmitRiskEscalated(appointment, band, next).Should().BeTrue();
    }

    [Fact]
    public void ShouldEmitRiskEscalated_ReturnsFalseWhenSuppressedConfirmedDeclinedOrNotScheduled()
    {
        var now = DateTimeOffset.UtcNow;

        var confirmed = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control");
        confirmed.RegisterPatientConfirmation(now);
        RiskEscalationPolicy.ShouldEmitRiskEscalated(confirmed, RiskBandLevel.Medium, RiskBandLevel.High)
            .Should().BeFalse();

        var declined = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control");
        declined.DeclineByPatient("No puedo", now);
        RiskEscalationPolicy.ShouldEmitRiskEscalated(declined, RiskBandLevel.Medium, RiskBandLevel.High)
            .Should().BeFalse();

        var cancelled = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control");
        cancelled.Cancel("Clinica");
        RiskEscalationPolicy.ShouldEmitRiskEscalated(cancelled, RiskBandLevel.Medium, RiskBandLevel.High)
            .Should().BeFalse();

        var finished = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control");
        finished.StartConsultation();
        finished.FinishConsultation();
        RiskEscalationPolicy.ShouldEmitRiskEscalated(finished, RiskBandLevel.Medium, RiskBandLevel.High)
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldEmitConfirmationDeadlineMissed_RequiresActualUnresponsiveTransitionOnActiveAppointment()
    {
        var now = DateTimeOffset.UtcNow;
        var unresponsive = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control");
        var transition = unresponsive.MarkUnresponsive(now);

        RiskEscalationPolicy.ShouldEmitConfirmationDeadlineMissed(unresponsive, transition.StateChanged)
            .Should().BeTrue();

        // Replay idempotente (StateChanged = false) no re-emite evento
        var replay = unresponsive.MarkUnresponsive(now.AddMinutes(5));
        RiskEscalationPolicy.ShouldEmitConfirmationDeadlineMissed(unresponsive, replay.StateChanged)
            .Should().BeFalse();

        // Confirmación posterior suprime
        unresponsive.RegisterPatientConfirmation(now.AddMinutes(10));
        RiskEscalationPolicy.ShouldEmitConfirmationDeadlineMissed(unresponsive, transitionedToUnresponsive: true)
            .Should().BeFalse();
    }
}
