namespace MissionClear.Api.Dtos.Orbital;

public sealed record TleDto(
    string Epoch,
    string Line1,
    string Line2);

public sealed record OrbitParamsDto(
    double InclinationDeg,
    double Eccentricity,
    double PeriodMinutes,
    double ApogeeKm,
    double PerigeeKm);

public sealed record DebrisDetailDto(
    string Id,
    string Name,
    string Type,
    double Latitude,
    double Longitude,
    double AltitudeKm,
    double VelocityKmS,
    string Source,
    string UpdatedAt,
    TleDto? Tle,
    OrbitParamsDto? Orbit);
