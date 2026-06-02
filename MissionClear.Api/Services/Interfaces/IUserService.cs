using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Dtos.User;

namespace MissionClear.Api.Services.Interfaces;

public interface IUserService
{
    Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<UserProfileResponse> UpdateProfileAsync(Guid userId, UpdateUserRequest request, CancellationToken ct = default);
    Task<FavoritesResponse> GetFavoritesAsync(Guid userId, CancellationToken ct = default);
    Task<FavoritesResponse> UpdateFavoritesAsync(Guid userId, UpdateFavoritesRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<DebrisDto>> GetFavoriteDebrisFilteredAsync(
        Guid userId, string? type, string sort, CancellationToken ct = default);

    Task<IReadOnlyList<object>> GetFavoriteWindowsFilteredAsync(
        Guid userId, string? destination, string sort, CancellationToken ct = default);
}
