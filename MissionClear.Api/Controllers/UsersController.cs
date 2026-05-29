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
}