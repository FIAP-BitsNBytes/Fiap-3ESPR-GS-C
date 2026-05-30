using System.Text.RegularExpressions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class UserService(IUserRepository userRepo, IMissionRepository missionRepo) : IUserService
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
