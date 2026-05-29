using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Authorize]
public sealed class MissionsController(
    IMissionHistoryService historyService) : BaseApiController
{
    // GET api/missions — any authenticated role
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int     page        = 1,
        [FromQuery] int     limit       = 20,
        [FromQuery] string? status      = null,
        [FromQuery] string? destination = null,
        [FromQuery] string  sort        = "created_at_desc",
        CancellationToken   ct          = default)
    {
        limit = Math.Min(limit, 50);
        var result = await historyService.GetMissionsAsync(
            CurrentUserId, page, limit, status, destination, sort, ct);
        return Ok(result);
    }

    // GET api/missions/stats — MUST be before {id} route
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var result = await historyService.GetStatsAsync(CurrentUserId, ct);
        return Ok(result);
    }

    // GET api/missions/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id.Replace("msn_", ""), out var guid))
            return BadRequest(new { error = "INVALID_ID", message = "Invalid mission ID format." });

        var result = await historyService.GetMissionDetailAsync(guid, CurrentUserId, ct);
        return Ok(result);
    }

    // DELETE api/missions/{id} — Administrator role only
    [HttpDelete("{id}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id.Replace("msn_", ""), out var guid))
            return BadRequest(new { error = "INVALID_ID", message = "Invalid mission ID format." });

        await historyService.DeleteMissionAsync(guid, CurrentUserId, ct);
        return NoContent();
    }
}