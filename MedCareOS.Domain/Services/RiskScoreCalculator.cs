using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Enums;

namespace MedCareOS.Domain.Services;

public static class RiskScoreCalculator
{
    public const decimal MinProbability = 0.01m;
    public const decimal MaxProbability = 0.99m;

    public static decimal ApplyLogitDelta(decimal probability, decimal logitDelta)
    {
        var clamped = Math.Clamp(probability, MinProbability, MaxProbability);
        var p = (double)clamped;
        var logit = Math.Log(p / (1.0 - p)) + (double)logitDelta;
        var transformed = 1.0 / (1.0 + Math.Exp(-logit));

        var rounded = Math.Round((decimal)transformed, 4, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, 0.0001m, 0.9999m);
    }

    public static decimal ComputeEffectiveScore(
        decimal baseModelProbability,
        IEnumerable<RiskSignal> signals,
        decimal maxReminderUnansweredCumulative)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (maxReminderUnansweredCumulative < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxReminderUnansweredCumulative),
                maxReminderUnansweredCumulative,
                "El tope acumulativo de ReminderUnanswered no puede ser negativo.");
        }

        decimal unansweredSum = 0m;
        decimal otherSum = 0m;

        foreach (var signal in signals)
        {
            if (signal.Kind == RiskSignalKind.ModelRescored)
                continue;

            if (signal.Kind == RiskSignalKind.ReminderUnanswered)
            {
                unansweredSum += signal.Weight;
            }
            else
            {
                otherSum += signal.Weight;
            }
        }

        var cappedUnanswered = Math.Min(unansweredSum, maxReminderUnansweredCumulative);
        var totalDelta = cappedUnanswered + otherSum;

        if (totalDelta == 0m)
            return Math.Round(Math.Clamp(baseModelProbability, 0m, 1m), 4, MidpointRounding.AwayFromZero);

        return ApplyLogitDelta(baseModelProbability, totalDelta);
    }
}
