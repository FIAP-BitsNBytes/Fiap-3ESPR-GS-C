using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

/// <summary>
/// Propagates orbital objects to a specific UTC instant.
///
/// SGP4 status: No compatible NuGet package is available in this project.
/// Uses a deterministic FNV-1a stub that produces reproducible lat/lon/alt deltas.
///
/// Swap-in path for real SGP4 (when available):
///   1. dotnet add package <sgp4-package>
///   2. Replace SimulateDelta() body with real ECI propagation + ECI→Geodetic conversion.
///   3. All existing tests remain valid because they assert on range/determinism, not exact values.
/// </summary>
public sealed class OrbitalEngineService : IOrbitalEngineService
{
    private const double AltMin = 200.0;
    private const double AltMax = 2000.0;

    public OrbitalObject? Propagate(OrbitalObject raw, DateTime atTime)
    {
        // Objects with no TLE lines cannot be re-propagated — return as-is.
        if (raw.TleLine1 is null)
            return raw;

        var (deltaLat, deltaLon, deltaAlt) = SimulateDelta(raw.Id, atTime);

        var lat = Math.Clamp(raw.Latitude + deltaLat, -90.0, 90.0);
        var lon = WrapLongitude(raw.Longitude + deltaLon);
        var alt = Math.Clamp(raw.AltitudeKm + deltaAlt, AltMin, AltMax);

        return raw with
        {
            Latitude  = Math.Round(lat, 4),
            Longitude = Math.Round(lon, 4),
            AltitudeKm = Math.Round(alt, 2),
            UpdatedAt = atTime,
        };
    }

    public IReadOnlyList<OrbitalObject> PropagateAll(IReadOnlyList<OrbitalObject> objects, DateTime atTime)
    {
        if (objects.Count == 0) return [];

        var results = new OrbitalObject?[objects.Count];

        Parallel.For(0, objects.Count, i =>
        {
            try { results[i] = Propagate(objects[i], atTime); }
            catch { results[i] = null; }
        });

        return results.Where(o => o is not null).Cast<OrbitalObject>().ToList().AsReadOnly();
    }

    /// <summary>
    /// Deterministic delta (lat, lon, alt) for a given NORAD ID at a given time.
    /// FNV-1a hash of the ID → base seed (stable per ID).
    /// XOR with time-derived value → varies between propagation ticks but reproduces exactly.
    /// Ranges: lat ±1°, lon ±2°, alt ±0.5 km.
    /// </summary>
    internal static (double DeltaLat, double DeltaLon, double DeltaAlt)
        SimulateDelta(string id, DateTime atTime)
    {
        var idHash = FnvHash(id);
        var timeSeed = (uint)(atTime.Ticks >> 16);
        var seed = (int)((idHash ^ timeSeed) & 0x7FFFFFFFu);

        var rng = new Random(seed);
        var deltaLat = (rng.NextDouble() - 0.5) * 2.0;   // ±1°
        var deltaLon = (rng.NextDouble() - 0.5) * 4.0;   // ±2°
        var deltaAlt = (rng.NextDouble() - 0.5) * 1.0;   // ±0.5 km

        return (deltaLat, deltaLon, deltaAlt);
    }

    internal static uint FnvHash(string input)
    {
        const uint FnvPrime   = 16777619u;
        const uint OffsetBasis = 2166136261u;
        var hash = OffsetBasis;
        foreach (var c in input)
            hash = (hash ^ (uint)c) * FnvPrime;
        return hash;
    }

    internal static double WrapLongitude(double lon)
    {
        lon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
        return lon;
    }
}
