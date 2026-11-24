using System.Text;

namespace MedCareOS.Domain.Common;

public static class PhoneNormalizer
{
    public static string? NormalizeToE164Chile(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            return null;

        var trimmed = rawPhone.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = ExtractDigits(trimmed);

        if (digits.Length == 0)
            return null;

        // Chile 9-digit mobile: 9XXXXXXXX -> +569XXXXXXXX
        if (digits.Length == 9 && digits.StartsWith('9'))
            return $"+56{digits}";

        // Chile 09XXXXXXXX -> +569XXXXXXXX
        if (digits.Length == 10 && digits.StartsWith("09", StringComparison.Ordinal))
            return $"+56{digits[1..]}";

        // Chile 569XXXXXXXX (11 digits) -> +569XXXXXXXX
        if (digits.Length == 11 && digits.StartsWith("569", StringComparison.Ordinal))
            return $"+{digits}";

        // Chile 00569XXXXXXXX -> +569XXXXXXXX
        if (digits.Length == 13 && digits.StartsWith("00569", StringComparison.Ordinal))
            return $"+{digits[2..]}";

        // Other international E.164 number with explicit '+' (8..15 digits)
        if (hasPlus && digits.Length is >= 8 and <= 15)
            return $"+{digits}";

        // Fallback if already country-prefixed digits (10..15 digits)
        if (digits.Length is >= 10 and <= 15)
            return $"+{digits}";

        return null;
    }

    public static string? ToWhatsAppDigits(string? rawPhone)
    {
        var e164 = NormalizeToE164Chile(rawPhone);
        return e164 is null ? null : e164.TrimStart('+');
    }

    public static string ExtractDigits(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            return string.Empty;

        var sb = new StringBuilder(rawPhone.Length);
        foreach (var ch in rawPhone)
        {
            if (char.IsAsciiDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }
}
