namespace MissionClear.Api.Dtos.Common;

public sealed record BestWindowDto(
    int Rank,
    string Start,
    string End,
    double RiskScore,
    double DeltaVKmS,
    double DurationHours,
    IReadOnlyList<ConjunctionDto> Conjunctions);

public sealed record BestWindowsResponse(
    string Destination,
    string From,
    string To,
    IReadOnlyList<BestWindowDto> BestWindows);
