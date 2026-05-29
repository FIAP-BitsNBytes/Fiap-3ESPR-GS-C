namespace MissionClear.Api.Dtos.Mission;

public sealed record SessionResponse(
    string SessionId,
    string Destination,
    string DepartureTime,
    string ArrivalTime,
    string StreamUrl,
    string ExpiresAt);
