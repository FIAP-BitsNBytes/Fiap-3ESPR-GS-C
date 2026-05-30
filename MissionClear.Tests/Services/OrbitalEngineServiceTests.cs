using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class OrbitalEngineServiceTests
{
    private readonly OrbitalEngineService _engine = new();

    private static readonly DateTime FixedTime =
        new(2025, 5, 27, 14, 0, 0, DateTimeKind.Utc);

    private static OrbitalObject MakeRaw(string id = "12345",
        double lat = 45.0, double lon = 90.0, double alt = 500.0,
        string? tleLine1 = "stub-line1") =>
        new(id, $"TEST DEB {id}", "debris", lat, lon, alt, 7.5, "celestrak",
            DateTime.UtcNow, TleLine1: tleLine1, TleLine2: "stub-line2");

    // ── Propagate: basic validity ─────────────────────────────────────────────

    [Fact]
    public void Propagate_ReturnsObjectWithSameId()
    {
        var result = _engine.Propagate(MakeRaw("99999"), FixedTime);
        result.Should().NotBeNull();
        result!.Id.Should().Be("99999");
    }

    [Fact]
    public void Propagate_LatitudeInValidRange()
    {
        var result = _engine.Propagate(MakeRaw(), FixedTime);
        result!.Latitude.Should().BeInRange(-90, 90);
    }

    [Fact]
    public void Propagate_LongitudeInValidRange()
    {
        var result = _engine.Propagate(MakeRaw(), FixedTime);
        result!.Longitude.Should().BeInRange(-180, 180);
    }

    [Fact]
    public void Propagate_AltitudeInLEORange()
    {
        var result = _engine.Propagate(MakeRaw(), FixedTime);
        result!.AltitudeKm.Should().BeInRange(200, 2000);
    }

    // ── Propagate: determinism ────────────────────────────────────────────────

    [Fact]
    public void Propagate_IsDeterministic_SameIdSameTime()
    {
        var raw = MakeRaw("42424");
        var a = _engine.Propagate(raw, FixedTime);
        var b = _engine.Propagate(raw, FixedTime);

        a!.Latitude.Should().Be(b!.Latitude);
        a.Longitude.Should().Be(b.Longitude);
        a.AltitudeKm.Should().Be(b.AltitudeKm);
    }

    // ── Propagate: pass-through when no TLE lines ─────────────────────────────

    [Fact]
    public void Propagate_NullTleLines_ReturnsSameObject()
    {
        var raw = MakeRaw(tleLine1: null);
        var result = _engine.Propagate(raw, FixedTime);

        // No TLE → pass-through unchanged (cannot re-propagate without TLE)
        result.Should().BeSameAs(raw);
    }

    // ── PropagateAll ──────────────────────────────────────────────────────────

    [Fact]
    public void PropagateAll_AllFiveObjects_AllReturned()
    {
        var objects = Enumerable.Range(1, 5)
            .Select(i => MakeRaw(i.ToString()))
            .ToList<OrbitalObject>();

        var results = _engine.PropagateAll(objects, FixedTime);

        results.Should().HaveCount(5);
        results.Select(o => o.Id).Should().BeEquivalentTo(["1", "2", "3", "4", "5"]);
    }

    [Fact]
    public void PropagateAll_EmptyInput_ReturnsEmpty()
    {
        var results = _engine.PropagateAll([], FixedTime);
        results.Should().BeEmpty();
    }
}
