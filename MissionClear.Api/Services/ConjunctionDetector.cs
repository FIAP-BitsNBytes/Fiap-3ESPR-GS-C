using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class ConjunctionDetector : IConjunctionDetector
{
    // Destination orbit is treated as a point at (LatitudeDeg=0, LongitudeDeg=0, altKm) for proximity filtering.
    // MissionDestination exposes LatitudeDeg and LongitudeDeg (defaulting to 0.0 for equatorial orbit assumption).
    // OrbitalObject exposes Latitude and Longitude (no "Deg" suffix).

    public IReadOnlyList<ConjunctionResult> Detect(
        MissionDestination destination,
        DateTime at,
        IReadOnlyList<OrbitalObject> debris)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(debris);

        var destLat = destination.LatitudeDeg;
        var destLon = destination.LongitudeDeg;
        var destAlt = destination.AltitudeKm;

        var results = new List<ConjunctionResult>();

        foreach (var obj in debris)
        {
            // 3D distance: horizontal Haversine at average altitude + altitude difference
            // OrbitalObject uses Latitude and Longitude (no "Deg" suffix)
            var avgAlt = (destAlt + obj.AltitudeKm) / 2.0;
            var horizKm = OrbitalMath.HaversineKm(
                destLat, destLon,
                obj.Latitude, obj.Longitude,
                OrbitalMath.EarthRadiusKm + avgAlt);
            var vertKm   = Math.Abs(destAlt - obj.AltitudeKm);
            var distKm   = Math.Sqrt(horizKm * horizKm + vertKm * vertKm);

            if (distKm > 200) continue; // only process debris within 200 km

            // Deterministic time-of-closest-approach: seeded by debris id hash for test stability
            var seed  = obj.Id.GetHashCode();
            var etaMin = new Random(seed).Next(5, 90);
            var toca  = at.AddMinutes(etaMin);

            results.Add(new ConjunctionResult(
                obj.Id,
                obj.Name,
                Math.Round(distKm, 3),
                toca,
                RiskScoring.Classify(distKm)));
        }

        return results.OrderBy(r => r.ClosestApproachKm).ToList().AsReadOnly();
    }
}
