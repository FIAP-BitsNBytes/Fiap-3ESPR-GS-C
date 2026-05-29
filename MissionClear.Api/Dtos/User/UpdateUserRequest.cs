namespace MissionClear.Api.Dtos.User;

public sealed record UpdateUserRequest(
    string? DisplayName,
    string? Password,
    string? CurrentPassword);
