using MissionClear.Api.Dtos.User;

namespace MissionClear.Api.Services.Interfaces;

public interface IUserService
{
    Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateUserRequest request, CancellationToken ct = default);
}
