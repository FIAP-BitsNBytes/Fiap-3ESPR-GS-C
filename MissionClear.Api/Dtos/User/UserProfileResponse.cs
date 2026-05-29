namespace MissionClear.Api.Dtos.User;

public sealed record UserStatsDto(
    int TotalMissions,
    int SuccessfulMissions,
    int FailedMissions,
    int AbortedMissions,
    double SuccessRate,
    int BestScore,
    int AverageScore,
    string? FavoriteDestination,
    double TotalDeltaVKmS);

public sealed record UserProfileResponse(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    string CreatedAt,
    UserStatsDto Stats);
