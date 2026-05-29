using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record RegisterRequest(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    [Required][StringLength(50, MinimumLength = 2)] string DisplayName,
    string Role = "Researcher");
