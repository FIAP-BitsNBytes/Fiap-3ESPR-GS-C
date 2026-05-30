using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class LaunchWindowCalculatorTests
{
    private readonly LaunchWindowCalculator _calc = new();

    private static readonly MissionDestination Iss = KnownDestinations.ISS;

    [Fact]
    public void Calculate_Returns48Windows_For12HourRange()
    {
        var from = new DateTime(2025, 5, 27, 0, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(12);

        var windows = _calc.Calculate(Iss, from, to, []);

        windows.Should().HaveCount(48); // 12h / 15min = 48 slots
    }

    [Fact]
    public void Calculate_IsRecommended_WhenRiskScoreUnder01()
    {
        var from    = DateTime.UtcNow;
        var windows = _calc.Calculate(Iss, from, from.AddMinutes(15), []);

        windows.Should().HaveCount(1);
        windows[0].IsRecommended.Should().Be(windows[0].RiskScore < 0.1);
    }

    [Fact]
    public void Calculate_ReturnsEmptyConjunctions_WhenNoCacheData()
    {
        var from    = DateTime.UtcNow;
        var windows = _calc.Calculate(Iss, from, from.AddMinutes(15), []);

        windows[0].Conjunctions.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_SetsCorrectDeltaV_ForDestination()
    {
        var from    = DateTime.UtcNow;
        var windows = _calc.Calculate(Iss, from, from.AddMinutes(15), []);

        windows[0].DeltaVKmS.Should().Be(Iss.DeltaVKmS);
    }
}
