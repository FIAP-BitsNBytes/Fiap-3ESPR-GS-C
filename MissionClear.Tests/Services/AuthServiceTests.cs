using FluentAssertions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using Moq;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IRefreshTokenRepository> _tokenRepo = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<UserEntity>())).Returns("access-token");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh-token");

        _service = new AuthService(
            _userRepo.Object,
            _tokenRepo.Object,
            _jwt.Object,
            Options.Create(new JwtSettings
            {
                Secret = "test-secret-key-must-be-32-chars-minimum!!",
                Issuer = "TestIssuer",
                Audience = "TestAudience",
                AccessTokenMinutes = 60,
                RefreshTokenDays = 7
            }));
    }

    // ── RegisterAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_CreatesUser_WithResearcherRole_ByDefault()
    {
        _userRepo.Setup(r => r.GetByEmailAsync("new@test.com", default)).ReturnsAsync((UserEntity?)null);
        _userRepo.Setup(r => r.AddAsync(It.IsAny<UserEntity>(), default)).Returns(Task.CompletedTask);
        _userRepo.Setup(r => r.SaveChangesAsync(default)).Returns(Task.CompletedTask);

        var result = await _service.RegisterAsync(
            new RegisterRequest("new@test.com", "Pass@word1", "New User"), default);

        result.User.Role.Should().Be("Researcher");
        result.AccessToken.Should().Be("access-token");

        _userRepo.Verify(r => r.AddAsync(
            It.Is<UserEntity>(u =>
                u.Role == "Researcher" &&
                !string.IsNullOrEmpty(u.PasswordHash) &&
                u.PasswordHash != "Pass@word1"),
            default));
    }

    [Fact]
    public async Task RegisterAsync_Throws_EMAIL_ALREADY_EXISTS_WhenEmailDuplicated()
    {
        _userRepo.Setup(r => r.GetByEmailAsync("dup@test.com", default))
            .ReturnsAsync(new UserEntity { Email = "dup@test.com", DisplayName = "D", PasswordHash = "h" });

        var act = () => _service.RegisterAsync(
            new RegisterRequest("dup@test.com", "Pass@word1", "Dup"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "EMAIL_ALREADY_EXISTS" && e.HttpStatus == 409);
    }

    [Theory]
    [InlineData("short1A")]       // menos de 8 chars
    [InlineData("nouppercase1")]  // sem maiúscula
    [InlineData("NoDigitsHere")]  // sem dígito
    public async Task RegisterAsync_Throws_INVALID_PASSWORD_FORMAT_WhenPasswordWeak(string weak)
    {
        _userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), default)).ReturnsAsync((UserEntity?)null);

        var act = () => _service.RegisterAsync(
            new RegisterRequest("a@test.com", weak, "User"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_PASSWORD_FORMAT" && e.HttpStatus == 400);
    }

    // ── LoginAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_ReturnsAuthResponse_WithRole()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Correct@1");
        _userRepo.Setup(r => r.GetByEmailAsync("u@test.com", default))
            .ReturnsAsync(new UserEntity
            {
                Id = Guid.NewGuid(),
                Email = "u@test.com",
                DisplayName = "U",
                PasswordHash = hash,
                Role = "Researcher"
            });

        var result = await _service.LoginAsync(new LoginRequest("u@test.com", "Correct@1"), default);

        result.User.Role.Should().Be("Researcher");
        result.AccessToken.Should().Be("access-token");
    }

    [Fact]
    public async Task LoginAsync_Throws_INVALID_CREDENTIALS_WhenUserNotFound()
    {
        _userRepo.Setup(r => r.GetByEmailAsync("ghost@test.com", default))
            .ReturnsAsync((UserEntity?)null);

        var act = () => _service.LoginAsync(new LoginRequest("ghost@test.com", "Pass@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CREDENTIALS" && e.HttpStatus == 401);
    }

    [Fact]
    public async Task LoginAsync_Throws_INVALID_CREDENTIALS_WhenPasswordWrong()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Correct@1");
        _userRepo.Setup(r => r.GetByEmailAsync("u@test.com", default))
            .ReturnsAsync(new UserEntity
            {
                Id = Guid.NewGuid(),
                Email = "u@test.com",
                DisplayName = "U",
                PasswordHash = hash,
                Role = "Researcher"
            });

        var act = () => _service.LoginAsync(new LoginRequest("u@test.com", "Wrong@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CREDENTIALS" && e.HttpStatus == 401);
    }

    // ── RefreshAsync ─────────────────────────────────────────────────────────

    [Fact]
    // No rotation: refresh_token is NOT revoked on refresh.
    // Mobile reuses the same token until it expires (TTL 7d).
    public async Task RefreshAsync_ReturnsNewAccessToken_NoRotation()
    {
        var userId = Guid.NewGuid();
        var existingToken = new RefreshTokenEntity
        {
            Token = "valid-token",
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = false
        };
        _tokenRepo.Setup(r => r.GetByTokenAsync("valid-token", default))
            .ReturnsAsync(existingToken);
        _userRepo.Setup(r => r.GetByIdAsync(userId, default))
            .ReturnsAsync(new UserEntity
            {
                Id = userId, Email = "u@test.com",
                DisplayName = "U", PasswordHash = "h", Role = "Researcher"
            });

        var result = await _service.RefreshAsync(new RefreshRequest("valid-token"), default);

        result.AccessToken.Should().Be("access-token");
        // CRITICAL: token must NOT be revoked — mobile reuses the same refresh_token
        _tokenRepo.Verify(r => r.RevokeByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _tokenRepo.Verify(r => r.AddAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_Throws_INVALID_REFRESH_TOKEN_WhenTokenNotFound()
    {
        _tokenRepo.Setup(r => r.GetByTokenAsync("bad-token", default))
            .ReturnsAsync((RefreshTokenEntity?)null);

        var act = () => _service.RefreshAsync(new RefreshRequest("bad-token"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_REFRESH_TOKEN" && e.HttpStatus == 401);
    }

    // ── LogoutAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LogoutAsync_RevokesToken_WhenCallerOwnsToken()
    {
        var callerId = Guid.NewGuid();
        var tokenEntity = new RefreshTokenEntity
        {
            UserId = callerId,
            Token  = "some-token",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        _tokenRepo.Setup(r => r.GetByTokenAsync("some-token", default))
                  .ReturnsAsync(tokenEntity);

        await _service.LogoutAsync(new LogoutRequest("some-token"), callerId, default);

        tokenEntity.IsRevoked.Should().BeTrue();
        _tokenRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_DoesNothing_WhenTokenBelongsToOtherUser()
    {
        var callerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var tokenEntity = new RefreshTokenEntity
        {
            UserId = otherUserId,
            Token  = "other-token",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        _tokenRepo.Setup(r => r.GetByTokenAsync("other-token", default))
                  .ReturnsAsync(tokenEntity);

        await _service.LogoutAsync(new LogoutRequest("other-token"), callerId, default);

        tokenEntity.IsRevoked.Should().BeFalse();
        _tokenRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogoutAsync_DoesNothing_WhenTokenNotFound()
    {
        var callerId = Guid.NewGuid();
        _tokenRepo.Setup(r => r.GetByTokenAsync("ghost-token", default))
                  .ReturnsAsync((RefreshTokenEntity?)null);

        await _service.LogoutAsync(new LogoutRequest("ghost-token"), callerId, default);

        _tokenRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
