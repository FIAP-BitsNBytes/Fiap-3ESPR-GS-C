namespace MissionClear.Api.Models;

/// <summary>
/// Destino orbital pré-definido. AltitudeKm e InclinationDeg alimentam o LaunchWindowCalculator.
/// LatitudeDeg/LongitudeDeg padrão 0.0 — órbitas tratadas como equatoriais na simulação MVP.
/// </summary>
public sealed record MissionDestination(
    string Id,
    string DisplayName,
    double AltitudeKm,
    double InclinationDeg,
    string Description,
    double DeltaVKmS,
    double MissionDurationHours,
    string Icon,
    double LatitudeDeg = 0.0,
    double LongitudeDeg = 0.0);

public static class KnownDestinations
{
    public static readonly MissionDestination ISS = new(
        "ISS",
        "Estação Espacial Internacional",
        408,
        51.6,
        "Órbita da ISS — destino mais popular para missões LEO",
        9.40,
        6.2,
        "iss");

    public static readonly MissionDestination LeoGeneric = new(
        "LEO_GENERIC",
        "Órbita LEO Genérica",
        400,
        28.5,
        "Órbita baixa padrão para satélites de observação",
        9.20,
        5.8,
        "leo");

    public static readonly MissionDestination Sso = new(
        "SSO",
        "Sun-Synchronous Orbit",
        500,
        97.4,
        "Órbita heliosíncrona — usada por satélites de imageamento",
        10.10,
        7.0,
        "sso");

    public static readonly IReadOnlyList<MissionDestination> All = [ISS, LeoGeneric, Sso];

    public static MissionDestination? FindById(string id) =>
        All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    public static MissionDestination? Get(string id) => FindById(id);
}
