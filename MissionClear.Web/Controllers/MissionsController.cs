using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MissionClear.Web.Controllers;

[Authorize]
public sealed class MissionsController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] int     page        = 1,
        [FromQuery] string? status      = null,
        [FromQuery] string? destination = null,
        [FromQuery] string? search      = null)
    {
        // Quick-filter buttons send `destination` (exact ID); search bar sends `search` (free text).
        // For free text, try to map display-name keywords → destination ID so the API can match.
        var destFilter = destination ?? ResolveDestinationId(search);

        var qs = $"page={page}&limit=20";
        if (!string.IsNullOrWhiteSpace(status))     qs += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(destFilter)) qs += $"&destination={Uri.EscapeDataString(destFilter)}";

        var isAdmin = User.IsInRole("Administrator");
        var endpoint = isAdmin ? $"/api/admin/missions?{qs}" : $"/api/missions?{qs}";

        var missionsTask = apiClient.GetAsync<JsonElement>(endpoint);
        var statsTask    = isAdmin
            ? Task.FromResult(default(JsonElement))
            : apiClient.GetAsync<JsonElement>("/api/missions/stats");

        await Task.WhenAll(missionsTask, statsTask);

        ViewBag.MissionsJson      = missionsTask.Result;
        ViewBag.StatsJson         = statsTask.Result;
        ViewBag.CurrentPage       = page;
        ViewBag.StatusFilter      = status;
        ViewBag.DestinationFilter = destFilter;
        ViewBag.SearchRaw         = search;
        ViewBag.IsAdmin           = isAdmin;
        return View();
    }

    // Maps free-text to a destination ID the API understands.
    // The API repository uses LIKE '%value%', so partial IDs (e.g. "LEO") also work.
    private static string? ResolveDestinationId(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;
        var s = search.Trim().ToLowerInvariant();

        if (s.Contains("iss") || s.Contains("esta") || s.Contains("espacial") || s.Contains("internacional"))
            return "ISS";

        if (s.Contains("sso") || s.Contains("sun") || s.Contains("helio") || s.Contains("imageamento") || s.Contains("sincrona") || s.Contains("síncrona"))
            return "SSO";

        if (s.Contains("leo") || s.Contains("gen") || s.Contains("observ") || s.Contains("baixa"))
            return "LEO_GENERIC";

        // No keyword matched — pass raw so the API tries LIKE '%search%' against the ID column.
        return search.Trim();
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        var json = await apiClient.GetAsync<JsonElement>($"/api/missions/{id}");
        if (json.ValueKind == JsonValueKind.Undefined)
            return NotFound();
        ViewBag.MissionJson = json;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> DetailsPartial(string id)
    {
        var endpoint = User.IsInRole("Administrator")
            ? $"/api/admin/missions/{id}"
            : $"/api/missions/{id}";

        var json = await apiClient.GetAsync<JsonElement>(endpoint);
        if (json.ValueKind == JsonValueKind.Undefined)
            return NotFound();
        ViewBag.MissionJson = json;
        return PartialView("_MissionDetail");
    }
}
