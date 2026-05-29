# Phase 06 — History + Dashboard Services

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Implementar MissionHistoryService (usa IMissionRepository) e DashboardService (usa IOrbitalCache + IMissionRepository).

**Mudança em relação ao plan-06 original:** Services dependem de IMissionRepository (não AppDbContext direto).

---

### Task 1: Interfaces

**Files:**
- Create: `MissionClear.Api/Services/Interfaces/IMissionHistoryService.cs`
- Create: `MissionClear.Api/Services/Interfaces/IDashboardService.cs`

- [ ] **Step 1: Escrever IMissionHistoryService.cs**

```csharp
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Dtos.History;

namespace MissionClear.Api.Services.Interfaces;

public interface IMissionHistoryService
{
    Task<PagedResponse<MissionSummaryDto>> GetMissionsAsync(
        Guid userId, int page, int limit,
        string? status, string? destination, string sort,
        CancellationToken ct = default);

    Task<MissionDetailResponse> GetMissionDetailAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<MissionStatsResponse> GetStatsAsync(Guid userId, CancellationToken ct = default);
    Task DeleteMissionAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task<MissionSummaryDto> SaveMissionAsync(
        Guid userId, string sessionId, string status,
        double riskScore, double deltaV, int score, int obstacles,
        DateTime departure, DateTime arrival, string destination,
        IReadOnlyList<object> obstaclesData,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Escrever IDashboardService.cs**

```csharp
using MissionClear.Api.Dtos.Dashboard;

namespace MissionClear.Api.Services.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(Guid? userId, CancellationToken ct = default);
    Task<AlertsResponse> GetAlertsAsync(int windowHours, string minRisk, CancellationToken ct = default);
}
```

---

### Task 2: MissionHistoryService

**Files:**
- Create: `MissionClear.Api/Services/MissionHistoryService.cs`

- [ ] **Step 1: Testes**

Em `MissionClear.Tests/Services/MissionHistoryServiceTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using Moq;

namespace MissionClear.Tests.Services;

public sealed class MissionHistoryServiceTests
{
    private readonly Mock<IMissionRepository> _missionRepo = new();
    private readonly MissionHistoryService _service;

    public MissionHistoryServiceTests()
    {
        _service = new MissionHistoryService(_missionRepo.Object);
    }

    [Fact]
    public async Task GetMissionsAsync_ReturnsPaginatedResult()
    {
        var userId = Guid.NewGuid();
        var missions = new List<MissionEntity>
        {
            new() { Id = Guid.NewGuid(), UserId = userId, Destination = "ISS",
                    Status = "success", MissionScore = 87, RiskScore = 0.1,
                    DeltaVKmS = 9.4, DepartureTime = DateTime.UtcNow, ArrivalTime = DateTime.UtcNow.AddHours(6) }
        };
        _missionRepo.Setup(r => r.FindByUserIdAsync(userId, 1, 20, null, null, "created_at_desc", default))
            .ReturnsAsync(new MissionPageResult(missions, 1));

        var result = await _service.GetMissionsAsync(userId, 1, 20, null, null, "created_at_desc", default);

        result.Data.Should().HaveCount(1);
        result.Pagination.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetMissionDetailAsync_Throws404_WhenNotFound()
    {
        _missionRepo.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((MissionEntity?)null);

        var act = () => _service.GetMissionDetailAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "MISSION_NOT_FOUND");
    }

    [Fact]
    public async Task GetMissionDetailAsync_Throws403_WhenNotOwner()
    {
        var missionOwnerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        _missionRepo.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(new MissionEntity
            {
                UserId = missionOwnerId, Destination = "ISS",
                Status = "success", DepartureTime = DateTime.UtcNow, ArrivalTime = DateTime.UtcNow.AddHours(6)
            });

        var act = () => _service.GetMissionDetailAsync(Guid.NewGuid(), otherUserId, default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
    }

    [Fact]
    public async Task DeleteMissionAsync_Throws403_WhenNotOwner()
    {
        _missionRepo.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(new MissionEntity
            {
                UserId = Guid.NewGuid(), Destination = "ISS",
                Status = "success", DepartureTime = DateTime.UtcNow, ArrivalTime = DateTime.UtcNow.AddHours(6)
            });

        var act = () => _service.DeleteMissionAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "FORBIDDEN");
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsStats()
    {
        var userId = Guid.NewGuid();
        _missionRepo.Setup(r => r.GetStatsByUserIdAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(10, 7, 2, 1, 97, 23, 80.0, 94.0, 15, "ISS",
                new() { { "ISS", 7 }, { "SSO", 3 } }));

        var result = await _service.GetStatsAsync(userId, default);

        result.TotalMissions.Should().Be(10);
        result.BestScore.Should().Be(97);
        result.FavoriteDestination.Should().Be("ISS");
    }
}
```

- [ ] **Step 2: Implementar MissionHistoryService.cs**

```csharp
using System.Text.Json;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Dtos.History;
using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class MissionHistoryService(IMissionRepository missionRepo) : IMissionHistoryService
{
    public async Task<PagedResponse<MissionSummaryDto>> GetMissionsAsync(
        Guid userId, int page, int limit, string? status, string? destination, string sort, CancellationToken ct)
    {
        var result = await missionRepo.FindByUserIdAsync(userId, page, limit, status, destination, sort, ct);
        var totalPages = (int)Math.Ceiling((double)result.Total / limit);
        var dtos = result.Items.Select(ToSummaryDto).ToList();
        return new PagedResponse<MissionSummaryDto>(dtos, new PaginationDto(page, limit, result.Total, totalPages));
    }

    public async Task<MissionDetailResponse> GetMissionDetailAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var mission = await missionRepo.FindByIdAsync(id, ct)
            ?? throw new DomainException("MISSION_NOT_FOUND", "Mission not found.", 404);

        if (mission.UserId != userId)
            throw new DomainException("FORBIDDEN", "Access denied.", 403);

        var obstacles = ParseObstacles(mission.ObstaclesJson);
        var scoring = MissionScoring.Compute(mission.DeltaVKmS, mission.RiskScore);
        var dest = KnownDestinations.FindById(mission.Destination);

        return new MissionDetailResponse(
            $"msn_{mission.Id:N}",
            mission.Destination,
            dest?.DisplayName ?? mission.Destination,
            mission.Status,
            mission.MissionScore,
            mission.RiskScore,
            mission.DeltaVKmS,
            mission.DepartureTime.ToString("O"),
            mission.ArrivalTime.ToString("O"),
            mission.CreatedAt.ToString("O"),
            obstacles,
            new ScoreBreakdownDto(scoring.Efficiency, scoring.Safety, scoring.Total));
    }

    public async Task<MissionStatsResponse> GetStatsAsync(Guid userId, CancellationToken ct)
    {
        var stats = await missionRepo.GetStatsByUserIdAsync(userId, ct);
        var successRate = stats.Total == 0 ? 0 : Math.Round((double)stats.Successful / stats.Total, 2);

        return new MissionStatsResponse(
            stats.Total, stats.Successful, stats.Failed, stats.Aborted,
            successRate, stats.BestScore, stats.WorstScore,
            (int)Math.Round(stats.AverageScore),
            Math.Round(stats.TotalDeltaV, 2), stats.TotalObstacles,
            stats.FavoriteDestination, stats.MissionsByDestination);
    }

    public async Task DeleteMissionAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var mission = await missionRepo.FindByIdAsync(id, ct)
            ?? throw new DomainException("MISSION_NOT_FOUND", "Mission not found.", 404);

        if (mission.UserId != userId)
            throw new DomainException("FORBIDDEN", "Access denied.", 403);

        await missionRepo.DeleteAsync(id, ct);
    }

    public async Task<MissionSummaryDto> SaveMissionAsync(
        Guid userId, string sessionId, string status, double riskScore,
        double deltaV, int score, int obstacles, DateTime departure, DateTime arrival,
        string destination, IReadOnlyList<object> obstaclesData, CancellationToken ct)
    {
        var entity = new MissionEntity
        {
            UserId = userId,
            Destination = destination,
            Status = status,
            MissionScore = score,
            RiskScore = riskScore,
            DeltaVKmS = deltaV,
            ObstaclesEncountered = obstacles,
            DepartureTime = departure,
            ArrivalTime = arrival,
            ObstaclesJson = JsonSerializer.Serialize(obstaclesData),
        };

        await missionRepo.CreateAsync(entity, ct);
        return ToSummaryDto(entity);
    }

    private static MissionSummaryDto ToSummaryDto(MissionEntity m)
    {
        var dest = KnownDestinations.FindById(m.Destination);
        return new MissionSummaryDto(
            $"msn_{m.Id:N}", m.Destination, dest?.DisplayName ?? m.Destination,
            m.Status, m.MissionScore, m.RiskScore, m.DeltaVKmS,
            m.ObstaclesEncountered,
            m.DepartureTime.ToString("O"), m.ArrivalTime.ToString("O"), m.CreatedAt.ToString("O"));
    }

    private static IReadOnlyList<ObstacleDto> ParseObstacles(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ObstacleDto>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
```

---

### Task 3: DashboardService

**Files:**
- Create: `MissionClear.Api/Services/DashboardService.cs`

- [ ] **Step 1: Testes**

Em `MissionClear.Tests/Services/DashboardServiceTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using Moq;

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
    public async Task GetSummaryAsync_ReturnsNullUser_WhenNoUserId()
    {
        _cache.Setup(c => c.GetAll()).Returns([]);
        _cache.Setup(c => c.LastPropagation).Returns(DateTime.UtcNow);

        var result = await _service.GetSummaryAsync(null, default);

        result.User.Should().BeNull();
    }

    [Fact]
    public async Task GetSummaryAsync_CountsDebrisByType()
    {
        var objects = new List<OrbitalObject>
        {
            new("1","D1","debris",0,0,400,7.5,"celestrak",DateTime.UtcNow),
            new("2","D2","debris",0,0,400,7.5,"celestrak",DateTime.UtcNow),
            new("3","S1","satellite",0,0,400,7.5,"celestrak",DateTime.UtcNow),
        };
        _cache.Setup(c => c.GetAll()).Returns(objects);
        _cache.Setup(c => c.LastPropagation).Returns(DateTime.UtcNow);

        var result = await _service.GetSummaryAsync(null, default);

        result.Orbital.ByType.Debris.Should().Be(2);
        result.Orbital.ByType.Satellite.Should().Be(1);
        result.Orbital.TotalTrackedObjects.Should().Be(3);
    }

    [Fact]
    public async Task GetAlertsAsync_ReturnsAlerts_FiltersbyMinRisk()
    {
        _cache.Setup(c => c.GetAll()).Returns([]);
        var result = await _service.GetAlertsAsync(6, "medium", default);
        result.Alerts.Should().NotBeNull();
        result.WindowHours.Should().Be(6);
    }
}
```

- [ ] **Step 2: Implementar DashboardService.cs**

```csharp
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Dtos.Dashboard;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class DashboardService(
    IOrbitalCache cache,
    IMissionRepository missionRepo) : IDashboardService
{
    private readonly ConjunctionDetector _detector = new();

    public async Task<DashboardSummaryResponse> GetSummaryAsync(Guid? userId, CancellationToken ct)
    {
        var all = cache.GetAll();
        var now = DateTime.UtcNow;

        int debris = 0, satellite = 0, rocket = 0;
        int low200 = 0, mid500 = 0, high1000 = 0;

        foreach (var o in all)
        {
            switch (o.Type)
            {
                case "debris": debris++; break;
                case "satellite": satellite++; break;
                case "rocket_body": rocket++; break;
            }
            if (o.AltitudeKm < 500) low200++;
            else if (o.AltitudeKm < 1000) mid500++;
            else high1000++;
        }

        var alertCount = CountActiveAlerts(all, now);

        var orbital = new OrbitalSummaryDto(
            all.Count,
            new ByTypeDto(debris, satellite, rocket),
            new ByAltitudeBandDto(low200, mid500, high1000),
            alertCount,
            (cache.LastPropagation ?? now).ToString("O"));

        UserDashboardDto? userDto = null;
        if (userId.HasValue)
        {
            var stats = await missionRepo.GetStatsByUserIdAsync(userId.Value, ct);
            userDto = new UserDashboardDto("", stats.Total, stats.BestScore, null);
        }

        return new DashboardSummaryResponse(orbital, userDto);
    }

    public Task<AlertsResponse> GetAlertsAsync(int windowHours, string minRisk, CancellationToken ct)
    {
        var all = cache.GetAll();
        var now = DateTime.UtcNow;
        var minLevel = ParseRiskLevel(minRisk);
        var alerts = new List<AlertDto>();

        foreach (var dest in KnownDestinations.All)
        {
            var conjunctions = _detector.Detect(dest, now, all);
            foreach (var c in conjunctions.Where(c => (int)c.RiskLevel >= (int)minLevel))
            {
                var minutesUntil = (int)(c.TimeOfClosestApproach - now).TotalMinutes;
                if (minutesUntil > windowHours * 60 || minutesUntil < 0) continue;

                alerts.Add(new AlertDto(
                    $"alrt_{Guid.NewGuid():N}",
                    c.DebrisId, c.DebrisName, dest.Id,
                    c.ClosestApproachKm,
                    c.TimeOfClosestApproach.ToString("O"),
                    c.RiskLevel.ToString().ToLowerInvariant(),
                    minutesUntil,
                    now.ToString("O")));
            }
        }

        return Task.FromResult(new AlertsResponse(alerts, windowHours, now.ToString("O")));
    }

    private int CountActiveAlerts(IReadOnlyList<OrbitalObject> all, DateTime now)
    {
        int count = 0;
        foreach (var dest in KnownDestinations.All)
        {
            var conjunctions = _detector.Detect(dest, now, all);
            count += conjunctions.Count(c => c.RiskLevel >= RiskLevel.High);
        }
        return count;
    }

    private static RiskLevel ParseRiskLevel(string level) => level.ToLower() switch
    {
        "low" => RiskLevel.Low,
        "high" => RiskLevel.High,
        "critical" => RiskLevel.Critical,
        _ => RiskLevel.Medium
    };
}
```

- [ ] **Step 3: Rodar testes**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "History|Dashboard" -v normal
```

- [ ] **Step 4: Commit**

```powershell
git add MissionClear.Api/Services/ MissionClear.Tests/Services/
git commit -m "feat(history): MissionHistoryService via Repository, DashboardService"
```
