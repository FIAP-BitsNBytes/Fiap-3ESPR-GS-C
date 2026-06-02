using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public interface IFavoritesRepository
{
    Task<IReadOnlyList<UserFavoriteDebrisEntity>> GetDebrisAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserSavedWindowEntity>> GetWindowsAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<UserSavedWindowEntity>> GetWindowsFilteredAsync(
        Guid userId,
        string? destination,
        CancellationToken ct = default);

    /// <summary>Atomically replaces all debris favorites for the user. Pass null to skip.</summary>
    Task ReplaceDebrisAsync(Guid userId, IEnumerable<string> debrisIds, CancellationToken ct = default);

    /// <summary>Atomically replaces all saved windows for the user. Pass null to skip.</summary>
    Task ReplaceWindowsAsync(Guid userId, IEnumerable<UserSavedWindowEntity> windows, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
