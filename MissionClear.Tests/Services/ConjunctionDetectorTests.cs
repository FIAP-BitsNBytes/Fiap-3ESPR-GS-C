using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class ConjunctionDetectorTests
{
    private readonly ConjunctionDetector _detector = new();

    // ISS at lat=0, lon=0, alt=408 km (uses KnownDestinations.ISS)
    private static MissionDestination IssDestination => KnownDestinations.ISS;

    private static OrbitalObject MakeDebris(string id, double lat, double lon, double altKm) =>
        new(id, $"DEB-{id}", "debris", lat, lon, altKm, 7.5, "celestrak", DateTime.UtcNow);

    [Fact]
    public void Detect_ReturnsCritical_WhenDebrisUnder1km()
    {
        // Debris at same lat/lon as destination, 0 km altitude diff → distance < 1 km → Critical
        var debris = new[] { MakeDebris("1", 0.0, 0.0, 408.0) };

        var results = _detector.Detect(IssDestination, DateTime.UtcNow, debris);

        results.Should().NotBeEmpty();
        results[0].RiskLevel.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void Detect_ReturnsEmpty_WhenDebrisFarAway()
    {
        // Debris at 1500 km altitude — well beyond the 200 km filter radius
        var debris = new[] { MakeDebris("far", 80.0, 120.0, 1500.0) };

        var results = _detector.Detect(IssDestination, DateTime.UtcNow, debris);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ClassifiesHigh_WhenDistanceBetween1And5km()
    {
        // Debris 2 km above ISS altitude, same lat/lon → distance ≈ 2 km → High
        var debris = new[] { MakeDebris("hi", 0.0, 0.0, 410.0) };

        var results = _detector.Detect(IssDestination, DateTime.UtcNow, debris);

        results.Should().NotBeEmpty();
        results[0].RiskLevel.Should().BeOneOf(RiskLevel.High, RiskLevel.Critical);
    }

    [Fact]
    public void Detect_DoesNotThrow_WhenDebrisListIsEmpty()
    {
        var act = () => _detector.Detect(IssDestination, DateTime.UtcNow, []);

        act.Should().NotThrow();
    }
}
