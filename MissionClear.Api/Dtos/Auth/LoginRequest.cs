using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record LoginRequest(
    [Required][EmailAddress] string Email,
    [Required] string Password);
