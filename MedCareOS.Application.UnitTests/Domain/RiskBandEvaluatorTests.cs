using FluentAssertions;
using MedCareOS.Domain.Enums;
using MedCareOS.Domain.Services;
using MedCareOS.Domain.ValueObjects;

namespace MedCareOS.Application.UnitTests.Domain;

public class RiskBandEvaluatorTests
{
    private static HysteresisThresholds Thresholds => HysteresisThresholds.Default; // 0.40 / 0.50 / 0.55 / 0.70

    [Theory]
    [InlineData(0.00, RiskBandLevel.Low)]
    [InlineData(0.4999, RiskBandLevel.Low)]
    [InlineData(0.5000, RiskBandLevel.Medium)]
    [InlineData(0.6999, RiskBandLevel.Medium)]
    [InlineData(0.7000, RiskBandLevel.High)]
    [InlineData(0.9500, RiskBandLevel.High)]
    public void EvaluateInitial_UsesEntryThresholds(decimal score, RiskBandLevel expected)
    {
        RiskBandEvaluator.EvaluateInitial(score, Thresholds).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_RespectsHysteresisDeadbandsWithoutFlapping()
    {
        // Desde Low
        RiskBandEvaluator.Evaluate(0.4999m, RiskBandLevel.Low, Thresholds).Should().Be(RiskBandLevel.Low);
        RiskBandEvaluator.Evaluate(0.5000m, RiskBandLevel.Low, Thresholds).Should().Be(RiskBandLevel.Medium);
        RiskBandEvaluator.Evaluate(0.7000m, RiskBandLevel.Low, Thresholds).Should().Be(RiskBandLevel.High);

        // Desde Medium: no baja hasta caer por debajo de 0.40, no sube hasta 0.70
        RiskBandEvaluator.Evaluate(0.4000m, RiskBandLevel.Medium, Thresholds).Should().Be(RiskBandLevel.Medium);
        RiskBandEvaluator.Evaluate(0.3999m, RiskBandLevel.Medium, Thresholds).Should().Be(RiskBandLevel.Low);
        RiskBandEvaluator.Evaluate(0.6999m, RiskBandLevel.Medium, Thresholds).Should().Be(RiskBandLevel.Medium);
        RiskBandEvaluator.Evaluate(0.7000m, RiskBandLevel.Medium, Thresholds).Should().Be(RiskBandLevel.High);

        // Desde High: permanece en High dentro de la zona muerta [0.55, 0.70)
        RiskBandEvaluator.Evaluate(0.6800m, RiskBandLevel.High, Thresholds).Should().Be(RiskBandLevel.High);
        RiskBandEvaluator.Evaluate(0.5500m, RiskBandLevel.High, Thresholds).Should().Be(RiskBandLevel.High);
        RiskBandEvaluator.Evaluate(0.5499m, RiskBandLevel.High, Thresholds).Should().Be(RiskBandLevel.Medium);
        RiskBandEvaluator.Evaluate(0.3999m, RiskBandLevel.High, Thresholds).Should().Be(RiskBandLevel.Low);
    }

    [Fact]
    public void Evaluate_OscillatingScoreAround070_DoesNotFlap()
    {
        var band = RiskBandEvaluator.Evaluate(0.69m, RiskBandLevel.Medium, Thresholds);
        band.Should().Be(RiskBandLevel.Medium);

        band = RiskBandEvaluator.Evaluate(0.71m, band, Thresholds);
        band.Should().Be(RiskBandLevel.High);

        // Oscilaciones en [0.55, 0.70) mantienen High
        foreach (var score in new[] { 0.68m, 0.60m, 0.69m, 0.56m })
        {
            band = RiskBandEvaluator.Evaluate(score, band, Thresholds);
            band.Should().Be(RiskBandLevel.High);
        }
    }
}
