using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;
using Xunit;

namespace MissionClear.Tests.Data;

public sealed class MissionRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly MissionRepository _sut;

    public MissionRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _sut = new MissionRepository(_context);
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ReturnsMission()
    {
        // Arrange
        var mission = new MissionEntity { UserId = Guid.NewGuid(), Destination = "ISS", Status = "success" };
        _context.Missions.Add(mission);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(mission.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(mission.Id);
    }

    [Fact]
    public async Task GetPagedAsync_AppliesFiltersAndPagination()
    {
        // Arrange
        var userId = Guid.NewGuid();
        for (int i = 0; i < 10; i++)
        {
            _context.Missions.Add(new MissionEntity 
            { 
                UserId = userId, 
                Destination = i % 2 == 0 ? "ISS" : "SSO", 
                Status = i < 5 ? "success" : "failure" 
            });
        }
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetPagedAsync(userId, page: 1, limit: 3, status: "success", destination: "ISS");

        // Assert
        // i=0 (ISS, success), i=2 (ISS, success), i=4 (ISS, success) -> 3 items
        result.TotalCount.Should().Be(3);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetPagedAsync_AppliesSorting_ScoreDesc()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _context.Missions.AddRange(
            new MissionEntity { UserId = userId, Destination = "ISS", Status = "success", MissionScore = 10 },
            new MissionEntity { UserId = userId, Destination = "ISS", Status = "success", MissionScore = 50 },
            new MissionEntity { UserId = userId, Destination = "ISS", Status = "success", MissionScore = 30 }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetPagedAsync(userId, page: 1, limit: 10, sort: "score_desc");

        // Assert
        result.Items.First().MissionScore.Should().Be(50);
        result.Items.Last().MissionScore.Should().Be(10);
    }

    [Fact]
    public async Task GetUserStatsAsync_CalculatesCorrectProjections()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _context.Missions.AddRange(
            new MissionEntity 
            { 
                UserId = userId, Destination = "ISS", Status = "success", 
                MissionScore = 100, DeltaVKmS = 2.5, ObstaclesEncountered = 5 
            },
            new MissionEntity 
            { 
                UserId = userId, Destination = "ISS", Status = "failure", 
                MissionScore = 0, DeltaVKmS = 1.5, ObstaclesEncountered = 10 
            },
            new MissionEntity 
            { 
                UserId = userId, Destination = "SSO", Status = "aborted", 
                MissionScore = 0, DeltaVKmS = 0.5, ObstaclesEncountered = 2 
            }
        );
        await _context.SaveChangesAsync();

        // Act
        var stats = await _sut.GetUserStatsAsync(userId);

        // Assert
        stats.TotalMissions.Should().Be(3);
        stats.SuccessfulMissions.Should().Be(1);
        stats.FailedMissions.Should().Be(1);
        stats.AbortedMissions.Should().Be(1);
        stats.BestScore.Should().Be(100);
        stats.TotalDeltaV.Should().Be(4.5);
        stats.TotalObstacles.Should().Be(17);
        stats.FavoriteDestination.Should().Be("ISS");
        stats.MissionsByDestination["ISS"].Should().Be(2);
        stats.MissionsByDestination["SSO"].Should().Be(1);
    }

    [Fact]
    public async Task GetByIdAsync_WhenDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPagedAsync_AppliesSorting_CreatedAtDesc()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var m1 = new MissionEntity { UserId = userId, Destination = "ISS", Status = "success", CreatedAt = DateTime.UtcNow.AddMinutes(-10) };
        var m2 = new MissionEntity { UserId = userId, Destination = "ISS", Status = "success", CreatedAt = DateTime.UtcNow };
        _context.Missions.AddRange(m1, m2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetPagedAsync(userId, page: 1, limit: 10, sort: "created_at_desc");

        // Assert
        result.Items.First().Id.Should().Be(m2.Id);
    }

    [Fact]
    public async Task GetPagedAsync_AppliesSorting_RiskScoreAsc()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _context.Missions.AddRange(
            new MissionEntity { UserId = userId, Destination = "ISS", Status = "success", RiskScore = 0.8 },
            new MissionEntity { UserId = userId, Destination = "ISS", Status = "success", RiskScore = 0.2 },
            new MissionEntity { UserId = userId, Destination = "ISS", Status = "success", RiskScore = 0.5 }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetPagedAsync(userId, page: 1, limit: 10, sort: "risk_score_asc");

        // Assert
        result.Items.First().RiskScore.Should().Be(0.2);
        result.Items.Last().RiskScore.Should().Be(0.8);
    }

    [Fact]
    public async Task GetPagedAsync_WhenNoMissions_ReturnsEmpty()
    {
        // Act
        var result = await _sut.GetPagedAsync(Guid.NewGuid(), page: 1, limit: 10);

        // Assert
        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserStatsAsync_WhenNoMissions_ReturnsZeroStats()
    {
        // Act
        var stats = await _sut.GetUserStatsAsync(Guid.NewGuid());

        // Assert
        stats.TotalMissions.Should().Be(0);
        stats.FavoriteDestination.Should().BeNull();
        stats.MissionsByDestination.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromContext()
    {
        // Arrange
        var mission = new MissionEntity { UserId = Guid.NewGuid(), Destination = "ISS", Status = "success" };
        _context.Missions.Add(mission);
        await _context.SaveChangesAsync();

        // Act
        await _sut.DeleteAsync(mission);
        await _sut.SaveChangesAsync();

        // Assert
        _context.Missions.Any(m => m.Id == mission.Id).Should().BeFalse();
    }
}
