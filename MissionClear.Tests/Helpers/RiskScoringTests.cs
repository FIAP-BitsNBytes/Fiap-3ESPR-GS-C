using FluentAssertions;
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using Xunit;

namespace MissionClear.Tests.Helpers;

public class RiskScoringTests
{
    [Theory]
    [InlineData(0.5, RiskLevel.Critical)]
    [InlineData(3.0, RiskLevel.High)]
    [InlineData(7.0, RiskLevel.Medium)]
    [InlineData(50.0, RiskLevel.Low)]
    public void Classify_ReturnsCorrectLevel(double km, RiskLevel expected)
    {
        RiskScoring.Classify(km).Should().Be(expected);
    }

    [Fact]
    public void ComputeScore_NoDebris_ReturnsZero()
    {
        RiskScoring.ComputeScore(Array.Empty<double>()).Should().Be(0.0);
    }

    [Fact]
    public void ComputeScore_FarDebris_ReturnsZero()
    {
        RiskScoring.ComputeScore(new[] { 300.0, 500.0 }).Should().Be(0.0);
    }

    [Fact]
    public void ComputeScore_ManyCloseDebris_ClampsToOne()
    {
        var many = Enumerable.Repeat(0.1, 100).ToList();
        RiskScoring.ComputeScore(many).Should().Be(1.0);
    }

    [Fact]
    public void ComputeScore_AtBoundary_IsNonZero()
    {
        RiskScoring.ComputeScore(new[] { RiskScoring.SafeKm }).Should().BeApproximately(1.0, 0.001);
    }
}
