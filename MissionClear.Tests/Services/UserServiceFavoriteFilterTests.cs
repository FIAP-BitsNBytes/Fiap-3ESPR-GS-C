using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Entities;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using NSubstitute;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class UserServiceFavoriteFilterTests
{
    private readonly AppDbContext _context;
    private readonly IUserRepository _userRepo;
    private readonly IMissionRepository _missionRepo;
    private readonly IFavoritesRepository _favoritesRepo;
    private readonly IOrbitalCache _orbitalCache;
    private readonly UserService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public UserServiceFavoriteFilterTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options);

        _userRepo      = Substitute.For<IUserRepository>();
        _missionRepo   = Substitute.For<IMissionRepository>();
        _favoritesRepo = new FavoritesRepository(_context);
        _orbitalCache  = Substitute.For<IOrbitalCache>();

        _sut = new UserService(_userRepo, _missionRepo, _favoritesRepo, _orbitalCache);
    }

    private static OrbitalObject MakeOrbit(string id, string type, double altKm, double velKmS = 7.5, string? name = null) =>
        new(id, name ?? $"Object {id}", type, 0, 0, altKm, velKmS, "test", DateTime.UtcNow);

    private async Task SeedFavoriteDebrisAsync(params string[] ids)
    {
        foreach (var id in ids)
        {
            _context.FavoriteDebris.Add(new UserFavoriteDebrisEntity
            {
                UserId   = _userId,
                DebrisId = id,
                SavedAt  = DateTime.UtcNow,
            });
        }
        await _context.SaveChangesAsync();
    }

    private async Task SeedFavoriteWindowAsync(string destination, string windowJson, DateTime savedAt)
    {
        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId      = _userId,
            WindowId    = $"{destination}_{savedAt:O}",
            Destination = destination,
            WindowJson  = windowJson,
            SavedAt     = savedAt,
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFavoriteDebrisFilteredAsync_NoFavorites_ReturnsEmpty()
    {
        _orbitalCache.GetAll().Returns(new List<OrbitalObject>());
        var result = await _sut.GetFavoriteDebrisFilteredAsync(_userId, null, "altitude_asc");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFavoriteDebrisFilteredAsync_ReturnsOnlySavedIds()
    {
        await SeedFavoriteDebrisAsync("A", "B");
        _orbitalCache.GetAll().Returns(new List<OrbitalObject>
        {
            MakeOrbit("A", "debris",   500),
            MakeOrbit("B", "satellite",800),
            MakeOrbit("C", "debris",   400),
        });
        var result = await _sut.GetFavoriteDebrisFilteredAsync(_userId, null, "altitude_asc");
        result.Should().HaveCount(2);
        result.Select(d => d.Id).Should().BeEquivalentTo(new[] { "A", "B" });
    }

    [Fact]
    public async Task GetFavoriteDebrisFilteredAsync_TypeFilter_ReturnsOnlyMatchingType()
    {
        await SeedFavoriteDebrisAsync("A", "B", "C");
        _orbitalCache.GetAll().Returns(new List<OrbitalObject>
        {
            MakeOrbit("A", "debris",      500),
            MakeOrbit("B", "satellite",   800),
            MakeOrbit("C", "rocket_body", 600),
        });
        var result = await _sut.GetFavoriteDebrisFilteredAsync(_userId, "satellite", "altitude_asc");
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("B");
    }

    [Fact]
    public async Task GetFavoriteDebrisFilteredAsync_SortAltitudeAsc_OrdersCorrectly()
    {
        await SeedFavoriteDebrisAsync("A", "B", "C");
        _orbitalCache.GetAll().Returns(new List<OrbitalObject>
        {
            MakeOrbit("A", "debris", 900),
            MakeOrbit("B", "debris", 300),
            MakeOrbit("C", "debris", 600),
        });
        var result = await _sut.GetFavoriteDebrisFilteredAsync(_userId, null, "altitude_asc");
        result.Select(d => d.AltitudeKm).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetFavoriteDebrisFilteredAsync_SortAltitudeDesc_OrdersCorrectly()
    {
        await SeedFavoriteDebrisAsync("A", "B");
        _orbitalCache.GetAll().Returns(new List<OrbitalObject>
        {
            MakeOrbit("A", "debris", 300),
            MakeOrbit("B", "debris", 800),
        });
        var result = await _sut.GetFavoriteDebrisFilteredAsync(_userId, null, "altitude_desc");
        result.Select(d => d.AltitudeKm).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetFavoriteDebrisFilteredAsync_SortVelocityDesc_OrdersCorrectly()
    {
        await SeedFavoriteDebrisAsync("A", "B");
        _orbitalCache.GetAll().Returns(new List<OrbitalObject>
        {
            MakeOrbit("A", "debris", 500, velKmS: 7.0),
            MakeOrbit("B", "debris", 500, velKmS: 8.5),
        });
        var result = await _sut.GetFavoriteDebrisFilteredAsync(_userId, null, "velocity_desc");
        result[0].VelocityKmS.Should().BeGreaterThan(result[1].VelocityKmS);
    }

    [Fact]
    public async Task GetFavoriteDebrisFilteredAsync_SortNameAsc_OrdersAlphabetically()
    {
        await SeedFavoriteDebrisAsync("A", "B");
        _orbitalCache.GetAll().Returns(new List<OrbitalObject>
        {
            MakeOrbit("A", "debris", 500, name: "Zeta Debris"),
            MakeOrbit("B", "debris", 500, name: "Alpha Sat"),
        });
        var result = await _sut.GetFavoriteDebrisFilteredAsync(_userId, null, "name_asc");
        result[0].Name.Should().Be("Alpha Sat");
    }

    [Fact]
    public async Task GetFavoriteDebrisFilteredAsync_IdNotInCache_SkipsGracefully()
    {
        await SeedFavoriteDebrisAsync("KNOWN", "GHOST");
        _orbitalCache.GetAll().Returns(new List<OrbitalObject>
        {
            MakeOrbit("KNOWN", "debris", 500),
        });
        var result = await _sut.GetFavoriteDebrisFilteredAsync(_userId, null, "altitude_asc");
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("KNOWN");
    }

    [Fact]
    public async Task GetFavoriteWindowsFilteredAsync_NoWindows_ReturnsEmpty()
    {
        var result = await _sut.GetFavoriteWindowsFilteredAsync(_userId, null, "saved_at_desc");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFavoriteWindowsFilteredAsync_FilterByDestination()
    {
        await SeedFavoriteWindowAsync("ISS",        """{"id":"ISS_001","destination":"ISS"}""",        new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc));
        await SeedFavoriteWindowAsync("LEO_GENERIC", """{"id":"LEO_001","destination":"LEO_GENERIC"}""", new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        var result = await _sut.GetFavoriteWindowsFilteredAsync(_userId, "ISS", "saved_at_desc");
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetFavoriteWindowsFilteredAsync_NullDestination_ReturnsAll()
    {
        await SeedFavoriteWindowAsync("ISS",         "{}", new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc));
        await SeedFavoriteWindowAsync("LEO_GENERIC",  "{}", new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        var result = await _sut.GetFavoriteWindowsFilteredAsync(_userId, null, "saved_at_desc");
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetFavoriteWindowsFilteredAsync_MalformedJson_SkipsGracefully()
    {
        await SeedFavoriteWindowAsync("ISS", "NOT_JSON", new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc));
        var result = await _sut.GetFavoriteWindowsFilteredAsync(_userId, null, "saved_at_desc");
        result.Should().BeEmpty();
    }
}
