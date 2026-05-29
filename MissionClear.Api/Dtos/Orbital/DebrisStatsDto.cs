namespace MissionClear.Api.Dtos.Orbital;

public sealed record ByTypeDto(
    int Debris,
    int Satellite,
    int RocketBody);

/// <summary>
/// Snake_case via JsonNamingPolicy produz: low_200_500_km — incorreto.
/// Usar [JsonPropertyName] explícito apenas neste record para forçar as chaves exatas do contrato.
/// </summary>
public sealed record ByAltitudeBandDto(
    [property: System.Text.Json.Serialization.JsonPropertyName("low_200_500km")] int Low200500km,
    [property: System.Text.Json.Serialization.JsonPropertyName("mid_500_1000km")] int Mid5001000km,
    [property: System.Text.Json.Serialization.JsonPropertyName("high_1000_2000km")] int High10002000km);

public sealed record SourcesDto(
    int Celestrak,
    int Keeptrack);

public sealed record DebrisStatsDto(
    int TotalTracked,
    ByTypeDto ByType,
    ByAltitudeBandDto ByAltitudeBand,
    SourcesDto Sources,
    string LastUpdated);
