using FluentAssertions;
using MedCareOS.Domain.Constants;
using MedCareOS.Domain.Entities;
using MedCareOS.Domain.Services;

namespace MedCareOS.Application.UnitTests.Domain;

public class OverbookAutonomyEvaluatorTests
{
    [Fact]
    public void Evaluate_BelowMinimum200_ShouldBlockWithVolumeReasonEvenWithZeroFalsePositives()
    {
        var policy = TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId);

        var eval = OverbookAutonomyEvaluator.Evaluate(
            evaluatedWouldOverbookCount: 199,
            falsePositivesCount: 0,
            policy);

        eval.IsEligibleForAutonomy.Should().BeFalse();
        eval.PointFalsePositiveRate.Should().Be(0m);
        eval.BlockingReasons.Should().ContainSingle(r => r.Contains("insuficientes"));
    }

    [Fact]
    public void Evaluate_AtOrAbove15PercentFpr_ShouldBlockEvenWith250Samples()
    {
        var policy = TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId);

        var eval = OverbookAutonomyEvaluator.Evaluate(
            evaluatedWouldOverbookCount: 200,
            falsePositivesCount: 30, // 15.00%
            policy);

        eval.IsEligibleForAutonomy.Should().BeFalse();
        eval.PointFalsePositiveRate.Should().Be(0.1500m);
        eval.BlockingReasons.Should().ContainSingle(r => r.Contains("falsos positivos"));
    }

    [Fact]
    public void Evaluate_At220SamplesWith24FalsePositives_ShouldPassGateAndComputeWilsonUpperBound()
    {
        var policy = TenantConfirmationPolicy.CreateDefault(RiskThresholds.DefaultTenantId);

        var eval = OverbookAutonomyEvaluator.Evaluate(
            evaluatedWouldOverbookCount: 220,
            falsePositivesCount: 24, // 10.91%
            policy);

        eval.IsEligibleForAutonomy.Should().BeTrue();
        eval.BlockingReasons.Should().BeEmpty();
        eval.PointFalsePositiveRate.Should().Be(0.1091m);
        eval.TruePositivesCount.Should().Be(196);
        eval.WilsonUpperBound95.Should().BeGreaterThan(eval.PointFalsePositiveRate);
        eval.WilsonUpperBound95.Should().BeLessThan(0.165m);
    }
}
