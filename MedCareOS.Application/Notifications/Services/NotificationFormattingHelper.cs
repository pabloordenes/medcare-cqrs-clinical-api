using System.Globalization;

namespace MedCareOS.Application.Notifications.Services;

public static class NotificationFormattingHelper
{
    private static readonly CultureInfo ChileCulture = CultureInfo.GetCultureInfo("es-CL");

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var candidate = string.IsNullOrWhiteSpace(timeZoneId) ? "America/Santiago" : timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(candidate);
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows fallback for America/Santiago
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Pacific SA Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }

    public static (string DateLocal, string TimeLocal) FormatAppointmentDateTime(DateTime appointmentStartUtc, string? timeZoneId)
    {
        var utc = appointmentStartUtc.Kind == DateTimeKind.Utc
            ? appointmentStartUtc
            : DateTime.SpecifyKind(appointmentStartUtc, DateTimeKind.Utc);

        var tz = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);

        var dateStr = local.ToString("dd/MM/yyyy", ChileCulture);
        var timeStr = local.ToString("HH:mm", ChileCulture) + " hrs";
        return (dateStr, timeStr);
    }

    public static string FormatDeadline(DateTimeOffset deadlineUtc, string? timeZoneId)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(deadlineUtc.UtcDateTime, tz);
        return local.ToString("dd/MM/yyyy HH:mm", ChileCulture) + " hrs";
    }

    public static string ResolveSpecialtyDisplay(
        string? rawSpecialty,
        bool includeSpecialty,
        IEnumerable<string>? sensitiveExcluded)
    {
        if (!includeSpecialty || string.IsNullOrWhiteSpace(rawSpecialty))
            return "atención médica";

        var normalized = rawSpecialty.Trim();
        if (sensitiveExcluded is not null &&
            sensitiveExcluded.Any(s => string.Equals(s?.Trim(), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return "atención médica";
        }

        return normalized;
    }
}
