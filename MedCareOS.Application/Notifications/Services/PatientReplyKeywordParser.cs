using System.Globalization;
using System.Text;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Application.Notifications.Services;

public sealed record ParsedPatientReply(
    PatientReplyIntent Intent,
    Guid? AppointmentId,
    string NormalizedKeyword,
    Guid? WaitingListOfferId = null);

public static class PatientReplyKeywordParser
{
    private static readonly HashSet<string> ConfirmKeywords = new(StringComparer.Ordinal)
    {
        "SI",
        "CONFIRMO",
        "CONFIRMAR",
        "1",
        "ASISTO",
        "ACEPTO",
        "TOMAR",
        "TOMAR CUPO"
    };

    private static readonly HashSet<string> CancelKeywords = new(StringComparer.Ordinal)
    {
        "NO",
        "CANCELO",
        "CANCELAR",
        "DECLINO",
        "DECLINAR",
        "2",
        "NO ASISTO",
        "RECHAZO",
        "RECHAZAR"
    };

    private static readonly HashSet<string> OptOutKeywords = new(StringComparer.Ordinal)
    {
        "STOP",
        "BAJA",
        "NO MAS",
        "DESUSCRIBIR",
        "SALIR",
        "UNSUBSCRIBE"
    };

    public static ParsedPatientReply Parse(string? payload, string? textBody)
    {
        if (!string.IsNullOrWhiteSpace(payload))
        {
            var trimmedPayload = payload.Trim();
            if (TryParseStructuredPayload(trimmedPayload, "CONFIRM:", PatientReplyIntent.Confirm, out var confirmParsed))
                return confirmParsed;

            if (TryParseStructuredPayload(trimmedPayload, "CANCEL:", PatientReplyIntent.Cancel, out var cancelParsed))
                return cancelParsed;

            if (TryParseStructuredPayload(trimmedPayload, "DECLINE:", PatientReplyIntent.Cancel, out var declineParsed))
                return declineParsed;

            if (TryParseOfferPayload(trimmedPayload, "ACCEPT_OFFER:", PatientReplyIntent.Confirm, out var acceptOfferParsed))
                return acceptOfferParsed;

            if (TryParseOfferPayload(trimmedPayload, "DECLINE_OFFER:", PatientReplyIntent.Cancel, out var declineOfferParsed))
                return declineOfferParsed;

            var payloadKeyword = NormalizeText(trimmedPayload);
            var payloadIntent = ClassifyKeyword(payloadKeyword);
            if (payloadIntent != PatientReplyIntent.Unknown)
                return new ParsedPatientReply(payloadIntent, null, payloadKeyword);
        }

        var normalizedText = NormalizeText(textBody);
        var textIntent = ClassifyKeyword(normalizedText);
        return new ParsedPatientReply(textIntent, null, normalizedText);
    }

    private static bool TryParseOfferPayload(
        string payload,
        string prefix,
        PatientReplyIntent intent,
        out ParsedPatientReply parsed)
    {
        if (payload.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = payload[prefix.Length..].Trim();
            Guid? offerId = Guid.TryParse(suffix, out var guid) && guid != Guid.Empty ? guid : null;
            parsed = new ParsedPatientReply(intent, null, prefix.TrimEnd(':').ToUpperInvariant(), offerId);
            return true;
        }

        parsed = default!;
        return false;
    }

    private static bool TryParseStructuredPayload(
        string payload,
        string prefix,
        PatientReplyIntent intent,
        out ParsedPatientReply parsed)
    {
        if (payload.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = payload[prefix.Length..].Trim();
            Guid? appointmentId = Guid.TryParse(suffix, out var guid) && guid != Guid.Empty ? guid : null;
            parsed = new ParsedPatientReply(intent, appointmentId, prefix.TrimEnd(':').ToUpperInvariant());
            return true;
        }

        parsed = default!;
        return false;
    }

    private static PatientReplyIntent ClassifyKeyword(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return PatientReplyIntent.Unknown;

        if (OptOutKeywords.Contains(normalized))
            return PatientReplyIntent.OptOut;

        if (ConfirmKeywords.Contains(normalized))
            return PatientReplyIntent.Confirm;

        if (CancelKeywords.Contains(normalized))
            return PatientReplyIntent.Cancel;

        return PatientReplyIntent.Unknown;
    }

    public static string NormalizeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var decomposed = input.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    previousWasSpace = true;
                }
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
                previousWasSpace = false;
            }
        }

        return sb.ToString().Trim();
    }
}
