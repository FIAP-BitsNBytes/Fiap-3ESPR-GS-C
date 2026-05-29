namespace MissionClear.Api.Dtos.Destination;

public sealed record DestinationDto(
    string Id,
    string DisplayName,
    double AltitudeKm,
    double InclinationDeg,
    string Description,
    double DeltaVKmS,
    double MissionDurationHours,
    string Icon);

public sealed record DestinationsResponse(IReadOnlyList<DestinationDto> Destinations);
