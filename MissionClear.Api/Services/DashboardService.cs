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
