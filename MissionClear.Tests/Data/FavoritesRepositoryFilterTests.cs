using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;
using Xunit;

namespace MissionClear.Tests.Data;

public sealed class FavoritesRepositoryFilterTests
{
    private readonly AppDbContext _context;
    private readonly FavoritesRepository _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public FavoritesRepositoryFilterTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);
        _sut = new FavoritesRepository(_context);
    }

    private async Task SeedWindowsAsync()
    {
        _context.SavedWindows.AddRange(
            new UserSavedWindowEntity
            {
                UserId      = _userId,
                WindowId    = "ISS_2026-06-01T08:00:00Z",
                Destination = "ISS",
                WindowJson  = "{}",
                SavedAt     = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            },
            new UserSavedWindowEntity
            {
                UserId      = _userId,
                WindowId    = "LEO_2026-06-02T10:00:00Z",
                Destination = "LEO_GENERIC",
                WindowJson  = "{}",
                SavedAt     = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc),
            },
            new UserSavedWindowEntity
            {
                UserId      = Guid.NewGuid(),
                WindowId    = "ISS_other",
                Destination = "ISS",
                WindowJson  = "{}",
                SavedAt     = DateTime.UtcNow,
            }
        );
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetWindowsFilteredAsync_NullDestination_ReturnsAllForUser()
    {
        await SeedWindowsAsync();
        var result = await _sut.GetWindowsFilteredAsync(_userId, null);
        result.Should().HaveCount(2);
        result.Should().OnlyContain(w => w.UserId == _userId);
    }

    [Fact]
    public async Task GetWindowsFilteredAsync_WithDestination_ReturnsOnlyMatching()
    {
        await SeedWindowsAsync();
        var result = await _sut.GetWindowsFilteredAsync(_userId, "ISS");
        result.Should().HaveCount(1);
        result[0].Destination.Should().Be("ISS");
    }

    [Fact]
    public async Task GetWindowsFilteredAsync_UnknownDestination_ReturnsEmpty()
    {
        await SeedWindowsAsync();
        var result = await _sut.GetWindowsFilteredAsync(_userId, "UNKNOWN");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWindowsFilteredAsync_OrderedBySavedAtDesc()
    {
        await SeedWindowsAsync();
        var result = await _sut.GetWindowsFilteredAsync(_userId, null);
        result.Should().HaveCount(2);
        result[0].SavedAt.Should().BeAfter(result[1].SavedAt);
    }

    [Fact]
    public async Task GetWindowsFilteredAsync_EmptyUserId_ReturnsEmpty()
    {
        await SeedWindowsAsync();
        var result = await _sut.GetWindowsFilteredAsync(Guid.NewGuid(), null);
        result.Should().BeEmpty();
    }
}
