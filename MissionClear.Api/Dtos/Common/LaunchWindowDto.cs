namespace MissionClear.Api.Dtos.Common;

public sealed record LaunchWindowDto(
    string Start,
    string End,
    double RiskScore,
    double DeltaVKmS,
    double DurationHours,
    bool IsRecommended,
    IReadOnlyList<ConjunctionDto> Conjunctions);

/// <summary>
/// Shape de GET /api/launch-windows — inclui total_windows e safe_windows.
/// </summary>
public sealed record LaunchWindowsResponse(
    string Destination,
    string From,
    string To,
    int TotalWindows,
    int SafeWindows,
    IReadOnlyList<LaunchWindowDto> Windows);
