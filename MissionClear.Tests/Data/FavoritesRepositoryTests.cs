using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;
using Xunit;

namespace MissionClear.Tests.Data;

/// <summary>
/// Testa FavoritesRepository contra EF InMemory.
/// Cada instância usa banco isolado (Guid único) para evitar estado compartilhado.
/// </summary>
public sealed class FavoritesRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly FavoritesRepository _sut;

    public FavoritesRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _sut = new FavoritesRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedUserAsync()
    {
        var user = new UserEntity
        {
            Email        = $"{Guid.NewGuid():N}@test.com",
            DisplayName  = "Test",
            PasswordHash = "hash",
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user.Id;
    }

    private static UserSavedWindowEntity MakeWindowEntity(Guid userId, string windowId, string destination) =>
        new()
        {
            UserId      = userId,
            WindowId    = windowId,
            Destination = destination,
            WindowJson  = $$"""{"id":"{{windowId}}","destination":"{{destination}}"}""",
            SavedAt     = DateTime.UtcNow,
        };

    // ── GetDebrisAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDebrisAsync_NewUser_ReturnsEmptyList()
    {
        var userId = await SeedUserAsync();

        var result = await _sut.GetDebrisAsync(userId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDebrisAsync_WithEntries_ReturnsAll()
    {
        var userId = await SeedUserAsync();
        _context.FavoriteDebris.AddRange(
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "25544", SavedAt = DateTime.UtcNow },
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "37820", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var result = await _sut.GetDebrisAsync(userId);

        result.Should().HaveCount(2);
        result.Select(d => d.DebrisId).Should().BeEquivalentTo(["25544", "37820"]);
    }

    [Fact]
    public async Task GetDebrisAsync_OrdersBySavedAt_Ascending()
    {
        var userId = await SeedUserAsync();
        var t0     = DateTime.UtcNow.AddMinutes(-5);
        var t1     = DateTime.UtcNow;

        _context.FavoriteDebris.AddRange(
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "NEWER", SavedAt = t1 },
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "OLDER", SavedAt = t0 });
        await _context.SaveChangesAsync();

        var result = await _sut.GetDebrisAsync(userId);

        result[0].DebrisId.Should().Be("OLDER");
        result[1].DebrisId.Should().Be("NEWER");
    }

    [Fact]
    public async Task GetDebrisAsync_DoesNotReturnOtherUsersDebris()
    {
        var userId1 = await SeedUserAsync();
        var userId2 = await SeedUserAsync();
        _context.FavoriteDebris.Add(
            new UserFavoriteDebrisEntity { UserId = userId2, DebrisId = "OTHER", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var result = await _sut.GetDebrisAsync(userId1);

        result.Should().BeEmpty("must not leak another user's favorites");
    }

    // ── ReplaceDebrisAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDebrisAsync_WithNew_InsertsEntries()
    {
        var userId = await SeedUserAsync();

        await _sut.ReplaceDebrisAsync(userId, ["25544", "37820"]);
        await _sut.SaveChangesAsync();

        var saved = await _context.FavoriteDebris.Where(f => f.UserId == userId).ToListAsync();
        saved.Should().HaveCount(2);
        saved.Select(d => d.DebrisId).Should().BeEquivalentTo(["25544", "37820"]);
    }

    [Fact]
    public async Task ReplaceDebrisAsync_RemovesExistingBeforeInserting()
    {
        var userId = await SeedUserAsync();
        _context.FavoriteDebris.Add(
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "OLD", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        await _sut.ReplaceDebrisAsync(userId, ["NEW"]);
        await _sut.SaveChangesAsync();

        var saved = await _context.FavoriteDebris.Where(f => f.UserId == userId).ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].DebrisId.Should().Be("NEW", "OLD must have been removed");
    }

    [Fact]
    public async Task ReplaceDebrisAsync_Deduplicates_BeforeInserting()
    {
        var userId = await SeedUserAsync();

        await _sut.ReplaceDebrisAsync(userId, ["DUP", "DUP", "DUP", "UNIQUE"]);
        await _sut.SaveChangesAsync();

        var saved = await _context.FavoriteDebris.Where(f => f.UserId == userId).ToListAsync();
        saved.Should().HaveCount(2, "DUP must appear only once");
        saved.Select(d => d.DebrisId).Should().BeEquivalentTo(["DUP", "UNIQUE"]);
    }

    [Fact]
    public async Task ReplaceDebrisAsync_IgnoresBlankAndWhitespaceIds()
    {
        var userId = await SeedUserAsync();

        await _sut.ReplaceDebrisAsync(userId, ["25544", "", "  ", "37820"]);
        await _sut.SaveChangesAsync();

        var saved = await _context.FavoriteDebris.Where(f => f.UserId == userId).ToListAsync();
        saved.Should().HaveCount(2, "blank/whitespace IDs must be filtered out");
        saved.Select(d => d.DebrisId).Should().NotContain("");
        saved.Select(d => d.DebrisId).Should().NotContain("  ");
    }

    [Fact]
    public async Task ReplaceDebrisAsync_EnforcesMax500Limit()
    {
        var userId = await SeedUserAsync();
        var ids    = Enumerable.Range(1, 600).Select(i => $"ID_{i:D4}");

        await _sut.ReplaceDebrisAsync(userId, ids);
        await _sut.SaveChangesAsync();

        var count = await _context.FavoriteDebris.CountAsync(f => f.UserId == userId);
        count.Should().Be(500, "repository must cap at 500 entries");
    }

    [Fact]
    public async Task ReplaceDebrisAsync_WithEmptyList_ClearsAllDebris()
    {
        var userId = await SeedUserAsync();
        _context.FavoriteDebris.Add(
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "EXISTING", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        await _sut.ReplaceDebrisAsync(userId, []);
        await _sut.SaveChangesAsync();

        var count = await _context.FavoriteDebris.CountAsync(f => f.UserId == userId);
        count.Should().Be(0, "empty list must clear all favorites");
    }

    [Fact]
    public async Task ReplaceDebrisAsync_SetsUserId_Correctly()
    {
        var userId = await SeedUserAsync();

        await _sut.ReplaceDebrisAsync(userId, ["99999"]);
        await _sut.SaveChangesAsync();

        var saved = await _context.FavoriteDebris.FirstAsync(f => f.UserId == userId);
        saved.UserId.Should().Be(userId);
    }

    // ── GetWindowsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetWindowsAsync_NewUser_ReturnsEmptyList()
    {
        var userId = await SeedUserAsync();

        var result = await _sut.GetWindowsAsync(userId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWindowsAsync_WithEntries_ReturnsAll()
    {
        var userId = await SeedUserAsync();
        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId      = userId,
            WindowId    = "ISS_W1",
            Destination = "ISS",
            WindowJson  = """{"id":"ISS_W1","destination":"ISS"}""",
            SavedAt     = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        var result = await _sut.GetWindowsAsync(userId);

        result.Should().HaveCount(1);
        result[0].WindowId.Should().Be("ISS_W1");
        result[0].Destination.Should().Be("ISS");
    }

    [Fact]
    public async Task GetWindowsAsync_OrdersBySavedAt_Ascending()
    {
        var userId = await SeedUserAsync();
        var t0     = DateTime.UtcNow.AddMinutes(-5);
        var t1     = DateTime.UtcNow;

        _context.SavedWindows.AddRange(
            new UserSavedWindowEntity { UserId = userId, WindowId = "W_NEWER", Destination = "ISS",
                                        WindowJson = "{}", SavedAt = t1 },
            new UserSavedWindowEntity { UserId = userId, WindowId = "W_OLDER", Destination = "LEO_GENERIC",
                                        WindowJson = "{}", SavedAt = t0 });
        await _context.SaveChangesAsync();

        var result = await _sut.GetWindowsAsync(userId);

        result[0].WindowId.Should().Be("W_OLDER");
        result[1].WindowId.Should().Be("W_NEWER");
    }

    [Fact]
    public async Task GetWindowsAsync_DoesNotReturnOtherUsersWindows()
    {
        var userId1 = await SeedUserAsync();
        var userId2 = await SeedUserAsync();
        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId = userId2, WindowId = "THEIRS", Destination = "SSO",
            WindowJson = "{}", SavedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        var result = await _sut.GetWindowsAsync(userId1);

        result.Should().BeEmpty("must not leak another user's saved windows");
    }

    // ── ReplaceWindowsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceWindowsAsync_WithNew_InsertsEntries()
    {
        var userId = await SeedUserAsync();

        await _sut.ReplaceWindowsAsync(userId, [MakeWindowEntity(userId, "ISS_W1", "ISS")]);
        await _sut.SaveChangesAsync();

        var saved = await _context.SavedWindows.Where(w => w.UserId == userId).ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].WindowId.Should().Be("ISS_W1");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_RemovesExistingBeforeInserting()
    {
        var userId = await SeedUserAsync();
        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId = userId, WindowId = "OLD_W", Destination = "SSO",
            WindowJson = "{}", SavedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        await _sut.ReplaceWindowsAsync(userId, [MakeWindowEntity(userId, "NEW_W", "ISS")]);
        await _sut.SaveChangesAsync();

        var saved = await _context.SavedWindows.Where(w => w.UserId == userId).ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].WindowId.Should().Be("NEW_W", "OLD_W must have been removed");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_WithEmptyList_ClearsAllWindows()
    {
        var userId = await SeedUserAsync();
        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId = userId, WindowId = "EXISTING_W", Destination = "ISS",
            WindowJson = "{}", SavedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        await _sut.ReplaceWindowsAsync(userId, []);
        await _sut.SaveChangesAsync();

        var count = await _context.SavedWindows.CountAsync(w => w.UserId == userId);
        count.Should().Be(0, "empty list must clear all saved windows");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_EnforcesMax200Limit()
    {
        var userId  = await SeedUserAsync();
        var windows = Enumerable.Range(1, 250)
            .Select(i => MakeWindowEntity(userId, $"W_{i:D4}", "ISS"));

        await _sut.ReplaceWindowsAsync(userId, windows);
        await _sut.SaveChangesAsync();

        var count = await _context.SavedWindows.CountAsync(w => w.UserId == userId);
        count.Should().Be(200, "repository must cap at 200 entries");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_SetsUserId_Correctly()
    {
        var userId = await SeedUserAsync();

        await _sut.ReplaceWindowsAsync(userId, [MakeWindowEntity(userId, "W1", "ISS")]);
        await _sut.SaveChangesAsync();

        var saved = await _context.SavedWindows.FirstAsync(w => w.UserId == userId);
        saved.UserId.Should().Be(userId);
    }

    // ── SaveChangesAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SaveChangesAsync_PersistsNewFavorites()
    {
        var userId = await SeedUserAsync();
        await _sut.ReplaceDebrisAsync(userId, ["SAVE_TEST"]);

        await _sut.SaveChangesAsync();

        var count = await _context.FavoriteDebris.CountAsync(f => f.DebrisId == "SAVE_TEST");
        count.Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutChanges_DoesNotThrow()
    {
        var act = async () => await _sut.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    // ── Isolação entre usuários ───────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDebrisAsync_OnlyAffects_TargetUser()
    {
        var userId1 = await SeedUserAsync();
        var userId2 = await SeedUserAsync();

        _context.FavoriteDebris.AddRange(
            new UserFavoriteDebrisEntity { UserId = userId1, DebrisId = "U1_ID", SavedAt = DateTime.UtcNow },
            new UserFavoriteDebrisEntity { UserId = userId2, DebrisId = "U2_ID", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        await _sut.ReplaceDebrisAsync(userId1, ["U1_NEW"]);
        await _sut.SaveChangesAsync();

        var u2debris = await _context.FavoriteDebris.Where(f => f.UserId == userId2).ToListAsync();
        u2debris.Should().HaveCount(1);
        u2debris[0].DebrisId.Should().Be("U2_ID", "replacing user1's debris must not affect user2");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_OnlyAffects_TargetUser()
    {
        var userId1 = await SeedUserAsync();
        var userId2 = await SeedUserAsync();

        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId = userId2, WindowId = "U2_W", Destination = "ISS",
            WindowJson = "{}", SavedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        await _sut.ReplaceWindowsAsync(userId1, [MakeWindowEntity(userId1, "U1_W", "ISS")]);
        await _sut.SaveChangesAsync();

        var u2windows = await _context.SavedWindows.Where(w => w.UserId == userId2).ToListAsync();
        u2windows.Should().HaveCount(1);
        u2windows[0].WindowId.Should().Be("U2_W");
    }
}
