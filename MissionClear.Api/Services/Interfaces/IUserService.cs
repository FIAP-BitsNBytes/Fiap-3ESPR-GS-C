using MissionClear.Api.Dtos.User;

namespace MissionClear.Api.Services.Interfaces;

public interface IUserService
{
    Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateUserRequest request, CancellationToken ct = default);
    Task<FavoritesResponse> GetFavoritesAsync(Guid userId, CancellationToken ct = default);
    Task<FavoritesResponse> UpdateFavoritesAsync(Guid userId, UpdateFavoritesRequest request, CancellationToken ct = default);
}
