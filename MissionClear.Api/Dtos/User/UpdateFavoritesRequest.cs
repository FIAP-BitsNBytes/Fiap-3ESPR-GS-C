using System.Text.Json;
using System.Text.Json.Serialization;

namespace MissionClear.Api.Dtos.User;

public sealed record UpdateFavoritesRequest(
    [property: JsonPropertyName("debris_ids")] string[]?         DebrisIds,
    [property: JsonPropertyName("windows")]    JsonElement[]?    Windows);
