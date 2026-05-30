using System.ComponentModel.DataAnnotations;

namespace MissionClear.Web.Models;

public sealed class RegisterViewModel
{
    [Required][EmailAddress]
    public string Email { get; set; } = "";

    [Required][MinLength(8)]
    public string Password { get; set; } = "";

    [Required][StringLength(50, MinimumLength = 2)]
    public string DisplayName { get; set; } = "";

    public string? Error { get; set; }
}
