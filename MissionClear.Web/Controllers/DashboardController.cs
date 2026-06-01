using MissionClear.Web.Models;
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MissionClear.Web.Controllers;

public sealed class DashboardController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = new DashboardViewModel();

        var summaryTask = apiClient.GetAsync<JsonElement?>("/api/dashboard/summary");
        var detailTask  = apiClient.GetAsync<JsonElement?>("/api/dashboard/orbital-detail");
        await Task.WhenAll(summaryTask, detailTask);

        var summary = await summaryTask;
        var detail  = await detailTask;

        if (summary.HasValue && summary.Value.ValueKind != JsonValueKind.Undefined)
        {
            if (summary.Value.TryGetProperty("orbital", out var orbital))
            {
                vm.TotalTrackedObjects = orbital.GetProperty("total_tracked_objects").GetInt32();
                vm.ActiveAlerts        = orbital.GetProperty("active_conjunction_alerts").GetInt32();
                vm.LastUpdated         = orbital.GetProperty("last_updated").GetString();
                if (orbital.TryGetProperty("by_type", out var bt))
                {
                    vm.Debris       = bt.GetProperty("debris").GetInt32();
                    vm.Satellites   = bt.GetProperty("satellite").GetInt32();
                    vm.RocketBodies = bt.GetProperty("rocket_body").GetInt32();
                }
                if (orbital.TryGetProperty("by_altitude_band", out var ba))
                {
                    vm.LowAlt  = ba.GetProperty("low_200_500km").GetInt32();
                    vm.MidAlt  = ba.GetProperty("mid_500_1000km").GetInt32();
                    vm.HighAlt = ba.GetProperty("high_1000_2000km").GetInt32();
                }
            }
            if (summary.Value.TryGetProperty("user", out var user) && user.ValueKind != JsonValueKind.Null)
            {
                vm.UserDisplayName   = user.GetProperty("display_name").GetString();
                vm.UserTotalMissions = user.GetProperty("total_missions").GetInt32();
                vm.UserBestScore     = user.GetProperty("best_score").GetInt32();
            }
        }

        if (detail.HasValue && detail.Value.ValueKind != JsonValueKind.Undefined)
        {
            if (detail.Value.TryGetProperty("by_source", out var src))
                vm.BySource = src.EnumerateArray()
                    .Select(x => new SourceCount { Source = x.GetProperty("source").GetString()!, Count = x.GetProperty("count").GetInt32() })
                    .ToList();

            if (detail.Value.TryGetProperty("inclination_distribution", out var inc))
                vm.InclinationBins = inc.EnumerateArray()
                    .Select(x => new InclinationBin { Band = x.GetProperty("band").GetString()!, Count = x.GetProperty("count").GetInt32() })
                    .ToList();

            if (detail.Value.TryGetProperty("inclination_altitude_grid", out var grid))
                vm.InclinationGrid = grid.EnumerateArray()
                    .Select(x => new InclinationAltitudeCell {
                        InclinationBand = x.GetProperty("inclination_band").GetString()!,
                        AltitudeBand    = x.GetProperty("altitude_band").GetString()!,
                        Count           = x.GetProperty("count").GetInt32()
                    }).ToList();

            if (detail.Value.TryGetProperty("total_with_tle", out var twt))    vm.TotalWithTle    = twt.GetInt32();
            if (detail.Value.TryGetProperty("total_without_tle", out var twot)) vm.TotalWithoutTle = twot.GetInt32();
        }

        return View(vm);
    }
}
