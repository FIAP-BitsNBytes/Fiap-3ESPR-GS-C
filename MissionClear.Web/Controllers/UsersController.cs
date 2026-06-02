using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MissionClear.Web.Controllers;

[Authorize(Roles = "Administrator")]
public sealed class UsersController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var json = await apiClient.GetAsync<JsonElement>("/api/admin/users");
        ViewBag.UsersJson = json;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        await apiClient.DeleteAsync($"/api/admin/users/{id}");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ChangeRole(string id, string role)
    {
        await apiClient.PostAsync<JsonElement>($"/api/admin/users/{id}/role", new { role });
        return RedirectToAction(nameof(Index));
    }
}
