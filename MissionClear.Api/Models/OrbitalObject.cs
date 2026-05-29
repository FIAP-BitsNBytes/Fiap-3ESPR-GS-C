namespace MissionClear.Api.Models;

/// <summary>
/// Objeto orbital propagado (debris, satélite ou rocket body) em um instante.
/// Imutável. Produzido pelo OrbitalEngine, consumido pelo ConjunctionDetector e pelos controllers.
/// Campos TLE/orbit opcionais — presentes apenas quando fetched via /api/debris/{id}.
/// </summary>
public sealed record OrbitalObject(
    string Id,
    string Name,
    string Type,
    double Latitude,
    double Longitude,
    double AltitudeKm,
    double VelocityKmS,
    string Source,
    DateTime UpdatedAt,
    string? TleLine1 = null,
    string? TleLine2 = null,
    string? TleEpoch = null,
    double? InclinationDeg = null,
    double? Eccentricity = null,
    double? PeriodMinutes = null,
    double? ApogeeKm = null,
    double? PerigeeKm = null);
