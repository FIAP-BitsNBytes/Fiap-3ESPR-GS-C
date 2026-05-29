namespace MissionClear.Api.Dtos.Mission;

public sealed record CompleteSessionResponse(
    string SessionId,
    string Status,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS,
    int ObstaclesEncountered,
    double DurationSeconds,
    bool SavedToHistory,
    string? MissionId);
