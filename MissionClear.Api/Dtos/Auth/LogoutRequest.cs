using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record LogoutRequest([Required] string RefreshToken);
