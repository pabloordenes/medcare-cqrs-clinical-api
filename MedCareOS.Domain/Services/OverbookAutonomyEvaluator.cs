using MedCareOS.Domain.Entities;

namespace MedCareOS.Domain.Services;

public sealed record OverbookAutonomyEvaluation(
    bool IsEligibleForAutonomy,
    int EvaluatedWouldOverbookCount,
    int FalsePositivesCount,
    int TruePositivesCount,
    int MinimumRequiredEvaluations,
    decimal MaxAllowedFalsePositiveRate,
    decimal PointFalsePositiveRate,
    decimal WilsonUpperBound95,
    IReadOnlyList<string> BlockingReasons);

public static class OverbookAutonomyEvaluator
{
    private const double Z95 = 1.96;

    public static OverbookAutonomyEvaluation Evaluate(
        int evaluatedWouldOverbookCount,
        int falsePositivesCount,
        TenantConfirmationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var n = Math.Max(0, evaluatedWouldOverbookCount);
        var fp = Math.Clamp(falsePositivesCount, 0, n);
        var tp = n - fp;

        decimal pointFpr = n == 0
            ? 0m
            : Math.Round((decimal)fp / n, 4, MidpointRounding.AwayFromZero);

        decimal wilsonUpper = ComputeWilsonUpperBound95(n, fp);

        var reasons = new List<string>();
        if (n < policy.MinShadowEvaluationsForActivation)
        {
            reasons.Add(
                $"Evaluaciones sombra insuficientes: {n} de {policy.MinShadowEvaluationsForActivation} requeridas.");
        }

        if (n > 0 && pointFpr >= policy.MaxFalsePositiveRateForActivation)
        {
            reasons.Add(
                $"Tasa de falsos positivos ({pointFpr:P2}) supera o iguala el umbral permitido (< {policy.MaxFalsePositiveRateForActivation:P2}).");
        }

        return new OverbookAutonomyEvaluation(
            IsEligibleForAutonomy: reasons.Count == 0,
            EvaluatedWouldOverbookCount: n,
            FalsePositivesCount: fp,
            TruePositivesCount: tp,
            MinimumRequiredEvaluations: policy.MinShadowEvaluationsForActivation,
            MaxAllowedFalsePositiveRate: policy.MaxFalsePositiveRateForActivation,
            PointFalsePositiveRate: pointFpr,
            WilsonUpperBound95: wilsonUpper,
            BlockingReasons: reasons);
    }

    public static decimal ComputeWilsonUpperBound95(int n, int positives)
    {
        if (n <= 0)
            return 1.0000m;

        double p = (double)positives / n;
        double z2 = Z95 * Z95;
        double denom = 1.0 + (z2 / n);
        double center = p + (z2 / (2.0 * n));
        double halfWidth = Z95 * Math.Sqrt(((p * (1.0 - p)) / n) + (z2 / (4.0 * n * n)));
        double upper = Math.Min(1.0, (center + halfWidth) / denom);

        return Math.Round((decimal)upper, 4, MidpointRounding.AwayFromZero);
    }
}
