namespace MissionClear.Api.Dtos.Status;

public sealed record SourceStatusDto(string Celestrak, string Keeptrack);

public sealed record StatusResponse(
    string Status,
    int TleCount,
    int PropagatedCount,
    string? LastTleFetch,
    string? LastPropagation,
    long UptimeSeconds,
    SourceStatusDto Sources);
