using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public sealed class FavoritesRepository(AppDbContext context) : IFavoritesRepository
{
    public async Task<IReadOnlyList<UserFavoriteDebrisEntity>> GetDebrisAsync(
        Guid userId, CancellationToken ct = default)
    {
        return await context.FavoriteDebris
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.SavedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UserSavedWindowEntity>> GetWindowsAsync(
        Guid userId, CancellationToken ct = default)
    {
        return await context.SavedWindows
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.SavedAt)
            .ToListAsync(ct);
    }

    public async Task ReplaceDebrisAsync(
        Guid userId, IEnumerable<string> debrisIds, CancellationToken ct = default)
    {
        var existing = await context.FavoriteDebris
            .Where(f => f.UserId == userId)
            .ToListAsync(ct);
        context.FavoriteDebris.RemoveRange(existing);

        var sanitised = debrisIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(500);

        foreach (var debrisId in sanitised)
        {
            context.FavoriteDebris.Add(new UserFavoriteDebrisEntity
            {
                UserId  = userId,
                DebrisId = debrisId,
                SavedAt  = DateTime.UtcNow,
            });
        }
    }

    public async Task ReplaceWindowsAsync(
        Guid userId, IEnumerable<UserSavedWindowEntity> windows, CancellationToken ct = default)
    {
        var existing = await context.SavedWindows
            .Where(w => w.UserId == userId)
            .ToListAsync(ct);
        context.SavedWindows.RemoveRange(existing);

        foreach (var window in windows.Take(200))
        {
            window.UserId = userId;
            context.SavedWindows.Add(window);
        }
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await context.SaveChangesAsync(ct);
    }
}
