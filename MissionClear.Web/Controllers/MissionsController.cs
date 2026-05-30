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
        [FromQuery] int page = 1,
        [FromQuery] string? status = null)
    {
        var url = $"/api/missions?page={page}&limit=20{(status != null ? $"&status={status}" : "")}";
        var json = await apiClient.GetAsync<JsonElement>(url);
        ViewBag.MissionsJson = json;
        ViewBag.CurrentPage = page;
        ViewBag.StatusFilter = status;
        return View();
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
}
