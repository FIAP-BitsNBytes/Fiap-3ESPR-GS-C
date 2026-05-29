using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class AuthController(IAuthService authService) : BaseApiController
{
    // POST api/auth/register — public
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct)
    {
        var result = await authService.RegisterAsync(request, ct);
        return StatusCode(201, result);
    }

    // POST api/auth/login — public
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        return Ok(result);
    }

    // POST api/auth/refresh — public
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken ct)
    {
        var result = await authService.RefreshAsync(request, ct);
        return Ok(result);
    }

    // POST api/auth/logout — requires auth
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken ct)
    {
        await authService.LogoutAsync(request, ct);
        return NoContent();
    }
}