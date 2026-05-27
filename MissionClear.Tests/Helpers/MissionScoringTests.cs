using FluentAssertions;
using MissionClear.Api.Helpers;
using Xunit;

namespace MissionClear.Tests.Helpers;

public class MissionScoringTests
{
    [Fact]
    public void Compute_ZeroDeltaV_ZeroRisk_Returns100()
    {
        var (_, _, total) = MissionScoring.Compute(0, 0);
        total.Should().Be(100);
    }

    [Fact]
    public void Compute_MaxDeltaV_ZeroRisk_Returns50()
    {
        var (_, _, total) = MissionScoring.Compute(MissionScoring.MaxDeltaVKmS, 0);
        total.Should().Be(50);
    }

    [Fact]
    public void Compute_ZeroDeltaV_MaxRisk_Returns50()
    {
        var (_, _, total) = MissionScoring.Compute(0, 1.0);
        total.Should().Be(50);
    }

    [Fact]
    public void Compute_MaxDeltaV_MaxRisk_Returns0()
    {
        var (_, _, total) = MissionScoring.Compute(MissionScoring.MaxDeltaVKmS, 1.0);
        total.Should().Be(0);
    }

    [Fact]
    public void Compute_IssCruise_TypicalValues()
    {
        var (eff, saf, total) = MissionScoring.Compute(9.4, 0.1);
        eff.Should().BeApproximately(10.83, 0.01);
        saf.Should().BeApproximately(45.0, 0.01);
        total.Should().Be(56);
    }
}
