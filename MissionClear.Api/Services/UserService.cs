using System.Text.Json;
using System.Text.RegularExpressions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class UserService(
    IUserRepository userRepo,
    IMissionRepository missionRepo,
    IFavoritesRepository favoritesRepo,
    IOrbitalCache orbitalCache) : IUserService
{
    private static readonly Regex PasswordRegex =
        new(@"^(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

    public async Task<UserProfileResponse> GetProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(userId, ct)
            ?? throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        return await BuildProfileAsync(user, ct);
    }

    public async Task<UserProfileResponse> UpdateProfileAsync(
        Guid userId, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(userId, ct)
            ?? throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        bool changed = false;

        if (request.Password is not null)
        {
            if (request.CurrentPassword is null ||
                !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                throw new DomainException("INVALID_CURRENT_PASSWORD", "Current password is incorrect.", 401);

            if (!PasswordRegex.IsMatch(request.Password))
                throw new DomainException("INVALID_PASSWORD_FORMAT",
                    "Password must be at least 8 characters with 1 uppercase and 1 digit.", 400);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            changed = true;
        }

        if (request.DisplayName is not null)
        {
            user.DisplayName = request.DisplayName.Trim();
            changed = true;
        }

        if (changed)
        {
            await userRepo.UpdateAsync(user, ct);
            await userRepo.SaveChangesAsync(ct);
        }
        
        return await BuildProfileAsync(user, ct);
    }

    public async Task<FavoritesResponse> GetFavoritesAsync(Guid userId, CancellationToken ct = default)
    {
        if (await userRepo.GetByIdAsync(userId, ct) is null)
            throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        var debris  = await favoritesRepo.GetDebrisAsync(userId, ct);
        var windows = await favoritesRepo.GetWindowsAsync(userId, ct);

        return BuildFavoritesResponse(debris, windows);
    }

    public async Task<FavoritesResponse> UpdateFavoritesAsync(
        Guid userId, UpdateFavoritesRequest request, CancellationToken ct = default)
    {
        if (await userRepo.GetByIdAsync(userId, ct) is null)
            throw new DomainException("USER_NOT_FOUND", "User not found.", 404);

        if (request.DebrisIds is not null)
            await favoritesRepo.ReplaceDebrisAsync(userId, request.DebrisIds, ct);

        if (request.Windows is not null)
        {
            var windowEntities = request.Windows.Take(200).Select(w => new UserSavedWindowEntity
            {
                UserId      = userId,
                WindowId    = w.TryGetProperty("id",          out var idP)   ? idP.GetString()   ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                Destination = w.TryGetProperty("destination", out var destP) ? destP.GetString() ?? ""                        : "",
                Label       = w.TryGetProperty("label",       out var lblP)  ? lblP.GetString()  : null,
                SavedAt     = w.TryGetProperty("saved_at",    out var saP) && DateTime.TryParse(saP.GetString(), out var sa)
                                  ? sa.ToUniversalTime() : DateTime.UtcNow,
                WindowJson  = w.ToString(),
            });
            await favoritesRepo.ReplaceWindowsAsync(userId, windowEntities, ct);
        }

        await favoritesRepo.SaveChangesAsync(ct);

        var debris      = await favoritesRepo.GetDebrisAsync(userId, ct);
        var savedWindows = await favoritesRepo.GetWindowsAsync(userId, ct);
        return BuildFavoritesResponse(debris, savedWindows);
    }

    public async Task<IReadOnlyList<DebrisDto>> GetFavoriteDebrisFilteredAsync(
        Guid userId, string? type, string sort, CancellationToken ct = default)
    {
        var favoriteEntities = await favoritesRepo.GetDebrisAsync(userId, ct);
        var savedIds = favoriteEntities.Select(f => f.DebrisId).ToHashSet(StringComparer.Ordinal);

        var query = orbitalCache.GetAll().Where(o => savedIds.Contains(o.Id));

        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(o => o.Type == type);

        IEnumerable<OrbitalObject> sorted = sort switch
        {
            "altitude_desc"  => query.OrderByDescending(o => o.AltitudeKm),
            "velocity_desc"  => query.OrderByDescending(o => o.VelocityKmS),
            "name_asc"       => query.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase),
            _                => query.OrderBy(o => o.AltitudeKm),
        };

        return sorted
            .Select(o => new DebrisDto(
                o.Id, o.Name, o.Type,
                o.Latitude, o.Longitude,
                o.AltitudeKm, o.VelocityKmS,
                o.Source, o.UpdatedAt.ToString("O")))
            .ToList();
    }

    public async Task<IReadOnlyList<object>> GetFavoriteWindowsFilteredAsync(
        Guid userId, string? destination, string sort, CancellationToken ct = default)
    {
        var entities = await favoritesRepo.GetWindowsFilteredAsync(userId, destination, ct);

        if (sort is "departure_asc" or "risk_asc")
        {
            var withMeta = entities
                .Select(e =>
                {
                    object? deserialized;
                    try { deserialized = System.Text.Json.JsonSerializer.Deserialize<object>(e.WindowJson); }
                    catch { return ((object, double)?)null; }
                    if (deserialized is null) return null;

                    double sortKey;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(e.WindowJson);
                        var root = doc.RootElement;
                        sortKey = sort == "departure_asc"
                            ? (root.TryGetProperty("window", out var win) &&
                               win.TryGetProperty("start", out var start) &&
                               DateTime.TryParse(start.GetString(), out var dt)
                               ? dt.Ticks : double.MaxValue)
                            : (root.TryGetProperty("window", out var winR) &&
                               winR.TryGetProperty("risk_score", out var risk)
                               ? risk.GetDouble() : double.MaxValue);
                    }
                    catch { sortKey = double.MaxValue; }

                    return ((object, double)?)(deserialized, sortKey);
                })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .OrderBy(x => x.Item2)
                .Select(x => x.Item1)
                .ToList();

            return withMeta;
        }

        return entities
            .Select(e =>
            {
                try { return System.Text.Json.JsonSerializer.Deserialize<object>(e.WindowJson); }
                catch { return null; }
            })
            .OfType<object>()
            .ToList();
    }

    private static FavoritesResponse BuildFavoritesResponse(
        IReadOnlyList<UserFavoriteDebrisEntity> debris,
        IReadOnlyList<UserSavedWindowEntity> windows)
    {
        var debrisIds = debris.Select(d => d.DebrisId).ToArray();

        var windowObjects = windows
            .Select(w =>
            {
                try { return JsonSerializer.Deserialize<object>(w.WindowJson) ?? new { }; }
                catch { return (object)new { }; }
            })
            .ToArray();

        var updatedAt = debris.Count > 0 || windows.Count > 0
            ? debris.Select(d => d.SavedAt).Concat(windows.Select(w => w.SavedAt)).Max()
            : DateTime.UtcNow;

        return new FavoritesResponse(debrisIds, windowObjects, updatedAt.ToString("O"));
    }

    private async Task<UserProfileResponse> BuildProfileAsync(UserEntity user, CancellationToken ct)
    {
        var stats = await missionRepo.GetUserStatsAsync(user.Id, ct);
        var successRate = stats.TotalMissions == 0 ? 0.0 : Math.Round((double)stats.SuccessfulMissions / stats.TotalMissions, 2);

        return new UserProfileResponse(
            $"usr_{user.Id:N}",
            user.Email,
            user.DisplayName,
            user.Role,
            user.CreatedAt.ToString("O"),
            new UserStatsDto(
                stats.TotalMissions,
                stats.SuccessfulMissions,
                stats.FailedMissions,
                stats.AbortedMissions,
                successRate,
                stats.BestScore,
                stats.TotalMissions == 0 ? 0 : (int)Math.Round(stats.AverageScore),
                stats.FavoriteDestination,
                Math.Round(stats.TotalDeltaV, 2)));
    }
}
