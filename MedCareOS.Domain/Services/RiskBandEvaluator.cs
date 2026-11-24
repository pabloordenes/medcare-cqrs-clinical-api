using MedCareOS.Domain.Enums;
using MedCareOS.Domain.ValueObjects;

namespace MedCareOS.Domain.Services;

public static class RiskBandEvaluator
{
    public static RiskBandLevel EvaluateInitial(decimal score, HysteresisThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        if (score >= thresholds.HighEnter)
            return RiskBandLevel.High;

        if (score >= thresholds.MediumEnter)
            return RiskBandLevel.Medium;

        return RiskBandLevel.Low;
    }

    public static RiskBandLevel Evaluate(
        decimal score,
        RiskBandLevel? currentBand,
        HysteresisThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        if (!currentBand.HasValue)
            return EvaluateInitial(score, thresholds);

        return currentBand.Value switch
        {
            RiskBandLevel.Low => score >= thresholds.HighEnter
                ? RiskBandLevel.High
                : (score >= thresholds.MediumEnter ? RiskBandLevel.Medium : RiskBandLevel.Low),

            RiskBandLevel.Medium => score >= thresholds.HighEnter
                ? RiskBandLevel.High
                : (score < thresholds.MediumExit ? RiskBandLevel.Low : RiskBandLevel.Medium),

            RiskBandLevel.High => score < thresholds.MediumExit
                ? RiskBandLevel.Low
                : (score < thresholds.HighExit ? RiskBandLevel.Medium : RiskBandLevel.High),

            _ => throw new ArgumentOutOfRangeException(nameof(currentBand), currentBand, "Banda de riesgo no soportada.")
        };
    }
}
