using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Services;

namespace MedCareOS.Application.UnitTests.Domain;

public class ConfirmationTimelinePlannerTests
{
    private static TenantConfirmationPolicy DefaultPolicy =>
        TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId);

    [Fact]
    public void Plan_WhenLeadGreaterThan72Hours_SchedulesFullFourStepTimeline()
    {
        var created = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        var start = created.AddDays(5); // 120h

        var plan = ConfirmationTimelinePlanner.Plan(start, created, DefaultPolicy);

        plan.Should().HaveCount(4);
        plan.Select(p => p.Kind).Should().ContainInOrder(
            ScheduledActionKind.Reminder1,
            ScheduledActionKind.Reminder2AndRescore,
            ScheduledActionKind.FinalConfirmation,
            ScheduledActionKind.ConfirmationDeadline);

        plan[0].DueAt.Should().Be(start.AddHours(-72));
        plan[1].DueAt.Should().Be(start.AddHours(-48));
        plan[2].DueAt.Should().Be(start.AddHours(-24));
        plan[3].DueAt.Should().Be(start.AddHours(-12));
        plan.All(p => !p.IsCompressed).Should().BeTrue();
    }

    [Fact]
    public void Plan_WhenLeadIs60Hours_SkipsReminder1AndSchedulesRemainingThree()
    {
        var created = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        var start = created.AddHours(60);

        var plan = ConfirmationTimelinePlanner.Plan(start, created, DefaultPolicy);

        plan.Select(p => p.Kind).Should().ContainInOrder(
            ScheduledActionKind.Reminder2AndRescore,
            ScheduledActionKind.FinalConfirmation,
            ScheduledActionKind.ConfirmationDeadline);

        plan[0].DueAt.Should().Be(start.AddHours(-48));
        plan[1].DueAt.Should().Be(start.AddHours(-24));
        plan[2].DueAt.Should().Be(start.AddHours(-12));
    }

    [Fact]
    public void Plan_WhenLeadIs36Hours_SkipsReminder1AndReminder2()
    {
        var created = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        var start = created.AddHours(36);

        var plan = ConfirmationTimelinePlanner.Plan(start, created, DefaultPolicy);

        plan.Select(p => p.Kind).Should().ContainInOrder(
            ScheduledActionKind.FinalConfirmation,
            ScheduledActionKind.ConfirmationDeadline);

        plan[0].DueAt.Should().Be(start.AddHours(-24));
        plan[0].IsCompressed.Should().BeFalse();
        plan[1].DueAt.Should().Be(start.AddHours(-12));
    }

    [Fact]
    public void Plan_WhenLeadIs18Hours_CompressesFinalConfirmationBeforeDeadline()
    {
        var created = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        var start = created.AddHours(18);
        var tDL = start.AddHours(-12); // created + 6h

        var plan = ConfirmationTimelinePlanner.Plan(start, created, DefaultPolicy);

        plan.Should().HaveCount(2);
        plan[0].Kind.Should().Be(ScheduledActionKind.FinalConfirmation);
        plan[0].IsCompressed.Should().BeTrue();
        plan[0].DueAt.Should().Be(created.AddHours(2)); // min(6h / 2, 2h) = 2h
        plan[0].DueAt.Should().BeBefore(tDL);

        plan[1].Kind.Should().Be(ScheduledActionKind.ConfirmationDeadline);
        plan[1].DueAt.Should().Be(tDL);
    }

    [Fact]
    public void Plan_WhenSameDay10Hours_NeverSchedulesRetroactiveDeadline_AndSchedulesCompressedFinalConfirmation()
    {
        var created = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var start = created.AddHours(10);

        var plan = ConfirmationTimelinePlanner.Plan(start, created, DefaultPolicy);

        plan.Should().ContainSingle();
        plan[0].Kind.Should().Be(ScheduledActionKind.FinalConfirmation);
        plan[0].IsCompressed.Should().BeTrue();
        plan[0].DueAt.Should().BeAfter(created);
        plan[0].DueAt.Should().BeBefore(start);
        plan.Should().NotContain(p => p.Kind == ScheduledActionKind.ConfirmationDeadline);
    }

    [Fact]
    public void Plan_WhenUrgent2Hours_ReturnsEmptyTimeline()
    {
        var created = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
        var start = created.AddHours(2);

        var plan = ConfirmationTimelinePlanner.Plan(start, created, DefaultPolicy);

        plan.Should().BeEmpty();
    }

    [Fact]
    public void Plan_WhenQuietHoursConfigured_ShiftsReminderOutOfNightWindowWithoutMovingDeadline()
    {
        var policy = TenantConfirmationPolicy.Create(
            tenantId: RiskThresholds.DefaultTenantId,
            reminder1OffsetHours: 72,
            reminder2OffsetHours: 48,
            finalConfirmationOffsetHours: 24,
            deadlineOffsetHours: 16, // 09:00 local -> deadline 17:00 día anterior
            minimumDelayAfterCreationMinutes: 30,
            sameDayReminderMinLeadHours: 3,
            timeZoneId: "UTC",
            quietHoursStartLocal: new TimeOnly(22, 0),
            quietHoursEndLocal: new TimeOnly(8, 0),
            primaryChannelStrategy: PrimaryChannelStrategy.PatientPreferred,
            alternateChannelStrategy: AlternateChannelStrategy.AlternateIfAvailable,
            enableRescoreAtReminder2: true,
            countSentAsDeliveredForUnanswered: false,
            weightConfirmedEarly: -1.5m,
            weightReminderUnanswered: 0.4m,
            maxReminderUnansweredCumulative: 1.2m,
            weightDeliveryFailed: 0.8m,
            weightRepliedWithoutConfirmation: 0m,
            weightFinalConfirmed: 0m,
            mediumExitThreshold: 0.40m,
            mediumEnterThreshold: 0.50m,
            highExitThreshold: 0.55m,
            highEnterThreshold: 0.70m);

        var start = new DateTimeOffset(2026, 11, 25, 2, 0, 0, TimeSpan.Zero); // 02:00 UTC (dentro de quiet hours 22:00-08:00)
        var created = start.AddDays(-5);

        var plan = ConfirmationTimelinePlanner.Plan(start, created, policy);

        plan.Should().HaveCount(4);
        // R1/R2/FC originalmente a las 02:00 UTC deben desplazarse a las 08:00 UTC del mismo día
        plan[0].DueAt.Hour.Should().Be(8);
        plan[1].DueAt.Hour.Should().Be(8);
        plan[2].DueAt.Hour.Should().Be(8);
        // Deadline queda exactamente en T - 16h = 10:00 UTC del día anterior
        plan[3].Kind.Should().Be(ScheduledActionKind.ConfirmationDeadline);
        plan[3].DueAt.Should().Be(start.AddHours(-16));
    }

    [Fact]
    public void Plan_For0900AppointmentWith16hDeadline_PlacesDeadlineAt1700PreviousDay()
    {
        var policy = TenantConfirmationPolicy.Create(
            tenantId: Guid.NewGuid(),
            reminder1OffsetHours: 72,
            reminder2OffsetHours: 48,
            finalConfirmationOffsetHours: 24,
            deadlineOffsetHours: 16,
            minimumDelayAfterCreationMinutes: 30,
            sameDayReminderMinLeadHours: 3,
            timeZoneId: "America/Santiago",
            quietHoursStartLocal: null,
            quietHoursEndLocal: null,
            primaryChannelStrategy: PrimaryChannelStrategy.PatientPreferred,
            alternateChannelStrategy: AlternateChannelStrategy.AlternateIfAvailable,
            enableRescoreAtReminder2: true,
            countSentAsDeliveredForUnanswered: false,
            weightConfirmedEarly: -1.5m,
            weightReminderUnanswered: 0.4m,
            maxReminderUnansweredCumulative: 1.2m,
            weightDeliveryFailed: 0.8m,
            weightRepliedWithoutConfirmation: 0m,
            weightFinalConfirmed: 0m,
            mediumExitThreshold: 0.40m,
            mediumEnterThreshold: 0.50m,
            highExitThreshold: 0.55m,
            highEnterThreshold: 0.70m);

        var tz = ConfirmationTimelinePlanner.ResolveTimeZone(policy.TimeZoneId);
        var localAppointment = new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Unspecified);
        var appointmentUtc = new DateTimeOffset(localAppointment, tz.GetUtcOffset(localAppointment)).ToUniversalTime();
        var createdUtc = appointmentUtc.AddDays(-4);

        var plan = ConfirmationTimelinePlanner.Plan(appointmentUtc, createdUtc, policy);
        var deadline = plan.Single(p => p.Kind == ScheduledActionKind.ConfirmationDeadline);
        var deadlineLocal = TimeZoneInfo.ConvertTime(deadline.DueAt.UtcDateTime, tz);

        deadlineLocal.Hour.Should().Be(17);
        deadlineLocal.Minute.Should().Be(0);
    }
}
