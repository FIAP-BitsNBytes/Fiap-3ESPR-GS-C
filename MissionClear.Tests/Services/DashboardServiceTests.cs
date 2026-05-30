using FluentAssertions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using Moq;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class DashboardServiceTests
{
    private readonly Mock<IOrbitalCache> _cache = new();
    private readonly Mock<IMissionRepository> _missionRepo = new();
    private readonly DashboardService _service;

    public DashboardServiceTests()
    {
        _service = new DashboardService(_cache.Object, _missionRepo.Object);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsOrbitalSummary_WhenNoUser()
    {
        var debrisList = new List<OrbitalObject>
        {
            new("1", "DEB-1", "debris", 0, 0, 400, 7.5, "celestrak", DateTime.UtcNow),
            new("2", "SAT-1", "satellite", 0, 0, 600, 7.5, "celestrak", DateTime.UtcNow)
        };

        _cache.Setup(c => c.GetAll()).Returns(debrisList.AsReadOnly());
        _cache.Setup(c => c.LastPropagation).Returns(DateTime.UtcNow);

        var result = await _service.GetSummaryAsync(null, null, default);

        result.Orbital.TotalTrackedObjects.Should().Be(2);
        result.Orbital.ByType.Debris.Should().Be(1);
        result.Orbital.ByType.Satellite.Should().Be(1);
        result.Orbital.ByAltitudeBand.Low200500km.Should().Be(1);
        result.Orbital.ByAltitudeBand.Mid5001000km.Should().Be(1);
        
        result.User.Should().BeNull();
        _missionRepo.Verify(r => r.GetPagedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsUserSummary_WhenUserIdProvided()
    {
        var userId = Guid.NewGuid();
        var displayName = "Test User";

        _cache.Setup(c => c.GetAll()).Returns(Array.Empty<OrbitalObject>());
        _cache.Setup(c => c.LastPropagation).Returns(DateTime.UtcNow);

        var missions = new List<MissionEntity>
        {
            new() { Id = Guid.NewGuid(), Destination = "ISS", Status = "success", MissionScore = 85, CreatedAt = DateTime.UtcNow }
        };

        _missionRepo
            .Setup(r => r.GetPagedAsync(userId, 1, 1, null, null, "created_at_desc", default))
            .ReturnsAsync(new MissionPageResult(missions, 5));
        
        _missionRepo
            .Setup(r => r.GetUserStatsAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(5, 4, 1, 0, 95, 60, 80.0, 50.0, 10, "ISS", new Dictionary<string, int>()));

        var result = await _service.GetSummaryAsync(userId, displayName, default);

        result.User.Should().NotBeNull();
        result.User!.DisplayName.Should().Be(displayName);
        result.User.TotalMissions.Should().Be(5);
        result.User.BestScore.Should().Be(95);
        result.User.LastMission.Should().NotBeNull();
        result.User.LastMission!.Destination.Should().Be("ISS");
        result.User.LastMission.Score.Should().Be(85);
    }

    [Fact]
    public async Task GetAlertsAsync_DetectsConjunctions_ForKnownDestinations()
    {
        // Debris dangerously close to ISS (lat=0, lon=0, alt=408)
        var debrisList = new List<OrbitalObject>
        {
            new("1", "DANGER", "debris", 0, 0, 408.5, 7.5, "celestrak", DateTime.UtcNow)
        };

        _cache.Setup(c => c.GetAll()).Returns(debrisList.AsReadOnly());

        var result = await _service.GetAlertsAsync(windowHours: 6, minRisk: "high", default);

        result.Alerts.Should().NotBeEmpty();
        result.Alerts[0].AffectedDestination.Should().Be("Estação Espacial Internacional");
        result.Alerts[0].RiskLevel.Should().Be("critical"); // Distance is 0.5km
        result.Alerts[0].DebrisId.Should().Be("1");
    }
}
