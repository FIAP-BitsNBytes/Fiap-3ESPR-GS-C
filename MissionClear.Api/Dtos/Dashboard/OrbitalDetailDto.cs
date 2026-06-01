namespace MissionClear.Api.Dtos.Dashboard;

public sealed record SourceCountDto(string Source, int Count);

public sealed record InclinationBinDto(string Band, double MinDeg, double MaxDeg, int Count);

public sealed record InclinationAltitudeCellDto(
    string InclinationBand,
    string AltitudeBand,   // "low_200_500", "mid_500_1000", "high_1000_2000"
    int Count);

public sealed record OrbitalDetailDto(
    IReadOnlyList<SourceCountDto> BySource,
    IReadOnlyList<InclinationBinDto> InclinationDistribution,
    IReadOnlyList<InclinationAltitudeCellDto> InclinationAltitudeGrid,
    int TotalWithTle,
    int TotalWithoutTle);
