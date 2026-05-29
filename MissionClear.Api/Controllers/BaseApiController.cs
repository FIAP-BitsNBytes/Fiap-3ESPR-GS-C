using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Returns the authenticated user's Guid.
    /// Checks ClaimTypes.NameIdentifier first, then "sub" claim (JWT standard).
    /// Returns Guid.Empty when not authenticated or claim is missing/malformed.
    /// </summary>
    protected Guid CurrentUserId =>
        Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub"),
            out var id)
        ? id : Guid.Empty;

    /// <summary>True when the request carries a valid, authenticated identity.</summary>
    protected bool IsAuthenticated => User.Identity?.IsAuthenticated == true;
}