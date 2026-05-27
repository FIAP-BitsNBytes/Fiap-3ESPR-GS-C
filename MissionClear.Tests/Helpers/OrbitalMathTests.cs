using FluentAssertions;
using MissionClear.Api.Helpers;
using Xunit;

namespace MissionClear.Tests.Helpers;

public class OrbitalMathTests
{
    [Fact]
    public void Haversine_SamePoint_ReturnsZero()
    {
        OrbitalMath.HaversineKm(0, 0, 0, 0, OrbitalMath.EarthRadiusKm).Should().BeApproximately(0, 0.001);
    }

    [Fact]
    public void Haversine_OneDegreeLatAtEquator_IsAbout111km()
    {
        var km = OrbitalMath.HaversineKm(0, 0, 1, 0, OrbitalMath.EarthRadiusKm);
        km.Should().BeInRange(110, 112);
    }

    [Fact]
    public void EciToGeodetic_EquatorialPoint_LatNearZero()
    {
        var gmst = OrbitalMath.Gmst(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var r = OrbitalMath.EarthRadiusKm + 400;
        var (lat, lon, alt) = OrbitalMath.EciToGeodetic(r, 0, 0, gmst);
        lat.Should().BeApproximately(0, 0.1);
        alt.Should().BeApproximately(400, 1.0);
    }

    [Fact]
    public void ToRad_And_ToDeg_AreInverse()
    {
        OrbitalMath.ToDeg(OrbitalMath.ToRad(45.0)).Should().BeApproximately(45.0, 0.0001);
    }
}
