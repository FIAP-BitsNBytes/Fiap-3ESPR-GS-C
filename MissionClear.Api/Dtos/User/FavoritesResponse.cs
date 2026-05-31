using System.Text.Json.Serialization;

namespace MissionClear.Api.Dtos.User;

public sealed record FavoritesResponse(
    [property: JsonPropertyName("debris_ids")] string[] DebrisIds,
    [property: JsonPropertyName("windows")]    object[] Windows,
    [property: JsonPropertyName("updated_at")] string UpdatedAt);
