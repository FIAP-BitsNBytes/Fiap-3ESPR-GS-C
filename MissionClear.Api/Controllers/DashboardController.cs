using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace MissionClear.Api.Controllers;

public sealed class DashboardController(
    IDashboardService dashboardService) : BaseApiController
{
    // GET api/dashboard/summary — optional auth
    // Without token: user section is null
    // With token: user section populated (display_name patched from JWT claims)
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        Guid? userId = null;
        string? displayName = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(sub, out var uid))
                userId = uid;
            displayName = User.FindFirst("display_name")?.Value;
        }

        var result = await dashboardService.GetSummaryAsync(userId, displayName, ct);

        return Ok(result);
    }

    // GET api/dashboard/orbital-detail — public
    [HttpGet("orbital-detail")]
    public IActionResult GetOrbitalDetail()
    {
        var result = dashboardService.GetOrbitalDetail();
        return Ok(result);
    }

    // GET api/dashboard/alerts — public
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] int    windowHours = 6,
        [FromQuery] string minRisk     = "medium",
        CancellationToken  ct          = default)
    {
        windowHours = Math.Clamp(windowHours, 1, 24);
        var result = await dashboardService.GetAlertsAsync(windowHours, minRisk, ct);
        return Ok(result);
    }
}