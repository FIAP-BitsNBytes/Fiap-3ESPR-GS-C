using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class AuthService(
    IUserRepository userRepo,
    IRefreshTokenRepository tokenRepo,
    IJwtService jwtService,
    IOptions<JwtSettings> jwtOptions) : IAuthService
{
    private static readonly Regex PasswordRegex =
        new(@"^(?=.*[A-Z])(?=.*\d).{8,}$", RegexOptions.Compiled);

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (!PasswordRegex.IsMatch(request.Password))
            throw new DomainException("INVALID_PASSWORD_FORMAT",
                "Password must be at least 8 characters with 1 uppercase and 1 digit.", 400);

        var existing = await userRepo.GetByEmailAsync(request.Email, ct);
        if (existing is not null)
            throw new DomainException("EMAIL_ALREADY_EXISTS", "Email already registered.", 409);

        var user = new UserEntity
        {
            Email = request.Email.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = "Researcher"  // Public registration always creates Researcher. Role promotion via admin endpoint only.
        };

        await userRepo.AddAsync(user, ct);
        await userRepo.SaveChangesAsync(ct);
        
        return await BuildAuthResponseAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userRepo.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new DomainException("INVALID_CREDENTIALS", "Email or password incorrect.", 401);

        return await BuildAuthResponseAsync(user, ct);
    }

    // No rotation: the same refresh_token remains valid until its ExpiresAt.
    // Mobile stores the original token and reuses it — do NOT revoke it here.
    public async Task<RefreshTokenResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var existing = await tokenRepo.GetByTokenAsync(request.RefreshToken, ct);

        if (existing is null || existing.IsRevoked || existing.ExpiresAt < DateTime.UtcNow)
            throw new DomainException("INVALID_REFRESH_TOKEN", "Token invalid or expired.", 401);

        var user = await userRepo.GetByIdAsync(existing.UserId, ct)
            ?? throw new DomainException("INVALID_REFRESH_TOKEN", "User not found.", 401);

        return new RefreshTokenResponse(
            jwtService.GenerateAccessToken(user),
            jwtOptions.Value.AccessTokenMinutes * 60);
    }

    public async Task LogoutAsync(LogoutRequest request, Guid callerId, CancellationToken ct = default)
    {
        var token = await tokenRepo.GetByTokenAsync(request.RefreshToken, ct);
        // Silently ignore if token not found or belongs to another user (no info leak).
        if (token is null || token.UserId != callerId) return;

        token.IsRevoked = true;
        await tokenRepo.SaveChangesAsync(ct);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(UserEntity user, CancellationToken ct)
    {
        var refreshToken = new RefreshTokenEntity
        {
            UserId = user.Id,
            Token = jwtService.GenerateRefreshToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)
        };
        await tokenRepo.AddAsync(refreshToken, ct);
        await tokenRepo.SaveChangesAsync(ct);

        return new AuthResponse(
            new UserInAuthResponse($"usr_{user.Id:N}", user.Email, user.DisplayName, user.Role, user.CreatedAt.ToString("O")),
            jwtService.GenerateAccessToken(user),
            refreshToken.Token,
            jwtOptions.Value.AccessTokenMinutes * 60);
    }
}
