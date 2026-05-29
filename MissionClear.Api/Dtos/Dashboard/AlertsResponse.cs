namespace MissionClear.Api.Dtos.Dashboard;

public sealed record AlertDto(
    string Id,
    string DebrisId,
    string DebrisName,
    string AffectedDestination,
    double ClosestApproachKm,
    string TimeOfClosestApproach,
    string RiskLevel,
    int MinutesUntilConjunction,
    string DetectedAt);

public sealed record AlertsResponse(
    IReadOnlyList<AlertDto> Alerts,
    int WindowHours,
    string GeneratedAt);
