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
        var json = await apiClient.GetAsync<JsonElement>("/api/dashboard/summary");
        var vm = new DashboardViewModel();

        if (json.ValueKind != JsonValueKind.Undefined)
        {
            if (json.TryGetProperty("orbital", out var orbital))
            {
                vm.TotalTrackedObjects = orbital.GetProperty("total_tracked_objects").GetInt32();
                vm.ActiveAlerts = orbital.GetProperty("active_conjunction_alerts").GetInt32();
                vm.LastUpdated = orbital.GetProperty("last_updated").GetString();
                if (orbital.TryGetProperty("by_type", out var byType))
                {
                    vm.Debris = byType.GetProperty("debris").GetInt32();
                    vm.Satellites = byType.GetProperty("satellite").GetInt32();
                    vm.RocketBodies = byType.GetProperty("rocket_body").GetInt32();
                }
            }
            if (json.TryGetProperty("user", out var user) && user.ValueKind != JsonValueKind.Null)
            {
                vm.UserDisplayName = user.GetProperty("display_name").GetString();
                vm.UserTotalMissions = user.GetProperty("total_missions").GetInt32();
                vm.UserBestScore = user.GetProperty("best_score").GetInt32();
            }
        }

        return View(vm);
    }
}
