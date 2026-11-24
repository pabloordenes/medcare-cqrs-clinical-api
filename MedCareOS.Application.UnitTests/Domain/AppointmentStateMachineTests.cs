using FluentAssertions;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.UnitTests.Domain;

public class AppointmentStateMachineTests
{
    private static Appointment CreateScheduledAppointment()
        => Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Dolor de cabeza", "paciente");

    [Fact]
    public void Confirm_WhenScheduled_TransitionsToConfirmed()
    {
        var appointment = CreateScheduledAppointment();

        appointment.Confirm();

        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        appointment.ActualAttendance.Should().BeNull();
    }

    [Fact]
    public void Confirm_WhenAlreadyConfirmed_ThrowsInvalidOperationException()
    {
        var appointment = CreateScheduledAppointment();
        appointment.Confirm();

        Action act = () => appointment.Confirm();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CheckIn_WhenScheduled_TransitionsToConfirmedAndSetsActualAttendanceTrue()
    {
        var appointment = CreateScheduledAppointment();

        appointment.CheckIn();

        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        appointment.ActualAttendance.Should().BeTrue();
    }

    [Fact]
    public void CheckIn_WhenCancelled_ThrowsInvalidOperationException()
    {
        var appointment = CreateScheduledAppointment();
        appointment.Cancel("Paciente reprogramó");

        Action act = () => appointment.CheckIn();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void StartAndFinishConsultation_UpdatesStatusAndActualAttendanceTrue()
    {
        var appointment = CreateScheduledAppointment();

        appointment.StartConsultation();
        appointment.Status.Should().Be(AppointmentStatus.InProgress);
        appointment.ActualAttendance.Should().BeTrue();

        appointment.FinishConsultation();
        appointment.Status.Should().Be(AppointmentStatus.Finished);
        appointment.ActualAttendance.Should().BeTrue();
    }

    [Fact]
    public void FinishConsultation_WhenNotInProgress_ThrowsInvalidOperationException()
    {
        var appointment = CreateScheduledAppointment();

        Action act = () => appointment.FinishConsultation();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cancel_WhenScheduledOrConfirmed_TransitionsToCancelledAndLeavesAttendanceNull()
    {
        var appointment = CreateScheduledAppointment();
        appointment.Confirm();

        appointment.Cancel("Cancelada por paciente");

        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.ActualAttendance.Should().BeNull();
    }

    [Fact]
    public void MarkNoShow_WhenScheduledOrConfirmed_SetsNoShowAndActualAttendanceFalse()
    {
        var appointment = CreateScheduledAppointment();
        appointment.Confirm();

        appointment.MarkNoShow();

        appointment.Status.Should().Be(AppointmentStatus.NoShow);
        appointment.ActualAttendance.Should().BeFalse();
    }

    [Fact]
    public void MarkNoShow_WhenFinishedOrCancelled_ThrowsInvalidOperationException()
    {
        var cancelled = CreateScheduledAppointment();
        cancelled.Cancel();

        var finished = CreateScheduledAppointment();
        finished.StartConsultation();
        finished.FinishConsultation();

        ((Action)(() => cancelled.MarkNoShow())).Should().Throw<InvalidOperationException>();
        ((Action)(() => finished.MarkNoShow())).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Create_ShouldInitializePendingScoringAndNullScore()
    {
        var withSymptoms = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Fiebre alta", "paciente");
        var withoutSymptoms = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "   ", "paciente");

        withSymptoms.ScoringStatus.Should().Be(ScoringStatus.Pending);
        withSymptoms.NlpStatus.Should().Be(NlpStatus.Pending);
        withSymptoms.NoShowRiskScore.Should().BeNull();
        withSymptoms.RiskBand.Should().BeNull();
        withSymptoms.ScoringAttempts.Should().Be(0);

        withoutSymptoms.ScoringStatus.Should().Be(ScoringStatus.Pending);
        withoutSymptoms.NlpStatus.Should().Be(NlpStatus.Skipped);
    }

    [Fact]
    public void UpdateRiskScore_ShouldRejectPendienteAndOutOfRangeValues()
    {
        var appointment = CreateScheduledAppointment();

        ((Action)(() => appointment.UpdateRiskScore(0m, "Pendiente"))).Should().Throw<ArgumentException>();
        ((Action)(() => appointment.UpdateRiskScore(-0.1m, "Bajo"))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => appointment.UpdateRiskScore(1.1m, "Alto"))).Should().Throw<ArgumentOutOfRangeException>();

        appointment.UpdateRiskScore(0.75m, "Alto");

        appointment.ScoringStatus.Should().Be(ScoringStatus.Scored);
        appointment.NoShowRiskScore.Should().Be(0.75m);
        appointment.RiskBand.Should().Be("Alto");
        appointment.ScoredAt.Should().NotBeNull();
    }

    [Fact]
    public void RecordScoringFailure_ShouldTrackAttemptsAndRespectMaxAttempts_WithoutTouchingNlpSummary()
    {
        var appointment = CreateScheduledAppointment();
        var t0 = DateTimeOffset.UtcNow;

        appointment.RecordScoringFailure("Timeout 1", maxAttempts: 2, t0, TimeSpan.FromMinutes(2), scoringFailed: true, nlpFailed: true);

        appointment.ScoringStatus.Should().Be(ScoringStatus.Failed);
        appointment.NlpStatus.Should().Be(NlpStatus.Failed);
        appointment.NlpSummary.Should().BeNull();
        appointment.ScoringAttempts.Should().Be(1);
        appointment.NextScoringRetryAt.Should().Be(t0.AddMinutes(2));

        appointment.RecordScoringFailure("Timeout 2", maxAttempts: 2, t0.AddMinutes(2), TimeSpan.FromMinutes(4), scoringFailed: true, nlpFailed: true);

        appointment.ScoringAttempts.Should().Be(2);
        appointment.NextScoringRetryAt.Should().BeNull();
        appointment.NlpSummary.Should().BeNull();
    }

    [Fact]
    public void MarkScoringTerminalFailure_ShouldClearNextRetryAt()
    {
        var appointment = CreateScheduledAppointment();
        var now = DateTimeOffset.UtcNow;

        appointment.MarkScoringTerminalFailure("400 Bad Request", now, scoringFailed: true, nlpFailed: false);

        appointment.ScoringStatus.Should().Be(ScoringStatus.Failed);
        appointment.NlpStatus.Should().Be(NlpStatus.Pending);
        appointment.NextScoringRetryAt.Should().BeNull();
        appointment.LastScoringError.Should().Contain("400");
    }
}
