namespace MissionClear.Api.Dtos.Admin;

public sealed record AdminMissionDto(
    string Id,
    string UserId,
    string UserEmail,
    string UserDisplayName,
    string Destination,
    string DestinationDisplay,
    string Status,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS,
    int ObstaclesEncountered,
    string CreatedAt);
