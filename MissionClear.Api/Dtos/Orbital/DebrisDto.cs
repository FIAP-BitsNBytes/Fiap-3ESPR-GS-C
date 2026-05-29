namespace MissionClear.Api.Dtos.Orbital;

public sealed record DebrisDto(
    string Id,
    string Name,
    string Type,
    double Latitude,
    double Longitude,
    double AltitudeKm,
    double VelocityKmS,
    string Source,
    string UpdatedAt);
