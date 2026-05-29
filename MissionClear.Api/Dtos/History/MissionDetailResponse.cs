using MissionClear.Api.Dtos.Mission;

namespace MissionClear.Api.Dtos.History;

public sealed record ScoreBreakdownDto(
    int EfficiencyScore,
    int SafetyScore,
    int Total);

public sealed record MissionDetailResponse(
    string Id,
    string Destination,
    string DestinationDisplay,
    string Status,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS,
    string DepartureTime,
    string ArrivalTime,
    string CreatedAt,
    IReadOnlyList<ObstacleDto> Obstacles,
    ScoreBreakdownDto ScoreBreakdown);
