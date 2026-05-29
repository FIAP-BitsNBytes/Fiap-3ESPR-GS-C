namespace MissionClear.Api.Dtos.Auth;

/// <summary>
/// Objeto user incluído nas respostas de login e register.
/// total_missions e best_score são opcionais: null em register (conta nova), populados em login.
/// </summary>
public sealed record UserInAuthResponse(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    string CreatedAt,
    int? TotalMissions = null,
    int? BestScore = null);

public sealed record AuthResponse(
    UserInAuthResponse User,
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

public sealed record RefreshTokenResponse(
    string AccessToken,
    int ExpiresIn);
