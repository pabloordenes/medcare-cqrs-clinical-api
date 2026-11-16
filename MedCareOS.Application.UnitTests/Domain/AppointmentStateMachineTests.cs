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
}
