using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Dashboard;
using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class DashboardService(
    IOrbitalCache cache,
    IMissionRepository missionRepo) : IDashboardService
{
    public async Task<DashboardSummaryResponse> GetSummaryAsync(
        Guid? userId,
        string? displayName,
        CancellationToken ct)
    {
        var debris = cache.GetAll();

        int debrisCount = 0, satCount = 0, rocketCount = 0;
        int lowCount = 0, midCount = 0, highCount = 0;

        foreach (var obj in debris)
        {
            switch (obj.Type)
            {
                case "debris": debrisCount++; break;
                case "satellite": satCount++; break;
                case "rocket_body": rocketCount++; break;
            }

            if (obj.AltitudeKm < 500) lowCount++;
            else if (obj.AltitudeKm < 1000) midCount++;
            else highCount++;
        }

        var orbital = new OrbitalSummaryDto(
            debris.Count,
            new ByTypeDto(debrisCount, satCount, rocketCount),
            new ByAltitudeBandDto(lowCount, midCount, highCount),
            0, // Simplified: actual active conjunctions would require predicting all known paths
            (cache.LastPropagation ?? DateTime.UtcNow).ToString("O"));

        UserDashboardDto? userDto = null;
        if (userId.HasValue)
        {
            var stats = await missionRepo.GetUserStatsAsync(userId.Value, ct);
            var paged = await missionRepo.GetPagedAsync(userId.Value, 1, 1, null, null, "created_at_desc", ct);
            
            LastMissionDto? last = null;
            var lastMission = paged.Items.FirstOrDefault();
            if (lastMission != null)
            {
                last = new LastMissionDto(
                    lastMission.Destination,
                    lastMission.Status,
                    lastMission.MissionScore,
                    lastMission.CreatedAt.ToString("O"));
            }

            userDto = new UserDashboardDto(
                displayName ?? "Comandante",
                stats.TotalMissions,
                stats.BestScore,
                last);
        }

        return new DashboardSummaryResponse(orbital, userDto);
    }

    public OrbitalDetailDto GetOrbitalDetail()
    {
        var objects = cache.GetAll();

        // By source — ordered by count desc
        var bySource = objects
            .GroupBy(o => o.Source)
            .Select(g => new SourceCountDto(g.Key, g.Count()))
            .OrderByDescending(s => s.Count)
            .ToList()
            .AsReadOnly();

        // Inclination bins — 10° intervals 0..100
        var inclinationBins = new List<InclinationBinDto>();
        for (int i = 0; i < 10; i++)
        {
            double minDeg = i * 10.0, maxDeg = (i + 1) * 10.0;
            int count = objects.Count(o =>
                o.InclinationDeg.HasValue &&
                o.InclinationDeg.Value >= minDeg &&
                o.InclinationDeg.Value < maxDeg);
            inclinationBins.Add(new InclinationBinDto($"{(int)minDeg}-{(int)maxDeg}°", minDeg, maxDeg, count));
        }

        // Inclination × Altitude heatmap grid
        static string AltBand(double alt) => alt < 500 ? "low_200_500" : alt < 1000 ? "mid_500_1000" : "high_1000_2000";
        var grid = objects
            .Where(o => o.InclinationDeg.HasValue)
            .GroupBy(o => (
                Inclination: $"{(int)(Math.Floor(o.InclinationDeg!.Value / 10.0) * 10)}-{(int)(Math.Floor(o.InclinationDeg.Value / 10.0) * 10 + 10)}°",
                Altitude: AltBand(o.AltitudeKm)))
            .Select(g => new InclinationAltitudeCellDto(g.Key.Inclination, g.Key.Altitude, g.Count()))
            .OrderBy(c => c.InclinationBand)
            .ThenBy(c => c.AltitudeBand)
            .ToList()
            .AsReadOnly();

        int withTle = objects.Count(o => o.TleLine1 != null);

        return new OrbitalDetailDto(bySource, inclinationBins.AsReadOnly(), grid, withTle, objects.Count - withTle);
    }

    public Task<AlertsResponse> GetAlertsAsync(
        int windowHours,
        string minRisk,
        CancellationToken ct)
    {
        var debris = cache.GetAll();
        var alerts = new List<AlertDto>();
        var now = DateTime.UtcNow;
        var end = now.AddHours(windowHours);

        var minRiskLevel = minRisk.ToLowerInvariant() switch
        {
            "critical" => RiskLevel.Critical,
            "high"     => RiskLevel.High,
            _          => RiskLevel.Medium
        };

        // Simplified alert generation based on static destination points vs current debris positions
        foreach (var dest in KnownDestinations.All)
        {
            foreach (var obj in debris)
            {
                var avgAlt = (dest.AltitudeKm + obj.AltitudeKm) / 2.0;
                var horizKm = OrbitalMath.HaversineKm(
                    dest.LatitudeDeg, dest.LongitudeDeg,
                    obj.Latitude, obj.Longitude,
                    OrbitalMath.EarthRadiusKm + avgAlt);
                var vertKm = Math.Abs(dest.AltitudeKm - obj.AltitudeKm);
                var distKm = Math.Sqrt(horizKm * horizKm + vertKm * vertKm);

                var risk = RiskScoring.Classify(distKm);
                if (risk >= minRiskLevel && risk != RiskLevel.Low)
                {
                    // Generate a deterministic time within the window
                    var seed = obj.Id.GetHashCode() ^ dest.Id.GetHashCode();
                    var rnd = new Random(seed);
                    var minutes = rnd.Next(1, windowHours * 60);
                    var toca = now.AddMinutes(minutes);

                    alerts.Add(new AlertDto(
                        Guid.NewGuid().ToString("N"),
                        obj.Id,
                        obj.Name,
                        dest.DisplayName,
                        Math.Round(distKm, 3),
                        toca.ToString("O"),
                        risk.ToString().ToLowerInvariant(),
                        minutes,
                        now.ToString("O")
                    ));
                }
            }
        }

        return Task.FromResult(new AlertsResponse(
            alerts.OrderBy(a => a.ClosestApproachKm).ToList(),
            windowHours,
            now.ToString("O")));
    }
}
