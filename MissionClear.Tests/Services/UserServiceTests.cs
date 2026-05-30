using FluentAssertions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using Moq;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IMissionRepository> _missionRepo = new();
    private readonly UserService _service;

    public UserServiceTests()
    {
        _service = new UserService(_userRepo.Object, _missionRepo.Object);
    }

    private static UserEntity MakeUser(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Email = "u@test.com",
        DisplayName = "Test User",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Current@1"),
        Role = "Researcher"
    };

    private static MissionStatsProjection EmptyStats() =>
        new(0, 0, 0, 0, 0, 0, 0.0, 0.0, 0, null, new Dictionary<string, int>());

    // ── GetProfileAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfileAsync_ReturnsProfile_WithStats()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);

        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _missionRepo.Setup(r => r.GetUserStatsAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(5, 3, 1, 1, 95, 40, 72.0, 47.0, 8, "ISS",
                new Dictionary<string, int> { ["ISS"] = 3 }));

        var result = await _service.GetProfileAsync(userId, default);

        result.Role.Should().Be("Researcher");
        result.Stats.TotalMissions.Should().Be(5);
        result.Stats.FavoriteDestination.Should().Be("ISS");
    }

    [Fact]
    public async Task GetProfileAsync_Throws_USER_NOT_FOUND_WhenUserMissing()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((UserEntity?)null);

        var act = () => _service.GetProfileAsync(Guid.NewGuid(), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "USER_NOT_FOUND" && e.HttpStatus == 404);
    }

    // ── UpdateProfileAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfileAsync_Throws_INVALID_CURRENT_PASSWORD_WhenCurrentPasswordWrong()
    {
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);

        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _missionRepo.Setup(r => r.GetUserStatsAsync(userId, default))
            .ReturnsAsync(EmptyStats());

        var act = () => _service.UpdateProfileAsync(userId,
            new UpdateUserRequest(null, "NewPass@1", "WrongCurrent@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CURRENT_PASSWORD" && e.HttpStatus == 401);
    }
}
