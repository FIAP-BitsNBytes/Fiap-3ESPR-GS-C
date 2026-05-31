using MissionClear.Api.Dtos.Admin;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Authorize(Roles = "Administrator")]
public sealed class AdminController(
    IDataAggregatorService aggregator,
    IOrbitalCache cache) : BaseApiController
{
    // POST api/admin/refresh
    [HttpPost("refresh")]
    public async Task<IActionResult> ForceRefresh(CancellationToken ct)
    {
        try
        {
            await aggregator.FetchAndMergeAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (DomainException) { throw; }
        catch (Exception ex)
        {
            throw new DomainException(
                "CACHE_NOT_READY",
                $"Unable to refresh TLE data: {ex.Message}",
                503);
        }

        return Ok(new RefreshResponse(
            ObjectsInCache: cache.Count,
            LastFetch:      cache.LastFetch?.ToString("O") ?? DateTime.UtcNow.ToString("O"),
            Message:        $"Refresh complete. {cache.Count} objects now in cache."));
    }
}
