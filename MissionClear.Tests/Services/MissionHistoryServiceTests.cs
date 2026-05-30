using FluentAssertions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using Moq;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class MissionHistoryServiceTests
{
    private readonly Mock<IMissionRepository> _missionRepo = new();
    private readonly MissionHistoryService _service;

    public MissionHistoryServiceTests()
    {
        _service = new MissionHistoryService(_missionRepo.Object);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetMissionsAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMissionsAsync_ReturnsPaginatedResult()
    {
        var userId = Guid.NewGuid();
        var missions = new List<MissionEntity>
        {
            new()
            {
                Id = Guid.NewGuid(), UserId = userId, Destination = "ISS",
                Status = "success", MissionScore = 87, RiskScore = 0.1,
                DeltaVKmS = 9.4, DepartureTime = DateTime.UtcNow,
                ArrivalTime = DateTime.UtcNow.AddHours(6)
            }
        };
        _missionRepo
            .Setup(r => r.GetPagedAsync(userId, 1, 20, null, null, "created_at_desc", default))
            .ReturnsAsync(new MissionPageResult(missions, 1));

        var result = await _service.GetMissionsAsync(userId, 1, 20, null, null, "created_at_desc", default);

        result.Data.Should().HaveCount(1);
        result.Pagination.Total.Should().Be(1);
        result.Pagination.Page.Should().Be(1);
        result.Pagination.Limit.Should().Be(20);
        result.Data[0].Id.Should().StartWith("msn_");
    }

    [Fact]
    public async Task GetMissionsAsync_PaginationCalculatesTotalPages()
    {
        var userId = Guid.NewGuid();
        var missions = Enumerable.Range(0, 5).Select(_ => new MissionEntity
        {
            Id = Guid.NewGuid(), UserId = userId, Destination = "ISS",
            Status = "success", MissionScore = 70, RiskScore = 0.1,
            DeltaVKmS = 9.4, DepartureTime = DateTime.UtcNow,
            ArrivalTime = DateTime.UtcNow.AddHours(6)
        }).ToList();

        _missionRepo
            .Setup(r => r.GetPagedAsync(userId, 1, 5, null, null, "created_at_desc", default))
            .ReturnsAsync(new MissionPageResult(missions, 12));

        var result = await _service.GetMissionsAsync(userId, 1, 5, null, null, "created_at_desc", default);

        result.Pagination.Total.Should().Be(12);
        result.Pagination.TotalPages.Should().Be(3);
        result.Data.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetMissionsAsync_DestinationDisplayPopulated()
    {
        var userId = Guid.NewGuid();
        var mission = new MissionEntity
        {
            Id = Guid.NewGuid(), UserId = userId, Destination = "ISS",
            Status = "success", MissionScore = 87, RiskScore = 0.1,
            DeltaVKmS = 9.4, DepartureTime = DateTime.UtcNow,
            ArrivalTime = DateTime.UtcNow.AddHours(6)
        };
        _missionRepo
            .Setup(r => r.GetPagedAsync(userId, 1, 20, null, null, "created_at_desc", default))
            .ReturnsAsync(new MissionPageResult([mission], 1));

        var result = await _service.GetMissionsAsync(userId, 1, 20, null, null, "created_at_desc", default);

        result.Data[0].DestinationDisplay.Should().Be("Estação Espacial Internacional");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetMissionDetailAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMissionDetailAsync_Throws404_WhenNotFound()
    {
        _missionRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((MissionEntity?)null);

        var act = () => _service.GetMissionDetailAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "MISSION_NOT_FOUND" && e.HttpStatus == 404);
    }

    [Fact]
    public async Task GetMissionDetailAsync_Throws403_WhenNotOwner()
    {
        var missionOwner = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        _missionRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(new MissionEntity
            {
                UserId = missionOwner, Destination = "ISS",
                Status = "success", DepartureTime = DateTime.UtcNow,
                ArrivalTime = DateTime.UtcNow.AddHours(6)
            });

        var act = () => _service.GetMissionDetailAsync(Guid.NewGuid(), otherUser, default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN" && e.HttpStatus == 403);
    }

    [Fact]
    public async Task GetMissionDetailAsync_ReturnsDetail_WithScoreBreakdown()
    {
        var userId = Guid.NewGuid();
        var missionId = Guid.NewGuid();

        _missionRepo
            .Setup(r => r.GetByIdAsync(missionId, default))
            .ReturnsAsync(new MissionEntity
            {
                Id = missionId, UserId = userId, Destination = "ISS",
                Status = "success", MissionScore = 56,
                RiskScore = 0.1, DeltaVKmS = 9.4,
                DepartureTime = DateTime.UtcNow,
                ArrivalTime = DateTime.UtcNow.AddHours(6),
                ObstaclesJson = "[]"
            });

        var result = await _service.GetMissionDetailAsync(missionId, userId, default);

        // efficiency = (1 - 9.4/12)*50 ≈ 10.83; safety = (1 - 0.1)*50 = 45; total = 56
        result.Id.Should().StartWith("msn_");
        result.ScoreBreakdown.Total.Should().Be(56);
        result.ScoreBreakdown.SafetyScore.Should().Be(45);
        result.DestinationDisplay.Should().Be("Estação Espacial Internacional");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetStatsAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_ReturnsMappedStats()
    {
        var userId = Guid.NewGuid();
        _missionRepo
            .Setup(r => r.GetUserStatsAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(
                10, 7, 2, 1,
                97, 23,
                80.0, 94.0,
                15,
                "ISS",
                new Dictionary<string, int> { { "ISS", 7 }, { "SSO", 3 } }));

        var result = await _service.GetStatsAsync(userId, default);

        result.TotalMissions.Should().Be(10);
        result.SuccessfulMissions.Should().Be(7);
        result.FailedMissions.Should().Be(2);
        result.AbortedMissions.Should().Be(1);
        result.BestScore.Should().Be(97);
        result.WorstScore.Should().Be(23);
        result.FavoriteDestination.Should().Be("ISS");
        result.MissionsByDestination.Should().ContainKey("ISS").WhoseValue.Should().Be(7);
    }

    [Fact]
    public async Task GetStatsAsync_SuccessRateRounded()
    {
        var userId = Guid.NewGuid();
        _missionRepo
            .Setup(r => r.GetUserStatsAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(
                10, 7, 2, 1, 97, 23, 80.0, 94.0, 15, "ISS",
                new Dictionary<string, int> { { "ISS", 7 }, { "SSO", 3 } }));

        var result = await _service.GetStatsAsync(userId, default);

        // 7 / 10 = 0.70
        result.SuccessRate.Should().Be(0.70);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DeleteMissionAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMissionAsync_Throws403_WhenNotOwner()
    {
        _missionRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(new MissionEntity
            {
                UserId = Guid.NewGuid(), Destination = "ISS",
                Status = "success", DepartureTime = DateTime.UtcNow,
                ArrivalTime = DateTime.UtcNow.AddHours(6)
            });

        var act = () => _service.DeleteMissionAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN" && e.HttpStatus == 403);
    }

    [Fact]
    public async Task DeleteMissionAsync_CallsRepoDelete_WhenOwner()
    {
        var userId = Guid.NewGuid();
        var missionId = Guid.NewGuid();
        var mission = new MissionEntity
        {
            Id = missionId, UserId = userId, Destination = "ISS",
            Status = "success", DepartureTime = DateTime.UtcNow,
            ArrivalTime = DateTime.UtcNow.AddHours(6)
        };

        _missionRepo
            .Setup(r => r.GetByIdAsync(missionId, default))
            .ReturnsAsync(mission);
        
        _missionRepo
            .Setup(r => r.DeleteAsync(mission, default))
            .Returns(Task.CompletedTask);

        await _service.DeleteMissionAsync(missionId, userId, default);

        _missionRepo.Verify(r => r.DeleteAsync(mission, default), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SaveMissionAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveMissionAsync_CreatesEntityAndReturns()
    {
        var userId = Guid.NewGuid();
        var departure = DateTime.UtcNow;
        var arrival = departure.AddHours(6);

        _missionRepo
            .Setup(r => r.AddAsync(It.IsAny<MissionEntity>(), default))
            .Returns(Task.CompletedTask);

        var result = await _service.SaveMissionAsync(
            userId, "sess_abc", "success",
            riskScore: 0.1, deltaV: 9.4, score: 56, obstacles: 2,
            departure, arrival, "ISS", [], default);

        result.Id.Should().StartWith("msn_");
        result.Destination.Should().Be("ISS");
        result.DestinationDisplay.Should().Be("Estação Espacial Internacional");
        result.Status.Should().Be("success");
        
        _missionRepo.Verify(r => r.AddAsync(
            It.Is<MissionEntity>(e => e.UserId == userId && e.Destination == "ISS"),
            default), Times.Once);
    }
}
