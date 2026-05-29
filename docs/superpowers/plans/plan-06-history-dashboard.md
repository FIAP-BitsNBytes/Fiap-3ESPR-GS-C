# Plan 06 — Mission History + Dashboard Services

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans
>
> **FONTE DA VERDADE** para Phase 6. Substitui o plan-06 original e o reboot/phase-06-history-dashboard.md.

**Execution order:** After plan-01 (DB + Repositories), plan-02 (DTOs), plan-03 (OrbitalCache), plan-04 (Auth), plan-05 (Simulation).
**Estimated time:** 90 minutes.
**Goal:** Implementar `MissionHistoryService` (usa `IMissionRepository`) e `DashboardService` (usa `IOrbitalCache` + `IMissionRepository`).

**REGRA ARQUITETURAL INVIOLÁVEL:** Services **nunca** injetam `AppDbContext` diretamente. Toda persistência passa por `IMissionRepository`.

---

## Dependências obrigatórias

| Fase | O que fornece |
|------|---------------|
| plan-01 | `MissionEntity`, `IMissionRepository`, `MissionPageResult`, `MissionStatsProjection` |
| plan-02 | `PagedResponse<T>`, `PaginationDto`, `MissionSummaryDto`, `MissionDetailResponse`, `MissionStatsResponse`, `ScoreBreakdownDto`, `DashboardSummaryResponse`, `AlertsResponse`, `AlertDto`, `ObstacleDto`, `OrbitalSummaryDto`, `ByTypeDto`, `ByAltitudeBandDto`, `UserDashboardDto` |
| plan-03 | `IOrbitalCache`, `OrbitalObject`, `KnownDestinations` |
| plan-05 | `ConjunctionDetector`, `IConjunctionDetector`, `ConjunctionResult`, `RiskLevel` |

**Unlocks:** plan-07-api-controllers.md (endpoints `/api/missions/*` e `/api/dashboard/*`)

---

## Visão Geral

Dois serviços independentes, ambos `Scoped`, totalmente testáveis com Moq.

1. **MissionHistoryService** — CRUD de `MissionEntity` filtradas por `userId`. Aplica regras de autorização (403 quando dono diferente). Score calculado internamente via `MissionScoring.Compute` — nunca recebido do cliente.
2. **DashboardService** — Agrega `IOrbitalCache` (snapshot de objetos orbitais) e `IMissionRepository` para produzir contadores + alertas de proximidade.

Regras invioláveis:
- Usuário **só** acessa suas próprias missões — toda leitura/escrita compara `UserId`.
- Score é determinístico (`efficiency + safety`, clamp [0,100]), nunca recebido do cliente.
- Hard delete só executa após verificação de propriedade.
- IDs de missão em respostas: prefixo `msn_` + `{guid:N}` (ex: `msn_a1b2c3d4...`).
- `destination_display`: `KnownDestinations.FindById(destination)?.DisplayName ?? destination`.

---

## Task 6.0 — Interfaces de Serviço

**Files:**
- Create: `MissionClear.Api/Services/Interfaces/IMissionHistoryService.cs`
- Create: `MissionClear.Api/Services/Interfaces/IDashboardService.cs`

### IMissionHistoryService.cs

```csharp
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Dtos.History;

namespace MissionClear.Api.Services.Interfaces;

public interface IMissionHistoryService
{
    /// <summary>
    /// Lista missões do usuário com paginação, filtros e sort.
    /// Mapeado para GET /api/missions
    /// </summary>
    Task<PagedResponse<MissionSummaryDto>> GetMissionsAsync(
        Guid userId,
        int page,
        int limit,
        string? status,
        string? destination,
        string sort,
        CancellationToken ct = default);

    /// <summary>
    /// Detalhe de uma missão. Lança 404 se não encontrada, 403 se não pertence ao userId.
    /// Mapeado para GET /api/missions/{id}
    /// </summary>
    Task<MissionDetailResponse> GetMissionDetailAsync(
        Guid id,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Estatísticas agregadas do usuário.
    /// Mapeado para GET /api/missions/stats
    /// </summary>
    Task<MissionStatsResponse> GetStatsAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Remove missão. Lança 404 se não encontrada, 403 se não pertence ao userId.
    /// Mapeado para DELETE /api/missions/{id}
    /// </summary>
    Task DeleteMissionAsync(
        Guid id,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Persiste missão finalizada e retorna o DTO de sumário.
    /// Chamado internamente por MissionSimulationService ao completar sessão com save_to_history = true.
    /// </summary>
    Task<MissionSummaryDto> SaveMissionAsync(
        Guid userId,
        string sessionId,
        string status,
        double riskScore,
        double deltaV,
        int score,
        int obstacles,
        DateTime departure,
        DateTime arrival,
        string destination,
        IReadOnlyList<object> obstaclesData,
        CancellationToken ct = default);
}
```

### IDashboardService.cs

```csharp
using MissionClear.Api.Dtos.Dashboard;

namespace MissionClear.Api.Services.Interfaces;

public interface IDashboardService
{
    /// <summary>
    /// Visão geral orbital. Se userId fornecido, inclui dados do usuário.
    /// displayName é resolvido pelo Controller a partir das Claims JWT.
    /// Mapeado para GET /api/dashboard/summary
    /// </summary>
    Task<DashboardSummaryResponse> GetSummaryAsync(
        Guid? userId,
        string? displayName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Alertas de conjunção filtrados por windowHours e minRisk.
    /// Mapeado para GET /api/dashboard/alerts
    /// </summary>
    Task<AlertsResponse> GetAlertsAsync(
        int windowHours,
        string minRisk,
        CancellationToken ct = default);
}
```

Steps:
- [ ] Criar `MissionClear.Api/Services/Interfaces/IMissionHistoryService.cs`
- [ ] Criar `MissionClear.Api/Services/Interfaces/IDashboardService.cs`
- [ ] `dotnet build MissionClear.Api/MissionClear.Api.csproj` — deve compilar

---

## Task 6.1 — MissionHistoryService (tests RED)

**File:** `MissionClear.Tests/Services/MissionHistoryServiceTests.cs`

> Usa **Moq** para mockar `IMissionRepository`. Sem `AppDbContext` nos testes.

```csharp
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
            .Setup(r => r.FindByUserIdAsync(userId, 1, 20, null, null, "created_at_desc", default))
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
            .Setup(r => r.FindByUserIdAsync(userId, 1, 5, null, null, "created_at_desc", default))
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
            .Setup(r => r.FindByUserIdAsync(userId, 1, 20, null, null, "created_at_desc", default))
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
            .Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), default))
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
            .Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), default))
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
            .Setup(r => r.FindByIdAsync(missionId, default))
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
            .Setup(r => r.GetStatsByUserIdAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(
                Total: 10, Successful: 7, Failed: 2, Aborted: 1,
                BestScore: 97, WorstScore: 23,
                AverageScore: 80.0, TotalDeltaV: 94.0,
                TotalObstacles: 15,
                FavoriteDestination: "ISS",
                MissionsByDestination: new() { { "ISS", 7 }, { "SSO", 3 } }));

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
            .Setup(r => r.GetStatsByUserIdAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(
                10, 7, 2, 1, 97, 23, 80.0, 94.0, 15, "ISS",
                new() { { "ISS", 7 }, { "SSO", 3 } }));

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
            .Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), default))
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

        _missionRepo
            .Setup(r => r.FindByIdAsync(missionId, default))
            .ReturnsAsync(new MissionEntity
            {
                Id = missionId, UserId = userId, Destination = "ISS",
                Status = "success", DepartureTime = DateTime.UtcNow,
                ArrivalTime = DateTime.UtcNow.AddHours(6)
            });
        _missionRepo
            .Setup(r => r.DeleteAsync(missionId, default))
            .Returns(Task.CompletedTask);

        await _service.DeleteMissionAsync(missionId, userId, default);

        _missionRepo.Verify(r => r.DeleteAsync(missionId, default), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SaveMissionAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveMissionAsync_CreatesEntityAndReturnsSummary()
    {
        var userId = Guid.NewGuid();
        var departure = DateTime.UtcNow;
        var arrival = departure.AddHours(6);

        _missionRepo
            .Setup(r => r.CreateAsync(It.IsAny<MissionEntity>(), default))
            .ReturnsAsync((MissionEntity entity, CancellationToken _) => entity);

        var result = await _service.SaveMissionAsync(
            userId, "ses_abc", "success",
            riskScore: 0.1, deltaV: 9.4, score: 56, obstacles: 2,
            departure, arrival, "ISS", [], default);

        result.Id.Should().StartWith("msn_");
        result.Destination.Should().Be("ISS");
        result.DestinationDisplay.Should().Be("Estação Espacial Internacional");
        result.Status.Should().Be("success");
        _missionRepo.Verify(r => r.CreateAsync(
            It.Is<MissionEntity>(e => e.UserId == userId && e.Destination == "ISS"),
            default), Times.Once);
    }
}
```

Steps:
- [ ] Criar `MissionClear.Tests/Services/MissionHistoryServiceTests.cs` com todos os 10 testes
- [ ] `dotnet build MissionClear.Tests/MissionClear.Tests.csproj` — deve falhar (RED confirmado — `MissionHistoryService` não existe ainda)

---

## Task 6.2 — MissionHistoryService (implementation GREEN)

**File:** `MissionClear.Api/Services/MissionHistoryService.cs`

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
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // ── GetMissionsAsync ──────────────────────────────────────────────────────

    public async Task<PagedResponse<MissionSummaryDto>> GetMissionsAsync(
        Guid userId, int page, int limit,
        string? status, string? destination, string sort,
        CancellationToken ct)
    {
        var result = await missionRepo.FindByUserIdAsync(userId, page, limit, status, destination, sort, ct);
        var totalPages = limit > 0 ? (int)Math.Ceiling((double)result.Total / limit) : 1;
        var dtos = result.Items.Select(ToSummaryDto).ToList();
        return new PagedResponse<MissionSummaryDto>(dtos, new PaginationDto(page, limit, result.Total, totalPages));
    }

    // ── GetMissionDetailAsync ─────────────────────────────────────────────────

    public async Task<MissionDetailResponse> GetMissionDetailAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var mission = await missionRepo.FindByIdAsync(id, ct)
            ?? throw new DomainException("MISSION_NOT_FOUND", "Mission not found.", 404);

        if (mission.UserId != userId)
            throw new DomainException("FORBIDDEN", "Access denied.", 403);

        return ToDetailResponse(mission);
    }

    // ── GetStatsAsync ─────────────────────────────────────────────────────────

    public async Task<MissionStatsResponse> GetStatsAsync(Guid userId, CancellationToken ct)
    {
        var stats = await missionRepo.GetStatsByUserIdAsync(userId, ct);
        var successRate = stats.Total == 0
            ? 0.0
            : Math.Round((double)stats.Successful / stats.Total, 2);

        return new MissionStatsResponse(
            TotalMissions:             stats.Total,
            SuccessfulMissions:        stats.Successful,
            FailedMissions:            stats.Failed,
            AbortedMissions:           stats.Aborted,
            SuccessRate:               successRate,
            BestScore:                 stats.BestScore,
            WorstScore:                stats.WorstScore,
            AverageScore:              (int)Math.Round(stats.AverageScore),
            TotalDeltaVKmS:            Math.Round(stats.TotalDeltaV, 2),
            TotalObstaclesEncountered: stats.TotalObstacles,
            FavoriteDestination:       stats.FavoriteDestination,
            MissionsByDestination:     stats.MissionsByDestination);
    }

    // ── DeleteMissionAsync ────────────────────────────────────────────────────

    public async Task DeleteMissionAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var mission = await missionRepo.FindByIdAsync(id, ct)
            ?? throw new DomainException("MISSION_NOT_FOUND", "Mission not found.", 404);

        if (mission.UserId != userId)
            throw new DomainException("FORBIDDEN", "Access denied.", 403);

        await missionRepo.DeleteAsync(id, ct);
    }

    // ── SaveMissionAsync ──────────────────────────────────────────────────────

    public async Task<MissionSummaryDto> SaveMissionAsync(
        Guid userId, string sessionId, string status,
        double riskScore, double deltaV, int score, int obstacles,
        DateTime departure, DateTime arrival,
        string destination, IReadOnlyList<object> obstaclesData,
        CancellationToken ct)
    {
        var entity = new MissionEntity
        {
            UserId               = userId,
            Destination          = destination,
            Status               = status,
            MissionScore         = score,
            RiskScore            = riskScore,
            DeltaVKmS            = deltaV,
            ObstaclesEncountered = obstacles,
            DepartureTime        = DateTime.SpecifyKind(departure, DateTimeKind.Utc),
            ArrivalTime          = DateTime.SpecifyKind(arrival, DateTimeKind.Utc),
            ObstaclesJson        = JsonSerializer.Serialize(obstaclesData),
            CreatedAt            = DateTime.UtcNow
        };

        await missionRepo.CreateAsync(entity, ct);
        return ToSummaryDto(entity);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static MissionSummaryDto ToSummaryDto(MissionEntity m)
    {
        var dest = KnownDestinations.FindById(m.Destination);
        return new MissionSummaryDto(
            Id:                   $"msn_{m.Id:N}",
            Destination:          m.Destination,
            DestinationDisplay:   dest?.DisplayName ?? m.Destination,
            Status:               m.Status,
            MissionScore:         m.MissionScore,
            RiskScore:            m.RiskScore,
            DeltaVKmS:            m.DeltaVKmS,
            ObstaclesEncountered: m.ObstaclesEncountered,
            DepartureTime:        m.DepartureTime.ToString("O"),
            ArrivalTime:          m.ArrivalTime.ToString("O"),
            CreatedAt:            m.CreatedAt.ToString("O"));
    }

    private static MissionDetailResponse ToDetailResponse(MissionEntity m)
    {
        var obstacles = DeserializeObstacles(m.ObstaclesJson);
        var dest = KnownDestinations.FindById(m.Destination);

        // Re-compute score breakdown from stored values (deterministic)
        var (efficiency, safety, total) = MissionScoring.Compute(m.DeltaVKmS, m.RiskScore);
        var breakdown = new ScoreBreakdownDto(
            EfficiencyScore: (int)Math.Round(efficiency),
            SafetyScore:     (int)Math.Round(safety),
            Total:           total);

        return new MissionDetailResponse(
            Id:                   $"msn_{m.Id:N}",
            Destination:          m.Destination,
            DestinationDisplay:   dest?.DisplayName ?? m.Destination,
            Status:               m.Status,
            MissionScore:         m.MissionScore,
            RiskScore:            m.RiskScore,
            DeltaVKmS:            m.DeltaVKmS,
            DepartureTime:        m.DepartureTime.ToString("O"),
            ArrivalTime:          m.ArrivalTime.ToString("O"),
            CreatedAt:            m.CreatedAt.ToString("O"),
            Obstacles:            obstacles,
            ScoreBreakdown:       breakdown);
    }

    private static IReadOnlyList<ObstacleDto> DeserializeObstacles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ObstacleDto>>(json, JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
```

> **Nota sobre ScoreBreakdown:** `efficiency_score` e `safety_score` são inteiros na resposta (arredondados),
> mas `MissionScoring.Compute` retorna `double`. O arredondamento ocorre em `ToDetailResponse`.
> Isso é consistente com o `score_breakdown` do API_CONTRACT §10 (`"efficiency_score": 42, "safety_score": 45`).

Steps:
- [ ] Criar `MissionClear.Api/Services/MissionHistoryService.cs`
- [ ] `dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "MissionHistoryServiceTests"` — todos 10 devem passar (GREEN)
- [ ] Commit: `feat(history): MissionHistoryService via IMissionRepository`

---

## Task 6.3 — DashboardService (tests RED)

**File:** `MissionClear.Tests/Services/DashboardServiceTests.cs`

> Usa **Moq** para mockar `IOrbitalCache` e `IMissionRepository`.

```csharp
using FluentAssertions;
using MissionClear.Api.Data.Repositories;
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

    private static OrbitalObject MakeObj(string id, string type, double alt) =>
        new(id, $"OBJ-{id}", type, 0, 0, alt, 7.5, "celestrak", DateTime.UtcNow);

    // ──────────────────────────────────────────────────────────────────────────
    // GetSummaryAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummaryAsync_ReturnsNullUser_WhenNoUserId()
    {
        _cache.Setup(c => c.GetAll()).Returns([]);
        _cache.Setup(c => c.LastPropagation).Returns(DateTime.UtcNow);

        var result = await _service.GetSummaryAsync(null, default);

        result.User.Should().BeNull();
    }

    [Fact]
    public async Task GetSummaryAsync_CountsObjectsByType()
    {
        var objects = new List<OrbitalObject>
        {
            MakeObj("1", "debris",      450),
            MakeObj("2", "debris",      600),
            MakeObj("3", "satellite",   800),
            MakeObj("4", "rocket_body", 1200),
        };
        _cache.Setup(c => c.GetAll()).Returns(objects);
        _cache.Setup(c => c.LastPropagation).Returns(DateTime.UtcNow);

        var result = await _service.GetSummaryAsync(null, default);

        result.Orbital.TotalTrackedObjects.Should().Be(4);
        result.Orbital.ByType.Debris.Should().Be(2);
        result.Orbital.ByType.Satellite.Should().Be(1);
        result.Orbital.ByType.RocketBody.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_CountsObjectsByAltitudeBand()
    {
        var objects = new List<OrbitalObject>
        {
            MakeObj("1", "debris", 250),   // low  [200-500)
            MakeObj("2", "debris", 499),   // low  [200-500)
            MakeObj("3", "debris", 500),   // mid  [500-1000)
            MakeObj("4", "debris", 999),   // mid  [500-1000)
            MakeObj("5", "debris", 1000),  // high [1000-2000)
            MakeObj("6", "debris", 1999),  // high [1000-2000)
            MakeObj("7", "debris", 2500),  // out of LEO range — not counted
        };
        _cache.Setup(c => c.GetAll()).Returns(objects);
        _cache.Setup(c => c.LastPropagation).Returns(DateTime.UtcNow);

        var result = await _service.GetSummaryAsync(null, default);

        result.Orbital.ByAltitudeBand.Low200500km.Should().Be(2);
        result.Orbital.ByAltitudeBand.Mid5001000km.Should().Be(2);
        result.Orbital.ByAltitudeBand.High10002000km.Should().Be(2);
    }

    [Fact]
    public async Task GetSummaryAsync_WithUserId_CallsStatsRepo()
    {
        var userId = Guid.NewGuid();
        _cache.Setup(c => c.GetAll()).Returns([]);
        _cache.Setup(c => c.LastPropagation).Returns(DateTime.UtcNow);
        _missionRepo
            .Setup(r => r.GetStatsByUserIdAsync(userId, default))
            .ReturnsAsync(new MissionStatsProjection(5, 4, 1, 0, 90, 50, 75.0, 47.0, 3, "ISS",
                new() { { "ISS", 4 }, { "SSO", 1 } }));

        var result = await _service.GetSummaryAsync(userId, default);

        result.User.Should().NotBeNull();
        result.User!.TotalMissions.Should().Be(5);
        result.User.BestScore.Should().Be(90);
        _missionRepo.Verify(r => r.GetStatsByUserIdAsync(userId, default), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetAlertsAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAlertsAsync_ReturnsAlertsList_WithWindowAndTimestamp()
    {
        _cache.Setup(c => c.GetAll()).Returns([]);

        var result = await _service.GetAlertsAsync(6, "medium", default);

        result.Should().NotBeNull();
        result.Alerts.Should().NotBeNull();
        result.WindowHours.Should().Be(6);
        result.GeneratedAt.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetAlertsAsync_AlertIdsHaveAlrtPrefix()
    {
        // Debris at ISS altitude (408 km) should trigger a conjunction alert
        var objects = new List<OrbitalObject>
        {
            new("close1", "DEB-CLOSE", "debris", 0, 0, 410, 7.5, "celestrak", DateTime.UtcNow)
        };
        _cache.Setup(c => c.GetAll()).Returns(objects);

        var result = await _service.GetAlertsAsync(24, "low", default);

        foreach (var alert in result.Alerts)
        {
            alert.Id.Should().StartWith("alrt_");
        }
    }
}
```

Steps:
- [ ] Criar `MissionClear.Tests/Services/DashboardServiceTests.cs` com todos os 6 testes
- [ ] `dotnet build MissionClear.Tests/MissionClear.Tests.csproj` — deve falhar (RED — `DashboardService` não existe ainda)

---

## Task 6.4 — DashboardService (implementation GREEN)

**File:** `MissionClear.Api/Services/DashboardService.cs`

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
    // ConjunctionDetector is a lightweight stateless class — instantiate once per service instance.
    private readonly ConjunctionDetector _detector = new();

    // Altitude band boundaries (km) — aligned with API_CONTRACT §7 /api/debris/stats
    private const double LowMin  = 200;
    private const double LowMax  = 500;
    private const double MidMax  = 1000;
    private const double HighMax = 2000;

    // ── GetSummaryAsync ───────────────────────────────────────────────────────

    public async Task<DashboardSummaryResponse> GetSummaryAsync(Guid? userId, string? displayName = null, CancellationToken ct = default)
    {
        var all = cache.GetAll();
        var now = cache.LastPropagation ?? DateTime.UtcNow;

        // Type counts
        int debris = 0, satellite = 0, rocket = 0;
        // Altitude band counts
        int low = 0, mid = 0, high = 0;

        foreach (var o in all)
        {
            switch (o.Type)
            {
                case "debris":      debris++;   break;
                case "satellite":   satellite++; break;
                case "rocket_body": rocket++;   break;
            }

            if      (o.AltitudeKm >= LowMin && o.AltitudeKm < LowMax)  low++;
            else if (o.AltitudeKm >= LowMax && o.AltitudeKm < MidMax)  mid++;
            else if (o.AltitudeKm >= MidMax && o.AltitudeKm < HighMax) high++;
        }

        var alertCount = CountActiveAlerts(all, DateTime.UtcNow);

        var orbital = new OrbitalSummaryDto(
            TotalTrackedObjects:    all.Count,
            ByType:                 new ByTypeDto(debris, satellite, rocket),
            ByAltitudeBand:         new ByAltitudeBandDto(low, mid, high),
            ActiveConjunctionAlerts: alertCount,
            LastUpdated:            now.ToString("O"));

        UserDashboardDto? userDto = null;
        if (userId.HasValue)
        {
            var stats = await missionRepo.GetStatsByUserIdAsync(userId.Value, ct);
            userDto = new UserDashboardDto(
                DisplayName:   displayName ?? "",
                TotalMissions: stats.Total,
                BestScore:     stats.BestScore,
                LastMission:   null);
        }

        return new DashboardSummaryResponse(orbital, userDto);
    }

    // ── GetAlertsAsync ────────────────────────────────────────────────────────

    public Task<AlertsResponse> GetAlertsAsync(int windowHours, string minRisk, CancellationToken ct)
    {
        var all = cache.GetAll();
        var now = DateTime.UtcNow;
        var minLevel = ParseRiskLevel(minRisk);
        var windowMinutes = windowHours * 60;
        var alerts = new List<AlertDto>();

        foreach (var dest in KnownDestinations.All)
        {
            var conjunctions = _detector.Detect(dest, now, all);

            foreach (var c in conjunctions)
            {
                if (c.RiskLevel < minLevel) continue;

                var minutesUntil = (int)(c.TimeOfClosestApproach - now).TotalMinutes;
                if (minutesUntil < 0 || minutesUntil > windowMinutes) continue;

                alerts.Add(new AlertDto(
                    Id:                       $"alrt_{Guid.NewGuid():N}",
                    DebrisId:                 c.DebrisId,
                    DebrisName:               c.DebrisName,
                    AffectedDestination:      dest.Id,
                    ClosestApproachKm:        c.ClosestApproachKm,
                    TimeOfClosestApproach:    c.TimeOfClosestApproach.ToString("O"),
                    RiskLevel:                c.RiskLevel.ToString().ToLowerInvariant(),
                    MinutesUntilConjunction:  minutesUntil,
                    DetectedAt:               now.ToString("O")));
            }
        }

        // Sort by closest approach ascending (API_CONTRACT §11)
        alerts.Sort((a, b) => a.ClosestApproachKm.CompareTo(b.ClosestApproachKm));

        return Task.FromResult(new AlertsResponse(alerts, windowHours, now.ToString("O")));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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

    private static RiskLevel ParseRiskLevel(string level) => level.ToLowerInvariant() switch
    {
        "low"      => RiskLevel.Low,
        "high"     => RiskLevel.High,
        "critical" => RiskLevel.Critical,
        _          => RiskLevel.Medium
    };
}
```

Steps:
- [ ] Criar `MissionClear.Api/Services/DashboardService.cs`
- [ ] `dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "DashboardServiceTests"` — todos 6 devem passar (GREEN)
- [ ] Commit: `feat(dashboard): DashboardService via IOrbitalCache + IMissionRepository`

---

## Task 6.5 — DI Registration

Adicionar em `MissionClear.Api/Program.cs` na seção de Services:

```csharp
// Mission History & Dashboard
builder.Services.AddScoped<IMissionHistoryService, MissionHistoryService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
```

Steps:
- [ ] Adicionar registros em `Program.cs`
- [ ] `dotnet build` — toda a solution deve compilar sem erros
- [ ] `dotnet test` — toda a suíte deve continuar verde
- [ ] Commit: `chore: register MissionHistoryService and DashboardService in DI`

---

## Contratos de Resposta (alinhamento com API_CONTRACT.md)

### GET /api/missions — §10

```
PagedResponse<MissionSummaryDto>
  .Data[].Id                → "msn_{guid:N}"
  .Data[].destination       → "ISS"
  .Data[].destination_display → "Estação Espacial Internacional"
  .Data[].status            → "success" | "failure" | "aborted"
  .Data[].mission_score     → inteiro
  .Data[].risk_score        → 4 casas decimais
  .Data[].delta_v_km_s      → 2 casas decimais
  .Data[].obstacles_encountered → inteiro
  .Pagination.page, .limit, .total, .total_pages
```

### GET /api/missions/{id} — §10

```
MissionDetailResponse
  .score_breakdown.efficiency_score → inteiro
  .score_breakdown.safety_score     → inteiro
  .score_breakdown.total            → inteiro
  .obstacles[]                      → ObstacleDto (deserializado de ObstaclesJson)
```

**Consistência ScoreBreakdown:**
- `efficiency = Math.Max(0, 1 - deltaV/12) * 50` → arredondado para int
- `safety = (1 - clamp(riskScore, 0, 1)) * 50` → arredondado para int
- `total = clamp(round(efficiency_raw + safety_raw), 0, 100)` — usa os doubles antes do arredondamento

### GET /api/missions/stats — §10

Todos os campos obrigatórios da resposta:

| Campo API (snake_case) | Propriedade DTO | Fonte |
|---|---|---|
| `total_missions` | `TotalMissions` | `stats.Total` |
| `successful_missions` | `SuccessfulMissions` | `stats.Successful` |
| `failed_missions` | `FailedMissions` | `stats.Failed` |
| `aborted_missions` | `AbortedMissions` | `stats.Aborted` |
| `success_rate` | `SuccessRate` | `round(Successful/Total, 2)` |
| `best_score` | `BestScore` | `stats.BestScore` |
| `worst_score` | `WorstScore` | `stats.WorstScore` |
| `average_score` | `AverageScore` | `round(stats.AverageScore)` como int |
| `total_delta_v_km_s` | `TotalDeltaVKmS` | `round(stats.TotalDeltaV, 2)` |
| `total_obstacles_encountered` | `TotalObstaclesEncountered` | `stats.TotalObstacles` |
| `favorite_destination` | `FavoriteDestination` | `stats.FavoriteDestination` |
| `missions_by_destination` | `MissionsByDestination` | `stats.MissionsByDestination` |

### GET /api/dashboard/alerts — §11

```
AlertsResponse
  .alerts[].id                     → "alrt_{Guid.NewGuid():N}"
  .alerts[].debris_id              → NORAD ID
  .alerts[].debris_name            → nome do objeto
  .alerts[].affected_destination   → "ISS" | "LEO_GENERIC" | "SSO"
  .alerts[].closest_approach_km    → double arredondado
  .alerts[].time_of_closest_approach → ISO 8601 UTC
  .alerts[].risk_level             → "low" | "medium" | "high" | "critical"
  .alerts[].minutes_until_conjunction → inteiro
  .alerts[].detected_at            → ISO 8601 UTC
  .window_hours                    → int
  .generated_at                    → ISO 8601 UTC
Ordenado por closest_approach_km ASC
```

---

## Testing Strategy

| Camada | Cobertura | Ferramenta |
|--------|-----------|------------|
| Unit — MissionHistoryService | 10 testes: paginação, total_pages, destination_display, 404, 403 (detail), 403 (delete), stats mapeados, success_rate, delete chama repo, save cria entity | Moq + FluentAssertions |
| Unit — DashboardService | 6 testes: null user, contagem por tipo, contagem por altitude, user dto via repo, alerts lista, alert IDs têm prefixo | Moq + FluentAssertions |
| Integração HTTP | Coberta em plan-07 via `WebApplicationFactory` | xUnit |
| Coverage target | >= 80% em Services/ | dotnet-coverage |

---

## Risks & Mitigations

| Risco | Impacto | Mitigação |
|-------|---------|-----------|
| `ScoreBreakdown` int vs double inconsistência | Respostas com valores errados | `ToDetailResponse` sempre re-computa via `MissionScoring.Compute` — nunca lê do JSON persistido |
| `GetSummaryAsync` com userId fornece `DisplayName` vazio | Dashboard mostra nome em branco | Controller extrai `displayName` das Claims JWT e passa como parâmetro para `GetSummaryAsync`; serviço usa `displayName ?? ""` |
| `ConjunctionDetector.Detect` retorna zero conjunções em empty cache | Sem alertas na inicialização | Comportamento correto — cache vazio = 0 alertas; sistema deve aguardar `GET /api/status` retornar `"ready"` |
| `MissionsByDestination` dict não tem chaves para ISS/LEO_GENERIC/SSO quando count=0 | Mobile recebe dict parcial | Mobile deve tratar chaves ausentes como 0; contrato não garante todas as chaves presentes |
| Migrations ainda não rodaram (plan-01) | Repository falha em produção | Fase 6 depende estricamente de plan-01 concluído |

---

## Success Criteria

- [ ] `IMissionHistoryService` e `IDashboardService` em `Services/Interfaces/`
- [ ] `MissionHistoryService` e `DashboardService` em `Services/`
- [ ] **Zero injeção de `AppDbContext`** em qualquer service desta fase
- [ ] Todos os 16 testes passam (10 + 6)
- [ ] `dotnet build` sem warnings ou erros
- [ ] `MissionStatsResponse` tem todos os 12 campos do API_CONTRACT §10
- [ ] `destination_display` populado via `KnownDestinations.FindById`
- [ ] IDs de missão com prefixo `msn_` + `{guid:N}`
- [ ] IDs de alerta com prefixo `alrt_` + `{guid:N}`
- [ ] Alertas ordenados por `closest_approach_km` ASC
- [ ] `DomainException("FORBIDDEN", ..., 403)` para acesso cross-user
- [ ] `DomainException("MISSION_NOT_FOUND", ..., 404)` para ID inexistente
- [ ] DI registrado como `Scoped`
- [ ] 3 commits atômicos (history service, dashboard service, DI)

---

## Arquivos desta fase

```
MissionClear.Api/Services/Interfaces/
├── IMissionHistoryService.cs     ← interface
├── IDashboardService.cs          ← interface

MissionClear.Api/Services/
├── MissionHistoryService.cs      ← implementação
├── DashboardService.cs           ← implementação

MissionClear.Tests/Services/
├── MissionHistoryServiceTests.cs ← 10 testes
├── DashboardServiceTests.cs      ← 6 testes

MissionClear.Api/Program.cs       ← edit: DI registration
```

> **Namespaces:**
> - Interfaces: `MissionClear.Api.Services.Interfaces`
> - Implementações: `MissionClear.Api.Services`
> - Testes: `MissionClear.Tests.Services`
