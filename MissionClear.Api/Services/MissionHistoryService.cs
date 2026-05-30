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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<PagedResponse<MissionSummaryDto>> GetMissionsAsync(
        Guid userId,
        int page,
        int limit,
        string? status,
        string? destination,
        string sort,
        CancellationToken ct)
    {
        var result = await missionRepo.GetPagedAsync(userId, page, limit, status, destination, sort, ct);

        var dtos = result.Items.Select(m => new MissionSummaryDto(
            $"msn_{m.Id:N}",
            m.Destination,
            KnownDestinations.FindById(m.Destination)?.DisplayName ?? m.Destination,
            m.Status,
            m.MissionScore,
            Math.Round(m.RiskScore, 4),
            m.DeltaVKmS,
            m.ObstaclesEncountered,
            m.DepartureTime.ToString("O"),
            m.ArrivalTime.ToString("O"),
            m.CreatedAt.ToString("O"))).ToList();

        return new PagedResponse<MissionSummaryDto>(
            dtos,
            PaginationDto.From(page, limit, result.TotalCount));
    }

    public async Task<MissionDetailResponse> GetMissionDetailAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var mission = await missionRepo.GetByIdAsync(id, ct)
            ?? throw new DomainException("MISSION_NOT_FOUND", "Mission not found.", 404);

        if (mission.UserId != userId)
            throw new DomainException("FORBIDDEN", "You do not have access to this mission.", 403);

        var (eff, saf, total) = MissionScoring.Compute(mission.DeltaVKmS, mission.RiskScore);

        var obstacles = JsonSerializer.Deserialize<List<ObstacleDto>>(
            mission.ObstaclesJson ?? "[]", JsonOptions) ?? [];

        return new MissionDetailResponse(
            $"msn_{mission.Id:N}",
            mission.Destination,
            KnownDestinations.FindById(mission.Destination)?.DisplayName ?? mission.Destination,
            mission.Status,
            total,
            Math.Round(mission.RiskScore, 4),
            mission.DeltaVKmS,
            mission.DepartureTime.ToString("O"),
            mission.ArrivalTime.ToString("O"),
            mission.CreatedAt.ToString("O"),
            obstacles,
            new ScoreBreakdownDto((int)Math.Round(eff), (int)Math.Round(saf), total));
    }

    public async Task<MissionStatsResponse> GetStatsAsync(Guid userId, CancellationToken ct)
    {
        var stats = await missionRepo.GetUserStatsAsync(userId, ct);

        double successRate = stats.TotalMissions > 0 
            ? Math.Round((double)stats.SuccessfulMissions / stats.TotalMissions, 2) 
            : 0.0;

        return new MissionStatsResponse(
            stats.TotalMissions,
            stats.SuccessfulMissions,
            stats.FailedMissions,
            stats.AbortedMissions,
            successRate,
            stats.BestScore,
            stats.WorstScore,
            (int)Math.Round(stats.AverageScore),
            Math.Round(stats.TotalDeltaV, 2),
            stats.TotalObstacles,
            stats.FavoriteDestination,
            stats.MissionsByDestination.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    public async Task DeleteMissionAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var mission = await missionRepo.GetByIdAsync(id, ct)
            ?? throw new DomainException("MISSION_NOT_FOUND", "Mission not found.", 404);

        if (mission.UserId != userId)
            throw new DomainException("FORBIDDEN", "You do not have access to this mission.", 403);

        await missionRepo.DeleteAsync(mission, ct);
        await missionRepo.SaveChangesAsync(ct);
    }

    public async Task<MissionSummaryDto> SaveMissionAsync(
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
        CancellationToken ct)
    {
        var mission = new MissionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Destination = destination,
            Status = status.ToLowerInvariant(),
            MissionScore = score,
            RiskScore = riskScore,
            DeltaVKmS = deltaV,
            ObstaclesEncountered = obstacles,
            DepartureTime = departure,
            ArrivalTime = arrival,
            CreatedAt = DateTime.UtcNow,
            ObstaclesJson = JsonSerializer.Serialize(obstaclesData, JsonOptions)
        };

        await missionRepo.AddAsync(mission, ct);
        await missionRepo.SaveChangesAsync(ct);

        return new MissionSummaryDto(
            $"msn_{mission.Id:N}",
            mission.Destination,
            KnownDestinations.FindById(mission.Destination)?.DisplayName ?? mission.Destination,
            mission.Status,
            mission.MissionScore,
            Math.Round(mission.RiskScore, 4),
            mission.DeltaVKmS,
            mission.ObstaclesEncountered,
            mission.DepartureTime.ToString("O"),
            mission.ArrivalTime.ToString("O"),
            mission.CreatedAt.ToString("O"));
    }
}
