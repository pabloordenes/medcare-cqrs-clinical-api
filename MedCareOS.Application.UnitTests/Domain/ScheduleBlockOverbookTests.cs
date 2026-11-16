using FluentAssertions;
using MedCareOS.Domain.Entities;

namespace MedCareOS.Application.UnitTests.Domain;

public class ScheduleBlockOverbookTests
{
    [Fact]
    public void CreateOverbook_ShouldNotInheritRuleProperties_AndSetSourceAppointmentId()
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
}
