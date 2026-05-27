# Plan 06 — Mission History + Dashboard Services

**Execution order:** After plan-01 (DB), plan-02 (DTOs), plan-03 (OrbitalCache). Parallel with plan-04 + plan-05.
**Estimated time:** 90 minutes.
**Goal:** Implementar `MissionHistoryService` (CRUD de missões do usuário autenticado) e `DashboardService` (estatísticas orbitais + alertas de conjunção).

**Dependencies:**
- plan-01 — `MissionEntity`, `SessionStatus` enum, `AppDbContext.Missions`
- plan-02 — `PagedResponse<T>`, `PaginationDto`, `MissionHistoryDto`, `MissionDetailDto`, `MissionStatsDto`, `ScoreBreakdownDto`, `DashboardSummaryResponse`, `DashboardAlertsResponse`, `DashboardAlertDto`, `UserResponse`, `ConjunctionDto`
- plan-03 — `OrbitalCache`, `OrbitalObject`
- plan-04 — `DomainException`

**Unlocks:** plan-07-controllers.md (endpoints `/api/missions/*` e `/api/dashboard/*`)

---

## Visão Geral

Dois serviços independentes, ambos `Scoped`, totalmente testáveis com EF Core InMemory e dados sintéticos de `OrbitalObject`.

1. **MissionHistoryService** — operações CRUD sobre `MissionEntity` filtradas por `UserId`. Aplica regras de autorização (403 quando dono diferente) e calcula score na persistência.
2. **DashboardService** — sem estado próprio; agrega `OrbitalCache` (já populado pelo job de propagação) e produz contadores + alertas de proximidade.

Regras invioláveis:
- Usuário **só** acessa suas próprias missões — toda leitura/escrita compara `UserId`.
- Score é determinístico (`efficiency + safety`), nunca recebido do cliente.
- Hard delete só executa após verificação de propriedade.
- `DashboardService` é puro sobre snapshots `IReadOnlyList<OrbitalObject>` — facilita teste sem cache.

---

## Task 6.0 — Constantes compartilhadas

**File:** `MissionClear.Api/Configuration/DashboardConstants.cs`

```csharp
namespace MissionClear.Api.Configuration;

public static class DashboardConstants
{
    public const double ConjunctionThresholdKm = 200.0;
    public const double AverageOrbitalVelocityKmS = 14.0;

    public const double IssAltitudeKm = 408.0;
    public const double LeoGenericAltitudeKm = 400.0;
    public const double SsoAltitudeKm = 500.0;

    public const string DestIss = "ISS";
    public const string DestLeoGeneric = "LEO_GENERIC";
    public const string DestSso = "SSO";

    public const int AltitudeBandLowMaxKm = 500;
    public const int AltitudeBandMidMaxKm = 1000;
    public const int AltitudeBandHighMaxKm = 2000;
}

public static class MissionScoring
{
    public const double MaxDeltaVKmS = 12.0;
    public const double EfficiencyWeight = 50.0;
    public const double SafetyWeight = 50.0;

    public static (double Efficiency, double Safety, int Total) Compute(double deltaVKmS, double riskScore)
    {
        var efficiency = Math.Max(0.0, 1.0 - (deltaVKmS / MaxDeltaVKmS)) * EfficiencyWeight;
        var safety = (1.0 - Math.Clamp(riskScore, 0.0, 1.0)) * SafetyWeight;
        var total = (int)Math.Clamp(Math.Round(efficiency + safety), 0, 100);
        return (efficiency, safety, total);
    }
}
```

Steps:
- [ ] Criar `MissionClear.Api/Configuration/DashboardConstants.cs`
- [ ] `dotnet build` — deve compilar
- [ ] Commit: `chore: add dashboard constants and mission scoring formula`

---

## Task 6.1 — MissionHistoryService (interface + tests RED)

**Files:**
- Create: `MissionClear.Api/Services/IMissionHistoryService.cs`
- Create: `MissionClear.Tests/Services/MissionHistoryServiceTests.cs`

### Interface

```csharp
using MissionClear.Api.Entities;
using MissionClear.Api.Models;

namespace MissionClear.Api.Services;

public interface IMissionHistoryService
{
    Task<PagedResponse<MissionHistoryDto>> GetMissionsAsync(
        string userId, int page, int limit, string? statusFilter, CancellationToken ct);

    Task<MissionDetailDto> GetMissionAsync(string userId, string missionId, CancellationToken ct);

    Task<MissionStatsDto> GetStatsAsync(string userId, CancellationToken ct);

    Task<MissionDetailDto> SaveMissionAsync(
        string userId,
        string sessionId,
        SessionStatus finalStatus,
        double riskScore,
        double deltaVKmS,
        int obstaclesEncountered,
        string obstaclesJson,
        string destination,
        DateTime departureTime,
        DateTime arrivalTime,
        CancellationToken ct);

    Task DeleteMissionAsync(string userId, string missionId, CancellationToken ct);
}
```

### Tests (RED) — `MissionClear.Tests/Services/MissionHistoryServiceTests.cs`

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public class MissionHistoryServiceTests : IAsyncLifetime
{
    private AppDbContext _db = null!;
    private MissionHistoryService _sut = null!;

    private const string UserA = "usr_alice";
    private const string UserB = "usr_bob";

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"missions-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options);
        _sut = new MissionHistoryService(_db, NullLogger());
        await SeedAsync();
    }

    public Task DisposeAsync() { _db.Dispose(); return Task.CompletedTask; }

    private static Microsoft.Extensions.Logging.ILogger<MissionHistoryService> NullLogger()
        => Microsoft.Extensions.Logging.Abstractions.NullLogger<MissionHistoryService>.Instance;

    private async Task SeedAsync()
    {
        var baseTime = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 25; i++)
        {
            _db.Missions.Add(new MissionEntity
            {
                Id = $"msn_seed_{i:D3}",
                UserId = UserA,
                Destination = i % 2 == 0 ? "ISS" : "LEO_GENERIC",
                DepartureTime = baseTime.AddHours(i),
                ArrivalTime = baseTime.AddHours(i + 6),
                Status = (i % 3) switch
                {
                    0 => SessionStatus.Success,
                    1 => SessionStatus.Failure,
                    _ => SessionStatus.Aborted
                },
                MissionScore = 50 + i,
                RiskScore = 0.1 + (i * 0.01),
                DeltaVKmS = 9.0 + (i * 0.05),
                ObstaclesEncountered = i,
                ObstaclesJson = "[]",
                ScoreBreakdownJson = "{}",
                CreatedAt = baseTime.AddHours(i)
            });
        }
        _db.Missions.Add(new MissionEntity
        {
            Id = "msn_bob_001",
            UserId = UserB,
            Destination = "SSO",
            DepartureTime = baseTime,
            ArrivalTime = baseTime.AddHours(6),
            Status = SessionStatus.Success,
            MissionScore = 90,
            RiskScore = 0.05,
            DeltaVKmS = 9.0,
            ObstaclesEncountered = 0,
            ObstaclesJson = "[]",
            ScoreBreakdownJson = "{}",
            CreatedAt = baseTime
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetMissionsAsync_ReturnsPaginatedDescOrdered()
    {
        var result = await _sut.GetMissionsAsync(UserA, page: 1, limit: 10, statusFilter: null, CancellationToken.None);

        result.Pagination.Total.Should().Be(25);
        result.Pagination.Page.Should().Be(1);
        result.Pagination.Limit.Should().Be(10);
        result.Data.Should().HaveCount(10);
        result.Data[0].Id.Should().Be("msn_seed_024");
        result.Data[9].Id.Should().Be("msn_seed_015");
    }

    [Fact]
    public async Task GetMissionsAsync_Page2_ReturnsCorrectSlice()
    {
        var result = await _sut.GetMissionsAsync(UserA, page: 2, limit: 10, statusFilter: null, CancellationToken.None);

        result.Data.Should().HaveCount(10);
        result.Data[0].Id.Should().Be("msn_seed_014");
        result.Data[9].Id.Should().Be("msn_seed_005");
    }

    [Fact]
    public async Task GetMissionsAsync_FilterSuccess_ReturnsOnlySuccess()
    {
        var result = await _sut.GetMissionsAsync(UserA, 1, 50, "success", CancellationToken.None);

        result.Data.Should().OnlyContain(m => m.Status == "success");
        result.Pagination.Total.Should().Be(result.Data.Count);
    }

    [Fact]
    public async Task GetMissionAsync_ForeignUser_Throws403()
    {
        var act = () => _sut.GetMissionAsync(UserA, "msn_bob_001", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("FORBIDDEN");
        ex.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task GetMissionAsync_NotFound_Throws404()
    {
        var act = () => _sut.GetMissionAsync(UserA, "msn_does_not_exist", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("MISSION_NOT_FOUND");
        ex.Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetStatsAsync_AggregatesCorrectly()
    {
        var stats = await _sut.GetStatsAsync(UserA, CancellationToken.None);

        stats.TotalMissions.Should().Be(25);
        stats.SuccessfulMissions.Should().Be(9);
        stats.FailedMissions.Should().Be(8);
        stats.AbortedMissions.Should().Be(8);
        stats.SuccessRate.Should().BeApproximately(0.36, 0.01);
        stats.BestScore.Should().Be(74);
        stats.WorstScore.Should().Be(50);
        stats.AverageScore.Should().BeApproximately(62.0, 0.01);
        stats.FavoriteDestination.Should().Be("ISS");
        stats.ByDestination.Iss.Should().Be(13);
        stats.ByDestination.LeoGeneric.Should().Be(12);
        stats.ByDestination.Sso.Should().Be(0);
    }

    [Fact]
    public async Task GetStatsAsync_NoMissions_ReturnsZeroed()
    {
        var stats = await _sut.GetStatsAsync("usr_empty", CancellationToken.None);

        stats.TotalMissions.Should().Be(0);
        stats.SuccessRate.Should().Be(0.0);
        stats.BestScore.Should().Be(0);
        stats.WorstScore.Should().Be(0);
        stats.AverageScore.Should().Be(0.0);
        stats.FavoriteDestination.Should().BeNull();
    }

    [Fact]
    public async Task SaveMissionAsync_PersistsAndComputesScore()
    {
        var dep = DateTime.UtcNow;
        var arr = dep.AddHours(6);

        var result = await _sut.SaveMissionAsync(
            UserA, "ses_abc", SessionStatus.Success,
            riskScore: 0.1, deltaVKmS: 9.4, obstaclesEncountered: 2,
            obstaclesJson: "[]", destination: "ISS",
            departureTime: dep, arrivalTime: arr,
            CancellationToken.None);

        result.Id.Should().StartWith("msn_");
        // efficiency = (1 - 9.4/12)*50 ≈ 10.83; safety = (1 - 0.1)*50 = 45; total = 56
        result.MissionScore.Should().Be(56);
        result.ScoreBreakdown.Total.Should().Be(56);
        result.ScoreBreakdown.SafetyScore.Should().BeApproximately(45.0, 0.01);

        var persisted = await _db.Missions.FindAsync(result.Id);
        persisted.Should().NotBeNull();
        persisted!.UserId.Should().Be(UserA);
    }

    [Fact]
    public async Task DeleteMissionAsync_ForeignUser_Throws403()
    {
        var act = () => _sut.DeleteMissionAsync(UserA, "msn_bob_001", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task DeleteMissionAsync_RemovesFromDatabase()
    {
        await _sut.DeleteMissionAsync(UserA, "msn_seed_000", CancellationToken.None);

        var exists = await _db.Missions.AnyAsync(m => m.Id == "msn_seed_000");
        exists.Should().BeFalse();
    }
}
```

Steps:
- [ ] Criar interface `IMissionHistoryService`
- [ ] Criar `MissionHistoryServiceTests.cs` com todos os 10 testes
- [ ] `dotnet test` — deve falhar na compilação. RED confirmado.

---

## Task 6.2 — MissionHistoryService (implementation GREEN)

**File:** `MissionClear.Api/Services/MissionHistoryService.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MissionClear.Api.Configuration;
using MissionClear.Api.Data;
using MissionClear.Api.Entities;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models;
using System.Text.Json;

namespace MissionClear.Api.Services;

public sealed class MissionHistoryService : IMissionHistoryService
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    private readonly AppDbContext _db;
    private readonly ILogger<MissionHistoryService> _log;

    public MissionHistoryService(AppDbContext db, ILogger<MissionHistoryService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<PagedResponse<MissionHistoryDto>> GetMissionsAsync(
        string userId, int page, int limit, string? statusFilter, CancellationToken ct)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedLimit = limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);

        var query = _db.Missions.AsNoTracking().Where(m => m.UserId == userId);

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            var status = ParseStatus(statusFilter);
            query = query.Where(m => m.Status == status);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((normalizedPage - 1) * normalizedLimit)
            .Take(normalizedLimit)
            .Select(m => MapToHistoryDto(m))
            .ToListAsync(ct);

        return new PagedResponse<MissionHistoryDto>
        {
            Data = items,
            Pagination = PaginationDto.From(normalizedPage, normalizedLimit, total)
        };
    }

    public async Task<MissionDetailDto> GetMissionAsync(string userId, string missionId, CancellationToken ct)
    {
        var mission = await _db.Missions.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == missionId, ct);

        if (mission is null)
            throw new DomainException("MISSION_NOT_FOUND", $"Mission {missionId} not found.", 404);

        if (mission.UserId != userId)
            throw new DomainException("FORBIDDEN", "You do not own this mission.", 403);

        return MapToDetailDto(mission);
    }

    public async Task<MissionStatsDto> GetStatsAsync(string userId, CancellationToken ct)
    {
        var missions = await _db.Missions.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.Status, m.MissionScore, m.DeltaVKmS, m.Destination })
            .ToListAsync(ct);

        if (missions.Count == 0)
        {
            return new MissionStatsDto
            {
                TotalMissions = 0,
                SuccessfulMissions = 0,
                FailedMissions = 0,
                AbortedMissions = 0,
                SuccessRate = 0.0,
                BestScore = 0,
                WorstScore = 0,
                AverageScore = 0.0,
                TotalDeltaV = 0.0,
                FavoriteDestination = null,
                ByDestination = new MissionByDestinationDto { Iss = 0, LeoGeneric = 0, Sso = 0 }
            };
        }

        var success = missions.Count(m => m.Status == SessionStatus.Success);
        var failed = missions.Count(m => m.Status == SessionStatus.Failure);
        var aborted = missions.Count(m => m.Status == SessionStatus.Aborted);

        var favorite = missions
            .GroupBy(m => m.Destination)
            .OrderByDescending(g => g.Count())
            .First().Key;

        return new MissionStatsDto
        {
            TotalMissions = missions.Count,
            SuccessfulMissions = success,
            FailedMissions = failed,
            AbortedMissions = aborted,
            SuccessRate = Math.Round((double)success / missions.Count, 4),
            BestScore = missions.Max(m => m.MissionScore),
            WorstScore = missions.Min(m => m.MissionScore),
            AverageScore = Math.Round(missions.Average(m => (double)m.MissionScore), 2),
            TotalDeltaV = Math.Round(missions.Sum(m => m.DeltaVKmS), 2),
            FavoriteDestination = favorite,
            ByDestination = new MissionByDestinationDto
            {
                Iss = missions.Count(m => m.Destination == DashboardConstants.DestIss),
                LeoGeneric = missions.Count(m => m.Destination == DashboardConstants.DestLeoGeneric),
                Sso = missions.Count(m => m.Destination == DashboardConstants.DestSso)
            }
        };
    }

    public async Task<MissionDetailDto> SaveMissionAsync(
        string userId, string sessionId, SessionStatus finalStatus,
        double riskScore, double deltaVKmS, int obstaclesEncountered,
        string obstaclesJson, string destination,
        DateTime departureTime, DateTime arrivalTime,
        CancellationToken ct)
    {
        var (efficiency, safety, total) = MissionScoring.Compute(deltaVKmS, riskScore);

        var breakdown = new ScoreBreakdownDto
        {
            EfficiencyScore = Math.Round(efficiency, 2),
            SafetyScore = Math.Round(safety, 2),
            Total = total
        };

        var entity = new MissionEntity
        {
            Id = $"msn_{Guid.NewGuid():N}",
            UserId = userId,
            Destination = destination,
            DepartureTime = DateTime.SpecifyKind(departureTime, DateTimeKind.Utc),
            ArrivalTime = DateTime.SpecifyKind(arrivalTime, DateTimeKind.Utc),
            Status = finalStatus,
            MissionScore = total,
            RiskScore = Math.Round(riskScore, 4),
            DeltaVKmS = Math.Round(deltaVKmS, 4),
            ObstaclesEncountered = obstaclesEncountered,
            ObstaclesJson = obstaclesJson ?? "[]",
            ScoreBreakdownJson = JsonSerializer.Serialize(breakdown),
            CreatedAt = DateTime.UtcNow
        };

        _db.Missions.Add(entity);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Mission {MissionId} saved for user {UserId} (session {SessionId})",
            entity.Id, userId, sessionId);

        return MapToDetailDto(entity);
    }

    public async Task DeleteMissionAsync(string userId, string missionId, CancellationToken ct)
    {
        var mission = await _db.Missions.FirstOrDefaultAsync(m => m.Id == missionId, ct);
        if (mission is null)
            throw new DomainException("MISSION_NOT_FOUND", $"Mission {missionId} not found.", 404);
        if (mission.UserId != userId)
            throw new DomainException("FORBIDDEN", "You do not own this mission.", 403);

        _db.Missions.Remove(mission);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Mission {MissionId} deleted by user {UserId}", missionId, userId);
    }

    private static SessionStatus ParseStatus(string raw) => raw.ToLowerInvariant() switch
    {
        "success" => SessionStatus.Success,
        "failure" => SessionStatus.Failure,
        "aborted" => SessionStatus.Aborted,
        _ => throw new DomainException("INVALID_STATUS_FILTER", $"Unknown status '{raw}'", 400)
    };

    private static MissionHistoryDto MapToHistoryDto(MissionEntity m) => new()
    {
        Id = m.Id,
        Destination = m.Destination,
        DepartureTime = m.DepartureTime,
        ArrivalTime = m.ArrivalTime,
        Status = m.Status.ToString().ToLowerInvariant(),
        MissionScore = m.MissionScore,
        RiskScore = m.RiskScore,
        DeltaVKmS = m.DeltaVKmS,
        ObstaclesEncountered = m.ObstaclesEncountered,
        CreatedAt = m.CreatedAt
    };

    private static MissionDetailDto MapToDetailDto(MissionEntity m)
    {
        var obstacles = SafeDeserialize<List<ConjunctionDto>>(m.ObstaclesJson) ?? new List<ConjunctionDto>();
        var breakdown = SafeDeserialize<ScoreBreakdownDto>(m.ScoreBreakdownJson)
            ?? new ScoreBreakdownDto { EfficiencyScore = 0, SafetyScore = 0, Total = m.MissionScore };

        return new MissionDetailDto
        {
            Id = m.Id,
            Destination = m.Destination,
            DepartureTime = m.DepartureTime,
            ArrivalTime = m.ArrivalTime,
            Status = m.Status.ToString().ToLowerInvariant(),
            MissionScore = m.MissionScore,
            RiskScore = m.RiskScore,
            DeltaVKmS = m.DeltaVKmS,
            ObstaclesEncountered = m.ObstaclesEncountered,
            CreatedAt = m.CreatedAt,
            Obstacles = obstacles,
            ScoreBreakdown = breakdown
        };
    }

    private static T? SafeDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return null; }
    }
}
```

Steps:
- [ ] Criar `MissionHistoryService.cs`
- [ ] Rodar `dotnet test --filter MissionHistoryServiceTests` — todos 10 devem passar (GREEN)
- [ ] Commit: `feat: add MissionHistoryService with pagination, stats, scoring and ownership checks`

---

## Task 6.3 — DashboardService (interface + tests RED)

**Files:**
- Create: `MissionClear.Api/Services/IDashboardService.cs`
- Create: `MissionClear.Tests/Services/DashboardServiceTests.cs`

### Interface

```csharp
using MissionClear.Api.Models;

namespace MissionClear.Api.Services;

public interface IDashboardService
{
    DashboardSummaryResponse GetSummary(
        string? userId,
        UserResponse? userDto,
        IReadOnlyList<OrbitalObject> debris);

    DashboardAlertsResponse GetAlerts(
        IReadOnlyList<OrbitalObject> debris,
        int windowHours = 24);
}
```

### Tests (RED) — `MissionClear.Tests/Services/DashboardServiceTests.cs`

```csharp
using FluentAssertions;
using MissionClear.Api.Configuration;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public class DashboardServiceTests
{
    private readonly DashboardService _sut = new();

    private static OrbitalObject Obj(string id, string type, double alt, double lat = 0, double lon = 0)
        => new(
            NoradCatId: id,
            Name: $"OBJ-{id}",
            Type: type,
            LatitudeDeg: lat,
            LongitudeDeg: lon,
            AltitudeKm: alt,
            VelocityKmS: 7.7,
            Source: "celestrak",
            PropagatedAt: DateTime.UtcNow);

    [Fact]
    public void GetSummary_CountsByType()
    {
        var debris = new List<OrbitalObject>
        {
            Obj("1", "debris", 450),
            Obj("2", "debris", 600),
            Obj("3", "satellite", 800),
            Obj("4", "rocket_body", 1200)
        };

        var summary = _sut.GetSummary(userId: null, userDto: null, debris);

        summary.Orbital.TotalTrackedObjects.Should().Be(4);
        summary.Orbital.ByType.Debris.Should().Be(2);
        summary.Orbital.ByType.Satellite.Should().Be(1);
        summary.Orbital.ByType.RocketBody.Should().Be(1);
    }

    [Fact]
    public void GetSummary_CountsByAltitudeBand()
    {
        var debris = new List<OrbitalObject>
        {
            Obj("1", "debris", 250),    // low
            Obj("2", "debris", 499),    // low
            Obj("3", "debris", 500),    // mid
            Obj("4", "debris", 999),    // mid
            Obj("5", "debris", 1000),   // high
            Obj("6", "debris", 1999),   // high
            Obj("7", "debris", 2500)    // out of range
        };

        var summary = _sut.GetSummary(null, null, debris);

        summary.Orbital.ByAltitudeBand.Low.Should().Be(2);
        summary.Orbital.ByAltitudeBand.Mid.Should().Be(2);
        summary.Orbital.ByAltitudeBand.High.Should().Be(2);
    }

    [Fact]
    public void GetSummary_NullUser_ReturnsNullUserField()
    {
        var summary = _sut.GetSummary(userId: null, userDto: null, Array.Empty<OrbitalObject>());
        summary.User.Should().BeNull();
    }

    [Fact]
    public void GetSummary_AuthenticatedUser_ReturnsUserDto()
    {
        var user = new UserResponse { Id = "usr_1", Email = "a@b.com", DisplayName = "Alice" };
        var summary = _sut.GetSummary("usr_1", user, Array.Empty<OrbitalObject>());
        summary.User.Should().NotBeNull();
        summary.User!.Id.Should().Be("usr_1");
    }

    [Fact]
    public void GetAlerts_NoCloseDebris_ReturnsEmpty()
    {
        var debris = new List<OrbitalObject>
        {
            Obj("far", "debris", 1500)
        };

        var alerts = _sut.GetAlerts(debris);

        alerts.Alerts.Should().BeEmpty();
    }

    [Fact]
    public void GetAlerts_DebrisCloseToIss_EmitsAlert()
    {
        var debris = new List<OrbitalObject>
        {
            Obj("close-iss", "debris", 410, lat: 0, lon: 0) // ~2 km from ISS reference
        };

        var alerts = _sut.GetAlerts(debris);

        alerts.Alerts.Should().NotBeEmpty();
        alerts.Alerts.Should().Contain(a =>
            a.DebrisId == "close-iss" && a.Destination == DashboardConstants.DestIss);
    }

    [Fact]
    public void GetAlerts_SortedByClosestApproachAsc()
    {
        var debris = new List<OrbitalObject>
        {
            Obj("d100", "debris", 508, 0, 0),
            Obj("d050", "debris", 503, 0, 0),
            Obj("d150", "debris", 410, 0, 0)
        };

        var alerts = _sut.GetAlerts(debris);

        alerts.Alerts.Should().HaveCountGreaterThan(1);
        alerts.Alerts.Should().BeInAscendingOrder(a => a.ClosestApproachKm);
    }
}
```

Steps:
- [ ] Criar `IDashboardService.cs`
- [ ] Criar `DashboardServiceTests.cs` com os 7 testes
- [ ] `dotnet test --filter DashboardServiceTests` → falha de compilação (RED)

---

## Task 6.4 — DashboardService (implementation GREEN)

**File:** `MissionClear.Api/Services/DashboardService.cs`

```csharp
using MissionClear.Api.Configuration;
using MissionClear.Api.Models;

namespace MissionClear.Api.Services;

public sealed class DashboardService : IDashboardService
{
    private static readonly (string Name, double AltitudeKm)[] Destinations = new[]
    {
        (DashboardConstants.DestIss,         DashboardConstants.IssAltitudeKm),
        (DashboardConstants.DestLeoGeneric,  DashboardConstants.LeoGenericAltitudeKm),
        (DashboardConstants.DestSso,         DashboardConstants.SsoAltitudeKm)
    };

    public DashboardSummaryResponse GetSummary(
        string? userId,
        UserResponse? userDto,
        IReadOnlyList<OrbitalObject> debris)
    {
        ArgumentNullException.ThrowIfNull(debris);

        var byType = new OrbitalByTypeDto
        {
            Debris = debris.Count(o => o.Type == "debris"),
            Satellite = debris.Count(o => o.Type == "satellite"),
            RocketBody = debris.Count(o => o.Type == "rocket_body")
        };

        var byAltitude = new OrbitalByAltitudeBandDto
        {
            Low = debris.Count(o =>
                o.AltitudeKm >= 200 && o.AltitudeKm < DashboardConstants.AltitudeBandLowMaxKm),
            Mid = debris.Count(o =>
                o.AltitudeKm >= DashboardConstants.AltitudeBandLowMaxKm
                && o.AltitudeKm < DashboardConstants.AltitudeBandMidMaxKm),
            High = debris.Count(o =>
                o.AltitudeKm >= DashboardConstants.AltitudeBandMidMaxKm
                && o.AltitudeKm < DashboardConstants.AltitudeBandHighMaxKm)
        };

        var activeAlerts = CountActiveConjunctions(debris);

        return new DashboardSummaryResponse
        {
            Orbital = new DashboardOrbitalStatsDto
            {
                TotalTrackedObjects = debris.Count,
                ByType = byType,
                ByAltitudeBand = byAltitude,
                ActiveConjunctionAlerts = activeAlerts,
                LastUpdated = ResolveLastUpdated(debris)
            },
            User = userId is null ? null : userDto
        };
    }

    public DashboardAlertsResponse GetAlerts(IReadOnlyList<OrbitalObject> debris, int windowHours = 24)
    {
        ArgumentNullException.ThrowIfNull(debris);

        var now = DateTime.UtcNow;
        var alerts = new List<DashboardAlertDto>();

        foreach (var (destName, destAltitude) in Destinations)
        {
            foreach (var obj in debris)
            {
                var distance = ApproximateDistanceKm(obj, destAltitude);
                if (distance > DashboardConstants.ConjunctionThresholdKm) continue;

                var minutesUntil = (distance / DashboardConstants.AverageOrbitalVelocityKmS) / 60.0;

                alerts.Add(new DashboardAlertDto
                {
                    DebrisId = obj.NoradCatId,
                    DebrisName = obj.Name,
                    Destination = destName,
                    ClosestApproachKm = Math.Round(distance, 2),
                    MinutesUntilConjunction = Math.Round(minutesUntil, 2),
                    DetectedAt = now
                });
            }
        }

        return new DashboardAlertsResponse
        {
            Alerts = alerts.OrderBy(a => a.ClosestApproachKm).ToList(),
            WindowHours = windowHours,
            GeneratedAt = now
        };
    }

    private static int CountActiveConjunctions(IReadOnlyList<OrbitalObject> debris)
    {
        var count = 0;
        foreach (var obj in debris)
        {
            foreach (var (_, alt) in Destinations)
            {
                if (ApproximateDistanceKm(obj, alt) <= DashboardConstants.ConjunctionThresholdKm)
                {
                    count++;
                    break; // count each object once
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Approximate radial distance from reference point (lat 0, lon 0, altitude=destAltitude).
    /// MVP dashboard heuristic only — not a full Haversine calculation.
    /// </summary>
    private static double ApproximateDistanceKm(OrbitalObject obj, double destAltitudeKm)
    {
        const double earthRadiusKm = 6371.0;

        var r1 = earthRadiusKm + obj.AltitudeKm;
        var r2 = earthRadiusKm + destAltitudeKm;

        var lat1 = DegToRad(obj.LatitudeDeg);
        var lon1 = DegToRad(obj.LongitudeDeg);
        var x1 = r1 * Math.Cos(lat1) * Math.Cos(lon1);
        var y1 = r1 * Math.Cos(lat1) * Math.Sin(lon1);
        var z1 = r1 * Math.Sin(lat1);

        var x2 = r2; // reference at lat=0, lon=0
        var y2 = 0.0;
        var z2 = 0.0;

        var dx = x1 - x2;
        var dy = y1 - y2;
        var dz = z1 - z2;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;

    private static DateTime ResolveLastUpdated(IReadOnlyList<OrbitalObject> debris)
    {
        if (debris.Count == 0) return DateTime.UtcNow;
        return debris.Max(o => o.PropagatedAt);
    }
}
```

Steps:
- [ ] Criar `DashboardService.cs`
- [ ] Rodar `dotnet test --filter DashboardServiceTests` — todos 7 devem passar (GREEN)
- [ ] Commit: `feat: add DashboardService with orbital stats and proximity alerts`

---

## Task 6.5 — DI registration

Adicionar em `MissionClear.Api/Program.cs`:

```csharp
builder.Services.AddScoped<IMissionHistoryService, MissionHistoryService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
```

Steps:
- [ ] Adicionar registros em `Program.cs`
- [ ] `dotnet build` deve passar
- [ ] `dotnet test` — toda a suíte deve continuar verde
- [ ] Commit: `chore: register mission history and dashboard services in DI`

---

## Testing Strategy

| Camada | Cobertura |
|--------|-----------|
| Unit (MissionHistory) | 10 testes — paginação, filtros, 403/404, agregados, persistência, delete |
| Unit (Dashboard) | 7 testes — contadores por tipo/altitude, user null/auth, alertas vazio/positivo/sort |
| Integration | Coberta indiretamente via `AppDbContext` InMemory; integração HTTP validada em plan-07 |
| Coverage target | >= 80% nos dois Services |

---

## Risks & Mitigations

| Risco | Impacto | Mitigação |
|-------|---------|-----------|
| EF Core InMemory difere de SQLite (ex: `OrderByDescending`) | Smoke test em plan-07 com SQLite real antes do merge |
| Fórmula de distância simplificada | MVP — documentar como heurística; refinar pós-entrega |
| `OrbitalCache` vazio na primeira request | DashboardService trabalha com `IReadOnlyList<>` injetado pelo controller |
| Status filter recebe valor desconhecido | `ParseStatus` lança `DomainException("INVALID_STATUS_FILTER", 400)` |

---

## Success Criteria

- [ ] `IMissionHistoryService` e `MissionHistoryService` implementados
- [ ] `IDashboardService` e `DashboardService` implementados
- [ ] Todos os 17 testes (10 + 7) passam em verde
- [ ] Cobertura >= 80% em ambos os Services
- [ ] `DomainException` com códigos `FORBIDDEN` (403) e `MISSION_NOT_FOUND` (404)
- [ ] Score calculado por `efficiency + safety`, clamp [0,100]
- [ ] Paginação: `page >= 1`, limit default 20, max 100
- [ ] Alertas ordenados por `ClosestApproachKm` ascendente
- [ ] DI registrado como `Scoped`
- [ ] Nenhum `catch {}` vazio, `Console.WriteLine` ou secret hardcoded
- [ ] Arquivos < 300 linhas
- [ ] 4 commits atômicos

---

## Arquivos relevantes

- `MissionClear.Api/Configuration/DashboardConstants.cs`
- `MissionClear.Api/Services/IMissionHistoryService.cs`
- `MissionClear.Api/Services/MissionHistoryService.cs`
- `MissionClear.Api/Services/IDashboardService.cs`
- `MissionClear.Api/Services/DashboardService.cs`
- `MissionClear.Tests/Services/MissionHistoryServiceTests.cs`
- `MissionClear.Tests/Services/DashboardServiceTests.cs`
- `MissionClear.Api/Program.cs` (edit)
