using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class MissionController(
    IMissionSimulationService simulation) : BaseApiController
{
    // POST api/mission/simulate — public
    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(
        [FromBody] SimulateRequest request,
        CancellationToken ct)
    {
        var result = await simulation.SimulateAsync(request, ct);
        return Ok(result);
    }

    // POST api/mission/session — public
    [HttpPost("session")]
    public async Task<IActionResult> CreateSession(
        [FromBody] SessionRequest request,
        CancellationToken ct)
    {
        var result = await simulation.CreateSessionAsync(request, ct);
        return StatusCode(201, result);
    }

    // GET api/mission/session/{sessionId}/stream — public (SSE)
    [HttpGet("session/{sessionId}/stream")]
    public async Task StreamSession(
        string sessionId,
        [FromServices] IMissionSseService sseService,
        CancellationToken ct)
    {
        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["Connection"]        = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await sseService.StreamAsync(sessionId, Response, ct);
    }

    // POST api/mission/session/{sessionId}/complete — optional auth
    [HttpPost("session/{sessionId}/complete")]
    public async Task<IActionResult> CompleteSession(
        string sessionId,
        [FromBody] CompleteSessionRequest request,
        CancellationToken ct)
    {
        Guid? userId = IsAuthenticated ? CurrentUserId : null;
        var result = await simulation.CompleteSessionAsync(sessionId, request, userId, ct);
        return Ok(result);
    }
}