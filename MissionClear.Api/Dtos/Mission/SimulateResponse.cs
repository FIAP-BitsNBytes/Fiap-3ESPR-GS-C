using MissionClear.Api.Dtos.Common;

namespace MissionClear.Api.Dtos.Mission;

/// <summary>
/// Obstáculo na trajetória — mesmo shape que ConjunctionDto (seção 15 do contrato).
/// Alias local para clareza semântica no contexto de simulação.
/// </summary>
public sealed record ObstacleDto(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    string TimeOfClosestApproach,
    string RiskLevel);

public sealed record SimulateResponse(
    string Destination,
    DateTime DepartureTime,
    DateTime ArrivalTime,
    IReadOnlyList<object> Trajectory,
    IReadOnlyList<ObstacleDto> Obstacles,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS);
