using System.Text.Json;
using FluentAssertions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using Moq;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class UserServiceTests
{
    private readonly Mock<IUserRepository>      _userRepo      = new();
    private readonly Mock<IMissionRepository>   _missionRepo   = new();
    private readonly Mock<IFavoritesRepository> _favoritesRepo = new();
    private readonly Mock<IOrbitalCache>        _orbitalCache  = new();
    private readonly UserService _service;

    public UserServiceTests()
    {
        _service = new UserService(_userRepo.Object, _missionRepo.Object, _favoritesRepo.Object, _orbitalCache.Object);
    }

    private static UserEntity MakeUser(Guid? id = null) => new()
    {
        Id           = id ?? Guid.NewGuid(),
        Email        = "u@test.com",
        DisplayName  = "Test User",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Current@1"),
        Role         = "Researcher",
    };

    private static MissionStatsProjection EmptyStats() =>
        new(0, 0, 0, 0, 0, 0, 0.0, 0.0, 0, null, new Dictionary<string, int>());

    private static JsonElement MakeWindowElement(string id = "W1", string dest = "ISS") =>
        JsonDocument.Parse($$$"""{"id":"{{{id}}}","destination":"{{{dest}}}","saved_at":"2026-05-30T12:00:00Z"}""").RootElement;

    // ── GetProfileAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfileAsync_ReturnsProfile_WithStats()
    {
        var userId = Guid.NewGuid();
        var user   = MakeUser(userId);

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
        var user   = MakeUser(userId);

        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(user);
        _missionRepo.Setup(r => r.GetUserStatsAsync(userId, default)).ReturnsAsync(EmptyStats());

        var act = () => _service.UpdateProfileAsync(userId,
            new UpdateUserRequest(null, "NewPass@1", "WrongCurrent@1"), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_CURRENT_PASSWORD" && e.HttpStatus == 401);
    }

    // ── GetFavoritesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetFavoritesAsync_UserNotFound_Throws()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((UserEntity?)null);

        var act = () => _service.GetFavoritesAsync(Guid.NewGuid(), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "USER_NOT_FOUND" && e.HttpStatus == 404);
    }

    [Fact]
    public async Task GetFavoritesAsync_NewUser_ReturnsEmptyArrays()
    {
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        _favoritesRepo.Setup(r => r.GetDebrisAsync(userId, default))
            .ReturnsAsync(new List<UserFavoriteDebrisEntity>());
        _favoritesRepo.Setup(r => r.GetWindowsAsync(userId, default))
            .ReturnsAsync(new List<UserSavedWindowEntity>());

        var result = await _service.GetFavoritesAsync(userId, default);

        result.DebrisIds.Should().BeEmpty();
        result.Windows.Should().BeEmpty();
        result.UpdatedAt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetFavoritesAsync_WithData_ReturnsDebrisIds()
    {
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        _favoritesRepo.Setup(r => r.GetDebrisAsync(userId, default))
            .ReturnsAsync(new List<UserFavoriteDebrisEntity>
            {
                new() { UserId = userId, DebrisId = "25544", SavedAt = DateTime.UtcNow },
                new() { UserId = userId, DebrisId = "12345", SavedAt = DateTime.UtcNow },
            });
        _favoritesRepo.Setup(r => r.GetWindowsAsync(userId, default))
            .ReturnsAsync(new List<UserSavedWindowEntity>());

        var result = await _service.GetFavoritesAsync(userId, default);

        result.DebrisIds.Should().BeEquivalentTo(["25544", "12345"]);
    }

    [Fact]
    public async Task GetFavoritesAsync_WithWindows_ReturnsDeserializedWindows()
    {
        var userId     = Guid.NewGuid();
        var windowJson = """{"id":"ISS_W1","destination":"ISS","saved_at":"2026-05-30T12:00:00Z"}""";

        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        _favoritesRepo.Setup(r => r.GetDebrisAsync(userId, default))
            .ReturnsAsync(new List<UserFavoriteDebrisEntity>());
        _favoritesRepo.Setup(r => r.GetWindowsAsync(userId, default))
            .ReturnsAsync(new List<UserSavedWindowEntity>
            {
                new() { UserId = userId, WindowId = "ISS_W1", Destination = "ISS",
                        WindowJson = windowJson, SavedAt = DateTime.UtcNow },
            });

        var result = await _service.GetFavoritesAsync(userId, default);

        result.Windows.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetFavoritesAsync_UpdatedAt_IsIso8601()
    {
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        _favoritesRepo.Setup(r => r.GetDebrisAsync(userId, default))
            .ReturnsAsync(new List<UserFavoriteDebrisEntity>());
        _favoritesRepo.Setup(r => r.GetWindowsAsync(userId, default))
            .ReturnsAsync(new List<UserSavedWindowEntity>());

        var result = await _service.GetFavoritesAsync(userId, default);

        var parsed = DateTime.Parse(result.UpdatedAt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);
        parsed.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ── UpdateFavoritesAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateFavoritesAsync_UserNotFound_Throws()
    {
        _userRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((UserEntity?)null);

        var act = () => _service.UpdateFavoritesAsync(Guid.NewGuid(),
            new UpdateFavoritesRequest(["25544"], null), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "USER_NOT_FOUND" && e.HttpStatus == 404);
    }

    [Fact]
    public async Task UpdateFavoritesAsync_WithDebrisIds_CallsReplaceDebris()
    {
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        SetupEmptyFavoritesMock(userId);

        await _service.UpdateFavoritesAsync(userId,
            new UpdateFavoritesRequest(["25544", "12345"], null), default);

        _favoritesRepo.Verify(r => r.ReplaceDebrisAsync(userId,
            It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "25544", "12345" })),
            default), Times.Once);
    }

    [Fact]
    public async Task UpdateFavoritesAsync_NullDebrisIds_DoesNotCallReplaceDebris()
    {
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        SetupEmptyFavoritesMock(userId);

        await _service.UpdateFavoritesAsync(userId,
            new UpdateFavoritesRequest(null, null), default);

        _favoritesRepo.Verify(r => r.ReplaceDebrisAsync(It.IsAny<Guid>(),
            It.IsAny<IEnumerable<string>>(), default), Times.Never);
    }

    [Fact]
    public async Task UpdateFavoritesAsync_WithWindows_CallsReplaceWindows()
    {
        var userId  = Guid.NewGuid();
        var windows = new[] { MakeWindowElement("W1"), MakeWindowElement("W2") };
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        SetupEmptyFavoritesMock(userId);

        await _service.UpdateFavoritesAsync(userId,
            new UpdateFavoritesRequest(null, windows), default);

        _favoritesRepo.Verify(r => r.ReplaceWindowsAsync(userId,
            It.Is<IEnumerable<UserSavedWindowEntity>>(ws => ws.Count() == 2),
            default), Times.Once);
    }

    [Fact]
    public async Task UpdateFavoritesAsync_NullWindows_DoesNotCallReplaceWindows()
    {
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        SetupEmptyFavoritesMock(userId);

        await _service.UpdateFavoritesAsync(userId,
            new UpdateFavoritesRequest(["X"], null), default);

        _favoritesRepo.Verify(r => r.ReplaceWindowsAsync(It.IsAny<Guid>(),
            It.IsAny<IEnumerable<UserSavedWindowEntity>>(), default), Times.Never);
    }

    [Fact]
    public async Task UpdateFavoritesAsync_CallsSaveChangesExactlyOnce()
    {
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        SetupEmptyFavoritesMock(userId);

        await _service.UpdateFavoritesAsync(userId,
            new UpdateFavoritesRequest(["25544"], new[] { MakeWindowElement() }), default);

        _favoritesRepo.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateFavoritesAsync_RepositoryThrows_PropagatesWithoutSwallowing()
    {
        var userId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));
        _favoritesRepo.Setup(r => r.ReplaceDebrisAsync(It.IsAny<Guid>(),
                It.IsAny<IEnumerable<string>>(), default))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var act = () => _service.UpdateFavoritesAsync(userId,
            new UpdateFavoritesRequest(["25544"], null), default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("db down");
    }

    [Fact]
    public async Task UpdateFavoritesAsync_WindowEntity_ExtractsWindowIdFromJson()
    {
        var userId  = Guid.NewGuid();
        var windows = new[] { MakeWindowElement("ISS_TEST_ID", "ISS") };
        _userRepo.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync(MakeUser(userId));

        // Configurar apenas o necessário — sem SetupEmptyFavoritesMock para evitar conflito no ReplaceWindowsAsync
        _favoritesRepo.Setup(r => r.SaveChangesAsync(default)).Returns(Task.CompletedTask);
        _favoritesRepo.Setup(r => r.GetDebrisAsync(userId, default))
            .ReturnsAsync(new List<UserFavoriteDebrisEntity>());
        _favoritesRepo.Setup(r => r.GetWindowsAsync(userId, default))
            .ReturnsAsync(new List<UserSavedWindowEntity>());
        _favoritesRepo.Setup(r => r.ReplaceDebrisAsync(It.IsAny<Guid>(),
            It.IsAny<IEnumerable<string>>(), default)).Returns(Task.CompletedTask);

        List<UserSavedWindowEntity>? captured = null;
        _favoritesRepo.Setup(r => r.ReplaceWindowsAsync(userId,
                It.IsAny<IEnumerable<UserSavedWindowEntity>>(), default))
            .Callback<Guid, IEnumerable<UserSavedWindowEntity>, CancellationToken>(
                (_, ws, _) => captured = ws.ToList())  // materializar para evitar lazy enum issues
            .Returns(Task.CompletedTask);

        await _service.UpdateFavoritesAsync(userId,
            new UpdateFavoritesRequest(null, windows), default);

        captured.Should().NotBeNull();
        var first = captured![0];
        first.WindowId.Should().Be("ISS_TEST_ID");
        first.Destination.Should().Be("ISS");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void SetupEmptyFavoritesMock(Guid userId)
    {
        _favoritesRepo.Setup(r => r.GetDebrisAsync(userId, default))
            .ReturnsAsync(new List<UserFavoriteDebrisEntity>());
        _favoritesRepo.Setup(r => r.GetWindowsAsync(userId, default))
            .ReturnsAsync(new List<UserSavedWindowEntity>());
        _favoritesRepo.Setup(r => r.ReplaceDebrisAsync(It.IsAny<Guid>(),
            It.IsAny<IEnumerable<string>>(), default)).Returns(Task.CompletedTask);
        _favoritesRepo.Setup(r => r.ReplaceWindowsAsync(It.IsAny<Guid>(),
            It.IsAny<IEnumerable<UserSavedWindowEntity>>(), default)).Returns(Task.CompletedTask);
        _favoritesRepo.Setup(r => r.SaveChangesAsync(default)).Returns(Task.CompletedTask);
    }
}
