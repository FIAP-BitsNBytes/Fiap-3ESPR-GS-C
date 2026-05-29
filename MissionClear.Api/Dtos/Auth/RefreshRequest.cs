using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record RefreshRequest([Required] string RefreshToken);
