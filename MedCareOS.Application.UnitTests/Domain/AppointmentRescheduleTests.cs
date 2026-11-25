using FluentAssertions;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.UnitTests.Domain;

public class AppointmentRescheduleTests
{
    [Fact]
    public void Reschedule_ShouldResetConfirmationState_IncrementCount_AndEnforceLimitUnlessDisplaced()
    {
        var now = new DateTimeOffset(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
        var block1 = Guid.NewGuid();
        var block2 = Guid.NewGuid();
        var block3 = Guid.NewGuid();
        var block4 = Guid.NewGuid();

        var app = Appointment.Create(block1, Guid.NewGuid(), "Control");
        app.RegisterPatientConfirmation(now);
        app.ConfirmationState.Should().Be(ConfirmationState.ConfirmedEarly);
        app.Status.Should().Be(AppointmentStatus.Confirmed);

        // Primer reagendamiento
        app.Reschedule(block2, now.AddMinutes(5), maxReschedules: 2);
        app.ScheduleBlockId.Should().Be(block2);
        app.Status.Should().Be(AppointmentStatus.Scheduled);
        app.ConfirmationState.Should().Be(ConfirmationState.Pending);
        app.ConfirmedAt.Should().BeNull();
        app.OrchestrationSuppressed.Should().BeFalse();
        app.ReschedulesCount.Should().Be(1);

        // Segundo reagendamiento (alcanza el límite = 2)
        app.Reschedule(block3, now.AddMinutes(10), maxReschedules: 2);
        app.ReschedulesCount.Should().Be(2);

        // Tercer reagendamiento sin desplazamiento falla
        Action thirdNormal = () => app.Reschedule(block4, now.AddMinutes(15), maxReschedules: 2);
        thirdNormal.Should().Throw<InvalidOperationException>();

        // Al marcarse como desplazado por sobrecupo, omite el límite
        app.MarkDisplacedByOverbook(now.AddMinutes(20));
        app.Reschedule(block4, now.AddMinutes(25), maxReschedules: 2, bypassLimitIfDisplaced: true);
        app.ScheduleBlockId.Should().Be(block4);
        app.ReschedulesCount.Should().Be(3);
        app.IsDisplacedByOverbook.Should().BeFalse();
        app.WasEverDisplacedByOverbook.Should().BeTrue();
    }

    [Fact]
    public void Cancel_WithReason_ShouldStoreTruncatedReasonAndCancelledTimestamp()
    {
        var now = new DateTimeOffset(2026, 11, 25, 11, 0, 0, TimeSpan.Zero);
        var app = Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), "Control");
        var longReason = new string('X', 350);

        app.Cancel(longReason, now);

        app.Status.Should().Be(AppointmentStatus.Cancelled);
        app.CancelledAt.Should().Be(now);
        app.CancellationReason.Should().HaveLength(Appointment.MaxCancellationReasonLength);
    }
}
