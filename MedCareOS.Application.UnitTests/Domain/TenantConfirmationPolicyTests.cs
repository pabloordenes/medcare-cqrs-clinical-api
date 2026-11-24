using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.ValueObjects;

namespace MedCareOS.Application.UnitTests.Domain;

public class TenantConfirmationPolicyTests
{
    [Fact]
    public void CreateDefault_ShouldInitializeExpectedTimelineWeightsAndHysteresis()
    {
        var policy = TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId);

        policy.TenantId.Should().Be(RiskThresholds.DefaultTenantId);
        policy.Reminder1OffsetHours.Should().Be(72);
        policy.Reminder2OffsetHours.Should().Be(48);
        policy.FinalConfirmationOffsetHours.Should().Be(24);
        policy.DeadlineOffsetHours.Should().Be(12);
        policy.MinimumDelayAfterCreationMinutes.Should().Be(30);
        policy.SameDayReminderMinLeadHours.Should().Be(3);
        policy.TimeZoneId.Should().Be("America/Santiago");
        policy.QuietHoursStartLocal.Should().BeNull();
        policy.QuietHoursEndLocal.Should().BeNull();
        policy.PrimaryChannelStrategy.Should().Be(PrimaryChannelStrategy.PatientPreferred);
        policy.AlternateChannelStrategy.Should().Be(AlternateChannelStrategy.AlternateIfAvailable);
        policy.EnableRescoreAtReminder2.Should().BeTrue();
        policy.CountSentAsDeliveredForUnanswered.Should().BeFalse();

        policy.WeightConfirmedEarly.Should().Be(-1.5000m);
        policy.WeightReminderUnanswered.Should().Be(0.4000m);
        policy.MaxReminderUnansweredCumulative.Should().Be(1.2000m);
        policy.WeightDeliveryFailed.Should().Be(0.8000m);
        policy.WeightRepliedWithoutConfirmation.Should().Be(0.0000m);
        policy.WeightFinalConfirmed.Should().Be(0.0000m);

        var thresholds = policy.GetHysteresisThresholds();
        thresholds.Should().Be(HysteresisThresholds.Default);
    }

    [Fact]
    public void Update_WithValidCustomValues_ShouldPersistAllProperties()
    {
        var tenantId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;
        var policy = TenantConfirmationPolicy.CreateDefault(tenantId, t0);
        var t1 = t0.AddMinutes(5);

        policy.Update(
            reminder1OffsetHours: 96,
            reminder2OffsetHours: 48,
            finalConfirmationOffsetHours: 30,
            deadlineOffsetHours: 18,
            minimumDelayAfterCreationMinutes: 15,
            sameDayReminderMinLeadHours: 4,
            timeZoneId: "America/Bogota",
            quietHoursStartLocal: new TimeOnly(22, 0),
            quietHoursEndLocal: new TimeOnly(7, 30),
            primaryChannelStrategy: PrimaryChannelStrategy.WhatsAppFirst,
            alternateChannelStrategy: AlternateChannelStrategy.SameChannelOnly,
            enableRescoreAtReminder2: false,
            countSentAsDeliveredForUnanswered: true,
            weightConfirmedEarly: -1.8000m,
            weightReminderUnanswered: 0.3500m,
            maxReminderUnansweredCumulative: 1.0500m,
            weightDeliveryFailed: 0.9000m,
            weightRepliedWithoutConfirmation: 0.0000m,
            weightFinalConfirmed: -0.2500m,
            mediumExitThreshold: 0.35m,
            mediumEnterThreshold: 0.45m,
            highExitThreshold: 0.60m,
            highEnterThreshold: 0.75m,
            utcNow: t1);

        policy.Reminder1Offset.Should().Be(TimeSpan.FromHours(96));
        policy.DeadlineOffset.Should().Be(TimeSpan.FromHours(18));
        policy.TimeZoneId.Should().Be("America/Bogota");
        policy.QuietHoursStartLocal.Should().Be(new TimeOnly(22, 0));
        policy.QuietHoursEndLocal.Should().Be(new TimeOnly(7, 30));
        policy.PrimaryChannelStrategy.Should().Be(PrimaryChannelStrategy.WhatsAppFirst);
        policy.AlternateChannelStrategy.Should().Be(AlternateChannelStrategy.SameChannelOnly);
        policy.EnableRescoreAtReminder2.Should().BeFalse();
        policy.CountSentAsDeliveredForUnanswered.Should().BeTrue();
        policy.UpdatedAt.Should().Be(t1);
    }

    [Theory]
    [InlineData(72, 48, 24, 0)]
    [InlineData(72, 48, 24, 24)]
    [InlineData(48, 48, 24, 12)]
    [InlineData(72, 24, 24, 12)]
    public void Create_WhenOffsetsNotStrictlyDescendingOrPositive_ThrowsArgumentException(
        int r1,
        int r2,
        int fc,
        int dl)
    {
        Action act = () => TenantConfirmationPolicy.Create(
            Guid.NewGuid(), r1, r2, fc, dl, 30, 3, "America/Santiago",
            null, null, PrimaryChannelStrategy.PatientPreferred, AlternateChannelStrategy.AlternateIfAvailable,
            true, false, -1.5m, 0.4m, 1.2m, 0.8m, 0m, 0m,
            0.40m, 0.50m, 0.55m, 0.70m);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0.00, 0.50, 0.55, 0.70)]
    [InlineData(0.50, 0.50, 0.55, 0.70)]
    [InlineData(0.40, 0.60, 0.55, 0.70)]
    [InlineData(0.40, 0.50, 0.70, 0.70)]
    [InlineData(0.40, 0.50, 0.55, 1.00)]
    public void HysteresisThresholds_WhenOrderingViolated_ThrowsArgumentOutOfRangeException(
        decimal medExit,
        decimal medEnter,
        decimal highExit,
        decimal highEnter)
    {
        Action act = () => HysteresisThresholds.Create(medExit, medEnter, highExit, highEnter);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_WhenWeightsOrQuietHoursInvalid_ThrowsExpectedException()
    {
        Action positiveEarlyWeight = () => TenantConfirmationPolicy.Create(
            Guid.NewGuid(), 72, 48, 24, 12, 30, 3, "America/Santiago",
            null, null, PrimaryChannelStrategy.PatientPreferred, AlternateChannelStrategy.AlternateIfAvailable,
            true, false, 0.2m, 0.4m, 1.2m, 0.8m, 0m, 0m,
            0.40m, 0.50m, 0.55m, 0.70m);
        positiveEarlyWeight.Should().Throw<ArgumentOutOfRangeException>();

        Action negativeUnansweredWeight = () => TenantConfirmationPolicy.Create(
            Guid.NewGuid(), 72, 48, 24, 12, 30, 3, "America/Santiago",
            null, null, PrimaryChannelStrategy.PatientPreferred, AlternateChannelStrategy.AlternateIfAvailable,
            true, false, -1.5m, -0.1m, 1.2m, 0.8m, 0m, 0m,
            0.40m, 0.50m, 0.55m, 0.70m);
        negativeUnansweredWeight.Should().Throw<ArgumentOutOfRangeException>();

        Action unpairedQuietHours = () => TenantConfirmationPolicy.Create(
            Guid.NewGuid(), 72, 48, 24, 12, 30, 3, "America/Santiago",
            new TimeOnly(22, 0), null, PrimaryChannelStrategy.PatientPreferred, AlternateChannelStrategy.AlternateIfAvailable,
            true, false, -1.5m, 0.4m, 1.2m, 0.8m, 0m, 0m,
            0.40m, 0.50m, 0.55m, 0.70m);
        unpairedQuietHours.Should().Throw<ArgumentException>();

        Action equalQuietHours = () => TenantConfirmationPolicy.Create(
            Guid.NewGuid(), 72, 48, 24, 12, 30, 3, "America/Santiago",
            new TimeOnly(22, 0), new TimeOnly(22, 0), PrimaryChannelStrategy.PatientPreferred, AlternateChannelStrategy.AlternateIfAvailable,
            true, false, -1.5m, 0.4m, 1.2m, 0.8m, 0m, 0m,
            0.40m, 0.50m, 0.55m, 0.70m);
        equalQuietHours.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Bajo", true, RiskBandLevel.Low)]
    [InlineData("low", true, RiskBandLevel.Low)]
    [InlineData("Medio", true, RiskBandLevel.Medium)]
    [InlineData("Moderado", true, RiskBandLevel.Medium)]
    [InlineData("Alto", true, RiskBandLevel.High)]
    [InlineData("Crítico", true, RiskBandLevel.High)]
    [InlineData("Pendiente", false, default(RiskBandLevel))]
    [InlineData("", false, default(RiskBandLevel))]
    public void RiskBandLevelExtensions_ParsesLabelsAndFormatsCorrectly(
        string label,
        bool expectedParsed,
        RiskBandLevel expectedLevel)
    {
        var ok = RiskBandLevelExtensions.TryParseLabel(label, out var parsed);
        ok.Should().Be(expectedParsed);
        if (expectedParsed)
        {
            parsed.Should().Be(expectedLevel);
            parsed.ToLabel().Should().BeOneOf("Bajo", "Medio", "Alto");
        }
    }
}
