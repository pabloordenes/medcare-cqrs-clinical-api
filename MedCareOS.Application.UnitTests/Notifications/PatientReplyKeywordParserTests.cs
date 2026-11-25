using FluentAssertions;
using MedCareOS.Application.Notifications.Services;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.UnitTests.Notifications;

public class PatientReplyKeywordParserTests
{
    [Fact]
    public void Parse_StructuredQuickReplyPayload_ExtractsIntentAndAppointmentId()
    {
        var appointmentId = Guid.NewGuid();

        var confirm = PatientReplyKeywordParser.Parse($"CONFIRM:{appointmentId}", "Confirmar");
        confirm.Intent.Should().Be(PatientReplyIntent.Confirm);
        confirm.AppointmentId.Should().Be(appointmentId);

        var cancel = PatientReplyKeywordParser.Parse($"CANCEL:{appointmentId}", "Cancelar");
        cancel.Intent.Should().Be(PatientReplyIntent.Cancel);
        cancel.AppointmentId.Should().Be(appointmentId);

        var decline = PatientReplyKeywordParser.Parse($"DECLINE:{appointmentId}", "Declinar");
        decline.Intent.Should().Be(PatientReplyIntent.Cancel);
        decline.AppointmentId.Should().Be(appointmentId);
    }

    [Theory]
    [InlineData("sí", PatientReplyIntent.Confirm)]
    [InlineData("  Si! ", PatientReplyIntent.Confirm)]
    [InlineData("Confirmo", PatientReplyIntent.Confirm)]
    [InlineData("1", PatientReplyIntent.Confirm)]
    [InlineData("no", PatientReplyIntent.Cancel)]
    [InlineData("Cancelo", PatientReplyIntent.Cancel)]
    [InlineData("Declino", PatientReplyIntent.Cancel)]
    [InlineData("declinar", PatientReplyIntent.Cancel)]
    [InlineData("2", PatientReplyIntent.Cancel)]
    [InlineData("STOP", PatientReplyIntent.OptOut)]
    [InlineData("no más", PatientReplyIntent.OptOut)]
    [InlineData("baja", PatientReplyIntent.OptOut)]
    [InlineData("gracias doctor", PatientReplyIntent.Unknown)]
    public void Parse_FreeText_HandlesAccentsCaseAndPunctuation(string text, PatientReplyIntent expected)
    {
        var parsed = PatientReplyKeywordParser.Parse(null, text);
        parsed.Intent.Should().Be(expected);
    }
}
