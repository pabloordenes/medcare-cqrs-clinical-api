using FluentAssertions;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.UnitTests.Domain;

public class ScheduleBlockOverbookTests
{
    [Fact]
    public void CreateOverbook_ShouldNotInheritRuleProperties_AndSetSourceAppointmentIdAndOpenStatus()
    {
        var start = DateTime.UtcNow.AddDays(1);
        var end = start.AddHours(1);
        var source = ScheduleBlock.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            start,
            end,
            Guid.NewGuid(),
            DateOnly.FromDateTime(start),
            DateOnly.FromDateTime(start.AddDays(7)),
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            [start.DayOfWeek]);
        var appointmentId = Guid.NewGuid();

        var overbook = ScheduleBlock.CreateOverbook(source, appointmentId);

        overbook.IsOverbook.Should().BeTrue();
        overbook.OverbookStatus.Should().Be(OverbookBlockStatus.Open);
        overbook.IsActiveOverbook.Should().BeTrue();
        overbook.IsAvailableForBackfillOffer.Should().BeTrue();
        overbook.SourceAppointmentId.Should().Be(appointmentId);
        overbook.StaffId.Should().Be(source.StaffId);
        overbook.BoxId.Should().Be(source.BoxId);
        overbook.StartTime.Should().Be(source.StartTime);
        overbook.EndTime.Should().Be(source.EndTime);
        overbook.RuleGroupId.Should().BeNull();
        overbook.RuleStartDate.Should().BeNull();
        overbook.RuleEndDate.Should().BeNull();
        overbook.RuleStartTime.Should().BeNull();
        overbook.RuleEndTime.Should().BeNull();
        overbook.RuleDaysOfWeek.Should().BeNull();
    }

    [Fact]
    public void CreateOverbook_FromAnotherOverbook_ShouldThrowInvalidOperationException()
    {
        var start = DateTime.UtcNow.AddDays(1);
        var source = ScheduleBlock.Create(
            Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1),
            Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start.AddDays(1)),
            new TimeOnly(9, 0), new TimeOnly(10, 0), [start.DayOfWeek]);
        var overbook = ScheduleBlock.CreateOverbook(source, Guid.NewGuid());

        Action act = () => ScheduleBlock.CreateOverbook(overbook, Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OverbookLifecycle_OpenToOfferedToOccupied_ShouldUpdateConcurrencyStampAndBlockDeactivation()
    {
        var now = DateTimeOffset.UtcNow;
        var overbook = CreateOverbookBlock(now);
        var stamp0 = overbook.ConcurrencyStamp;

        overbook.MarkOverbookOffered(now.AddMinutes(1));
        overbook.OverbookStatus.Should().Be(OverbookBlockStatus.Offered);
        overbook.ConcurrencyStamp.Should().NotBe(stamp0);

        var stamp1 = overbook.ConcurrencyStamp;
        var overbookAppointmentId = Guid.NewGuid();
        overbook.MarkOverbookOccupied(overbookAppointmentId, now.AddMinutes(2));

        overbook.OverbookStatus.Should().Be(OverbookBlockStatus.Occupied);
        overbook.OccupiedAppointmentId.Should().Be(overbookAppointmentId);
        overbook.IsActiveOverbook.Should().BeTrue();
        overbook.IsAvailableForBackfillOffer.Should().BeFalse();
        overbook.ConcurrencyStamp.Should().NotBe(stamp1);

        Action deactivateOccupied = () => overbook.DeactivateOverbook("LateConfirmationUndo", now.AddMinutes(3));
        deactivateOccupied.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OverbookLifecycle_DeactivateAndExpire_ShouldBeIdempotentAndBlockReuse()
    {
        var now = DateTimeOffset.UtcNow;
        var overbook1 = CreateOverbookBlock(now);
        overbook1.MarkOverbookOffered(now.AddMinutes(1));

        var firstDeactivate = overbook1.DeactivateOverbook("LateConfirmationUndo", now.AddMinutes(2));
        var secondDeactivate = overbook1.DeactivateOverbook("LateConfirmationUndo", now.AddMinutes(3));

        firstDeactivate.Should().BeTrue();
        secondDeactivate.Should().BeFalse();
        overbook1.OverbookStatus.Should().Be(OverbookBlockStatus.Deactivated);
        overbook1.DeactivationReason.Should().Be("LateConfirmationUndo");
        overbook1.DeactivatedAt.Should().Be(now.AddMinutes(2));
        overbook1.IsActiveOverbook.Should().BeFalse();

        var overbook2 = CreateOverbookBlock(now);
        overbook2.MarkOverbookOffered(now.AddMinutes(1));
        overbook2.RevertOverbookToOpen(now.AddMinutes(2));
        overbook2.OverbookStatus.Should().Be(OverbookBlockStatus.Open);

        overbook2.ExpireOverbook(now.AddMinutes(3));
        overbook2.OverbookStatus.Should().Be(OverbookBlockStatus.Expired);
        overbook2.IsActiveOverbook.Should().BeFalse();
    }

    private static ScheduleBlock CreateOverbookBlock(DateTimeOffset now)
    {
        var start = now.UtcDateTime.AddDays(2);
        var source = ScheduleBlock.Create(
            Guid.NewGuid(), Guid.NewGuid(), start, start.AddHours(1),
            Guid.NewGuid(), DateOnly.FromDateTime(start), DateOnly.FromDateTime(start.AddDays(1)),
            new TimeOnly(10, 0), new TimeOnly(11, 0), [start.DayOfWeek]);
        return ScheduleBlock.CreateOverbook(source, Guid.NewGuid());
    }
}
