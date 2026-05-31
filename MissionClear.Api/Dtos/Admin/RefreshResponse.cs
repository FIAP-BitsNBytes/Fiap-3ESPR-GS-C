using System.Text.Json.Serialization;

namespace MissionClear.Api.Dtos.Admin;

public sealed record RefreshResponse(
    [property: JsonPropertyName("objects_in_cache")] int ObjectsInCache,
    [property: JsonPropertyName("last_fetch")]       string LastFetch,
    [property: JsonPropertyName("message")]          string Message);
