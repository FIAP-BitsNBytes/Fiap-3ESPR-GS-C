namespace MissionClear.Api.Dtos.History;

public sealed record MissionSummaryDto(
    string Id,
    string Destination,
    string DestinationDisplay,
    string Status,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS,
    int ObstaclesEncountered,
    string DepartureTime,
    string ArrivalTime,
    string CreatedAt);
