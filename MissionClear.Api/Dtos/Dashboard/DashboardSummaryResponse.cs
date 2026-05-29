using MissionClear.Api.Dtos.Orbital;

namespace MissionClear.Api.Dtos.Dashboard;

public sealed record OrbitalSummaryDto(
    int TotalTrackedObjects,
    ByTypeDto ByType,
    ByAltitudeBandDto ByAltitudeBand,
    int ActiveConjunctionAlerts,
    string LastUpdated);

public sealed record LastMissionDto(
    string Destination,
    string Status,
    int Score,
    string CreatedAt);

public sealed record UserDashboardDto(
    string DisplayName,
    int TotalMissions,
    int BestScore,
    LastMissionDto? LastMission);

public sealed record DashboardSummaryResponse(
    OrbitalSummaryDto Orbital,
    UserDashboardDto? User);
