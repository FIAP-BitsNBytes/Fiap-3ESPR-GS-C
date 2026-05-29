namespace MissionClear.Api.Dtos.History;

public sealed record MissionStatsResponse(
    int TotalMissions,
    int SuccessfulMissions,
    int FailedMissions,
    int AbortedMissions,
    double SuccessRate,
    int BestScore,
    int WorstScore,
    int AverageScore,
    double TotalDeltaVKmS,
    int TotalObstaclesEncountered,
    string? FavoriteDestination,
    Dictionary<string, int> MissionsByDestination);
