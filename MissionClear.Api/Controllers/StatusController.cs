using MissionClear.Api.Dtos.Status;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class StatusController(IOrbitalCache cache) : BaseApiController
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    // GET api/status
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new StatusResponse(
            Status:            cache.IsReady ? "ready" : "loading",
            TleCount:          cache.Count,
            PropagatedCount:   cache.Count,
            LastTleFetch:      cache.LastFetch?.ToString("O"),
            LastPropagation:   cache.LastPropagation?.ToString("O"),
            UptimeSeconds:     (long)(DateTime.UtcNow - StartTime).TotalSeconds,
            Sources:           new SourceStatusDto("ok", "unavailable")));
    }
}