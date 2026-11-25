namespace MedCareOS.Domain.Enums;

public enum RiskBandLevel
{
    Low,
    Medium,
    High
}

public static class RiskBandLevelExtensions
{
    public static string ToLabel(this RiskBandLevel level) => level switch
    {
        RiskBandLevel.Low => "Bajo",
        RiskBandLevel.Medium => "Medio",
        RiskBandLevel.High => "Alto",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Banda de riesgo no soportada.")
    };

    public static bool TryParseLabel(string? label, out RiskBandLevel level)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            level = default;
            return false;
        }

        switch (label.Trim().ToLowerInvariant())
        {
            case "bajo":
            case "low":
                level = RiskBandLevel.Low;
                return true;

            case "medio":
            case "moderado":
            case "medium":
                level = RiskBandLevel.Medium;
                return true;

            case "alto":
            case "critico":
            case "crítico":
            case "high":
                level = RiskBandLevel.High;
                return true;

            default:
                level = default;
                return false;
        }
    }
}
