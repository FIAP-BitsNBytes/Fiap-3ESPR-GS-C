using MissionClear.Api.Dtos.User;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Authorize]
public sealed class UsersController(IUserService userService) : BaseApiController
{
    // GET api/users/me
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var profile = await userService.GetProfileAsync(CurrentUserId, ct);
        return Ok(profile);
    }

    // PUT api/users/me
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(
        [FromBody] UpdateUserRequest request,
        CancellationToken ct)
    {
        var profile = await userService.UpdateProfileAsync(CurrentUserId, request, ct);
        return Ok(profile);
    }

    // GET api/users/me/favorites
    [HttpGet("me/favorites")]
    public async Task<IActionResult> GetFavorites(CancellationToken ct)
    {
        var favorites = await userService.GetFavoritesAsync(CurrentUserId, ct);
        return Ok(favorites);
    }

    // PUT api/users/me/favorites
    [HttpPut("me/favorites")]
    public async Task<IActionResult> UpdateFavorites(
        [FromBody] UpdateFavoritesRequest request,
        CancellationToken ct)
    {
        var favorites = await userService.UpdateFavoritesAsync(CurrentUserId, request, ct);
        return Ok(favorites);
    }
}