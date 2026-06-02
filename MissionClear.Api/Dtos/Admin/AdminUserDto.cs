namespace MissionClear.Api.Dtos.Admin;

public sealed record AdminUserDto(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    string CreatedAt,
    int TotalMissions);
