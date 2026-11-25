using FluentAssertions;
using MedCareOS.Domain.Common;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.UnitTests.Domain;

public class PatientNotificationPreferencesTests
{
    [Fact]
    public void Create_WithEmailAndNoWhatsAppOptIn_ResolvesEmailChannel()
    {
        var patient = Patient.Create(
            Guid.NewGuid(),
            "12.345.678-9",
            "Ana",
            "Lopez",
            new DateOnly(1992, 5, 10),
            "femenino",
            "+56911111111",
            email: "ana@test.cl");

        patient.PreferredChannel.Should().Be(NotificationChannel.Email);
        patient.WhatsAppOptIn.Should().BeFalse();
        patient.GetEffectiveReminderChannel().Should().Be(NotificationChannel.Email);
        patient.ToPredictReminderChannel().Should().Be("email");
    }

    [Fact]
    public void Create_WithoutEmailAndNoWhatsAppOptIn_ResolvesNinguno()
    {
        var patient = Patient.Create(
            Guid.NewGuid(),
            "12.345.678-9",
            "Ana",
            "Lopez",
            new DateOnly(1992, 5, 10),
            "femenino",
            "+56911111111",
            email: null);

        patient.GetEffectiveReminderChannel().Should().BeNull();
        patient.ToPredictReminderChannel().Should().Be("ninguno");
    }

    [Fact]
    public void UpdateNotificationPreferences_EnablingWhatsApp_SetsOptInAuditAndResolvesWhatsApp()
    {
        var patient = Patient.Create(
            Guid.NewGuid(),
            "12.345.678-9",
            "Carlos",
            "Soto",
            new DateOnly(1988, 3, 12),
            "masculino",
            "912345678",
            email: "carlos@test.cl");

        var now = new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);
        patient.UpdateNotificationPreferences(
            NotificationChannel.WhatsApp,
            whatsAppOptIn: true,
            source: "patient_portal",
            utcNow: now);

        patient.PreferredChannel.Should().Be(NotificationChannel.WhatsApp);
        patient.WhatsAppOptIn.Should().BeTrue();
        patient.WhatsAppOptInAt.Should().Be(now);
        patient.WhatsAppOptInSource.Should().Be("patient_portal");
        patient.GetEffectiveReminderChannel().Should().Be(NotificationChannel.WhatsApp);
        patient.ToPredictReminderChannel().Should().Be("whatsapp");
    }

    [Fact]
    public void UpdateNotificationPreferences_PreferringWhatsAppWithoutOptIn_ThrowsInvalidOperationException()
    {
        var patient = Patient.Create(
            Guid.NewGuid(),
            "12.345.678-9",
            "Carlos",
            "Soto",
            new DateOnly(1988, 3, 12),
            "masculino",
            "+56912345678",
            email: "carlos@test.cl");

        Action act = () => patient.UpdateNotificationPreferences(
            NotificationChannel.WhatsApp,
            whatsAppOptIn: false,
            source: "patient_portal",
            utcNow: DateTimeOffset.UtcNow);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OptOut_ClearsWhatsAppOptIn_SetsOptOutTimestamp_AndResolvesNinguno()
    {
        var patient = Patient.Create(
            Guid.NewGuid(),
            "12.345.678-9",
            "Laura",
            "Diaz",
            new DateOnly(1990, 1, 20),
            "femenino",
            "+56987654321",
            email: "laura@test.cl",
            preferredChannel: NotificationChannel.WhatsApp,
            whatsAppOptIn: true,
            consentSource: "web_booking_checkbox");

        var optOutTime = new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero);
        patient.OptOut("whatsapp_stop", optOutTime);

        patient.HasOptedOut().Should().BeTrue();
        patient.OptOutAt.Should().Be(optOutTime);
        patient.OptOutSource.Should().Be("whatsapp_stop");
        patient.WhatsAppOptIn.Should().BeFalse();
        patient.GetEffectiveReminderChannel().Should().BeNull();
        patient.ToPredictReminderChannel().Should().Be("ninguno");
    }

    [Theory]
    [InlineData("912345678", "+56912345678", "56912345678")]
    [InlineData("+56 9 1234 5678", "+56912345678", "56912345678")]
    [InlineData("56912345678", "+56912345678", "56912345678")]
    [InlineData("0912345678", "+56912345678", "56912345678")]
    [InlineData("123", null, null)]
    public void PhoneNormalizer_NormalizesChileanNumbersCorrectly(string input, string? expectedE164, string? expectedDigits)
    {
        PhoneNormalizer.NormalizeToE164Chile(input).Should().Be(expectedE164);
        PhoneNormalizer.ToWhatsAppDigits(input).Should().Be(expectedDigits);
    }
}
