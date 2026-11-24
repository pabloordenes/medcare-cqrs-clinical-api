using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.ValueObjects;

namespace MedCareOS.Application.UnitTests.Domain;

public class ConfirmationLifecycleTests
{
    private static Appointment CreateScheduledAppointment(Guid? tenantId = null)
        => Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Dolor lumbar", "paciente", tenantId);

    [Fact]
    public void Create_ShouldInitializePendingConfirmationStateAndDefaultTenant()
    {
        var appointment = CreateScheduledAppointment();

        appointment.TenantId.Should().Be(RiskThresholds.DefaultTenantId);
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.ConfirmationState.Should().Be(ConfirmationState.Pending);
        appointment.OrchestrationSuppressed.Should().BeFalse();
        appointment.IsPatientConfirmed.Should().BeFalse();
        appointment.ConfirmedAt.Should().BeNull();
        appointment.FinalConfirmationRequestedAt.Should().BeNull();
        appointment.DeadlineMissedAt.Should().BeNull();
        appointment.ConfirmationOutcome.Should().BeNull();
    }

    [Fact]
    public void Create_WithCustomTenant_ShouldAssignTenantId_AndRejectEmptyGuid()
    {
        var tenantId = Guid.NewGuid();
        var appointment = CreateScheduledAppointment(tenantId);

        appointment.TenantId.Should().Be(tenantId);

        Action act = () => Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control", "paciente", Guid.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterPatientConfirmation_WhenPending_TransitionsToConfirmedEarly_AndSuppressesOrchestration()
    {
        var appointment = CreateScheduledAppointment();
        var t1 = DateTimeOffset.UtcNow;

        var result = appointment.RegisterPatientConfirmation(t1);

        result.Outcome.Should().Be(ConfirmationTransitionOutcome.ConfirmedEarly);
        result.PreviousState.Should().Be(ConfirmationState.Pending);
        result.NewState.Should().Be(ConfirmationState.ConfirmedEarly);
        result.StateChanged.Should().BeTrue();
        result.ShouldSupersedeHitlTasks.Should().BeTrue();
        result.ShouldCancelPendingScheduledActions.Should().BeTrue();

        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        appointment.ConfirmationState.Should().Be(ConfirmationState.ConfirmedEarly);
        appointment.IsPatientConfirmed.Should().BeTrue();
        appointment.OrchestrationSuppressed.Should().BeTrue();
        appointment.ConfirmedAt.Should().Be(t1);
    }

    [Fact]
    public void RegisterPatientConfirmation_WhenAlreadyConfirmed_IsIdempotentNoOp()
    {
        var appointment = CreateScheduledAppointment();
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddMinutes(10);

        appointment.RegisterPatientConfirmation(t1);
        var replay = appointment.RegisterPatientConfirmation(t2);

        replay.Outcome.Should().Be(ConfirmationTransitionOutcome.AlreadyConfirmed);
        replay.PreviousState.Should().Be(ConfirmationState.ConfirmedEarly);
        replay.NewState.Should().Be(ConfirmationState.ConfirmedEarly);
        replay.StateChanged.Should().BeFalse();
        replay.ShouldSupersedeHitlTasks.Should().BeFalse();
        replay.ShouldCancelPendingScheduledActions.Should().BeFalse();
        appointment.ConfirmedAt.Should().Be(t1);
    }

    [Fact]
    public void MarkFinalConfirmationRequested_WhenPending_TransitionsOnlyConfirmationDimension()
    {
        var appointment = CreateScheduledAppointment();
        var t1 = DateTimeOffset.UtcNow;

        var result = appointment.MarkFinalConfirmationRequested(t1);

        result.Outcome.Should().Be(ConfirmationTransitionOutcome.FinalRequested);
        result.PreviousState.Should().Be(ConfirmationState.Pending);
        result.NewState.Should().Be(ConfirmationState.FinalRequested);
        result.StateChanged.Should().BeTrue();
        result.ShouldSupersedeHitlTasks.Should().BeFalse();
        result.ShouldCancelPendingScheduledActions.Should().BeFalse();

        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.ConfirmationState.Should().Be(ConfirmationState.FinalRequested);
        appointment.OrchestrationSuppressed.Should().BeFalse();
        appointment.FinalConfirmationRequestedAt.Should().Be(t1);

        var replay = appointment.MarkFinalConfirmationRequested(t1.AddMinutes(5));
        replay.Outcome.Should().Be(ConfirmationTransitionOutcome.AlreadyFinalRequested);
        replay.StateChanged.Should().BeFalse();
    }

    [Fact]
    public void RegisterPatientConfirmation_WhenFinalRequested_TransitionsToFinalConfirmed()
    {
        var appointment = CreateScheduledAppointment();
        var t1 = DateTimeOffset.UtcNow;
        var t2 = t1.AddHours(2);

        appointment.MarkFinalConfirmationRequested(t1);
        var result = appointment.RegisterPatientConfirmation(t2);

        result.Outcome.Should().Be(ConfirmationTransitionOutcome.FinalConfirmed);
        result.PreviousState.Should().Be(ConfirmationState.FinalRequested);
        result.NewState.Should().Be(ConfirmationState.FinalConfirmed);
        result.StateChanged.Should().BeTrue();
        result.ShouldSupersedeHitlTasks.Should().BeTrue();
        result.ShouldCancelPendingScheduledActions.Should().BeTrue();

        appointment.Status.Should().Be(AppointmentStatus.Confirmed);
        appointment.ConfirmationState.Should().Be(ConfirmationState.FinalConfirmed);
        appointment.OrchestrationSuppressed.Should().BeTrue();
        appointment.ConfirmedAt.Should().Be(t2);
    }

    [Fact]
    public void MarkUnresponsive_FromPendingOrFinalRequested_TransitionsToUnresponsive_AndLateConfirmMovesToFinalConfirmed()
    {
        var fromPending = CreateScheduledAppointment();
        var t1 = DateTimeOffset.UtcNow;
        var pendingRes = fromPending.MarkUnresponsive(t1);
        pendingRes.Outcome.Should().Be(ConfirmationTransitionOutcome.MarkedUnresponsive);
        pendingRes.PreviousState.Should().Be(ConfirmationState.Pending);
        pendingRes.NewState.Should().Be(ConfirmationState.Unresponsive);
        fromPending.DeadlineMissedAt.Should().Be(t1);

        var fromFinal = CreateScheduledAppointment();
        fromFinal.MarkFinalConfirmationRequested(t1);
        var t2 = t1.AddHours(12);
        var finalRes = fromFinal.MarkUnresponsive(t2);
        finalRes.Outcome.Should().Be(ConfirmationTransitionOutcome.MarkedUnresponsive);
        finalRes.PreviousState.Should().Be(ConfirmationState.FinalRequested);
        finalRes.NewState.Should().Be(ConfirmationState.Unresponsive);
        fromFinal.Status.Should().Be(AppointmentStatus.Scheduled);
        fromFinal.OrchestrationSuppressed.Should().BeFalse();

        var replay = fromFinal.MarkUnresponsive(t2.AddMinutes(1));
        replay.Outcome.Should().Be(ConfirmationTransitionOutcome.AlreadyUnresponsive);
        replay.StateChanged.Should().BeFalse();

        var lateConfirm = fromFinal.RegisterPatientConfirmation(t2.AddMinutes(15));
        lateConfirm.Outcome.Should().Be(ConfirmationTransitionOutcome.FinalConfirmed);
        lateConfirm.PreviousState.Should().Be(ConfirmationState.Unresponsive);
        lateConfirm.NewState.Should().Be(ConfirmationState.FinalConfirmed);
        fromFinal.Status.Should().Be(AppointmentStatus.Confirmed);
        fromFinal.OrchestrationSuppressed.Should().BeTrue();
    }

    [Fact]
    public void DeclineByPatient_IsTerminalAndIdempotent_CancelsClinicalStatus_AndRejectsLaterConfirmation()
    {
        var appointment = CreateScheduledAppointment();
        var t1 = DateTimeOffset.UtcNow;
        appointment.MarkFinalConfirmationRequested(t1);

        var declined = appointment.DeclineByPatient("No puedo asistir", t1.AddHours(1));
        declined.Outcome.Should().Be(ConfirmationTransitionOutcome.DeclinedByPatient);
        declined.PreviousState.Should().Be(ConfirmationState.FinalRequested);
        declined.NewState.Should().Be(ConfirmationState.DeclinedByPatient);
        declined.StateChanged.Should().BeTrue();
        declined.ShouldSupersedeHitlTasks.Should().BeTrue();
        declined.ShouldCancelPendingScheduledActions.Should().BeTrue();

        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.ConfirmationState.Should().Be(ConfirmationState.DeclinedByPatient);
        appointment.OrchestrationSuppressed.Should().BeTrue();
        appointment.ActualAttendance.Should().BeNull();
        appointment.ConfirmationOutcome.Should().BeNull();

        var replay = appointment.DeclineByPatient("No puedo asistir", t1.AddHours(2));
        replay.Outcome.Should().Be(ConfirmationTransitionOutcome.AlreadyDeclined);
        replay.StateChanged.Should().BeFalse();

        Action confirmAfterDecline = () => appointment.RegisterPatientConfirmation(t1.AddHours(3));
        confirmAfterDecline.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InvalidTransitions_ThrowInvalidOperationException()
    {
        var confirmed = CreateScheduledAppointment();
        confirmed.RegisterPatientConfirmation(DateTimeOffset.UtcNow);
        ((Action)(() => confirmed.MarkFinalConfirmationRequested(DateTimeOffset.UtcNow))).Should().Throw<InvalidOperationException>();
        ((Action)(() => confirmed.MarkUnresponsive(DateTimeOffset.UtcNow))).Should().Throw<InvalidOperationException>();

        var cancelled = CreateScheduledAppointment();
        cancelled.Cancel("Clinica cerro");
        ((Action)(() => cancelled.RegisterPatientConfirmation(DateTimeOffset.UtcNow))).Should().Throw<InvalidOperationException>();
        ((Action)(() => cancelled.DeclineByPatient("Rechazo", DateTimeOffset.UtcNow))).Should().Throw<InvalidOperationException>();
        ((Action)(() => cancelled.MarkFinalConfirmationRequested(DateTimeOffset.UtcNow))).Should().Throw<InvalidOperationException>();
        ((Action)(() => cancelled.MarkUnresponsive(DateTimeOffset.UtcNow))).Should().Throw<InvalidOperationException>();

        var finished = CreateScheduledAppointment();
        finished.StartConsultation();
        finished.FinishConsultation();
        ((Action)(() => finished.RegisterPatientConfirmation(DateTimeOffset.UtcNow))).Should().Throw<InvalidOperationException>();
        ((Action)(() => finished.DeclineByPatient("Rechazo", DateTimeOffset.UtcNow))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AttendanceLifecycle_RecordsAllFourCalibrationOutcomes_AndCancelLeavesOutcomeNull()
    {
        // 1. ConfirmedAndAttended
        var confirmedAttended = CreateScheduledAppointment();
        confirmedAttended.RegisterPatientConfirmation(DateTimeOffset.UtcNow);
        confirmedAttended.CheckIn();
        confirmedAttended.StartConsultation();
        confirmedAttended.FinishConsultation();
        confirmedAttended.ConfirmationOutcome.Should().Be(ConfirmationAttendanceOutcome.ConfirmedAndAttended);

        // 2. ConfirmedButNoShow
        var confirmedNoShow = CreateScheduledAppointment();
        confirmedNoShow.MarkFinalConfirmationRequested(DateTimeOffset.UtcNow);
        confirmedNoShow.RegisterPatientConfirmation(DateTimeOffset.UtcNow.AddMinutes(5));
        confirmedNoShow.MarkNoShow();
        confirmedNoShow.ConfirmationOutcome.Should().Be(ConfirmationAttendanceOutcome.ConfirmedButNoShow);
        confirmedNoShow.OrchestrationSuppressed.Should().BeTrue();

        // 3. UnconfirmedAndAttended (CheckIn moves clinical Status -> Confirmed while ConfirmationState stays Pending)
        var unconfirmedAttended = CreateScheduledAppointment();
        unconfirmedAttended.CheckIn();
        unconfirmedAttended.ConfirmationState.Should().Be(ConfirmationState.Pending);
        unconfirmedAttended.ConfirmationOutcome.Should().Be(ConfirmationAttendanceOutcome.UnconfirmedAndAttended);
        unconfirmedAttended.OrchestrationSuppressed.Should().BeTrue();

        // 4. UnconfirmedAndNoShow
        var unconfirmedNoShow = CreateScheduledAppointment();
        unconfirmedNoShow.MarkUnresponsive(DateTimeOffset.UtcNow);
        unconfirmedNoShow.MarkNoShow();
        unconfirmedNoShow.ConfirmationOutcome.Should().Be(ConfirmationAttendanceOutcome.UnconfirmedAndNoShow);
        unconfirmedNoShow.OrchestrationSuppressed.Should().BeTrue();

        // 5. Cancelled appointment keeps ConfirmationOutcome null
        var cancelledAfterConfirm = CreateScheduledAppointment();
        cancelledAfterConfirm.RegisterPatientConfirmation(DateTimeOffset.UtcNow);
        cancelledAfterConfirm.Cancel("Paciente canceló por recepción");
        cancelledAfterConfirm.ActualAttendance.Should().BeNull();
        cancelledAfterConfirm.ConfirmationOutcome.Should().BeNull();
        cancelledAfterConfirm.OrchestrationSuppressed.Should().BeTrue();
    }
}
