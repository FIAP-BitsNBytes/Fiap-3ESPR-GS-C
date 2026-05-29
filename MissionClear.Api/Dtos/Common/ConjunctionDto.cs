namespace MissionClear.Api.Dtos.Common;

public sealed record ConjunctionDto(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    string TimeOfClosestApproach,
    string RiskLevel);
