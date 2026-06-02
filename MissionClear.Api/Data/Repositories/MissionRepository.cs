using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public sealed class MissionRepository(AppDbContext context) : IMissionRepository
{
    public async Task<MissionEntity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.Missions.FindAsync([id], ct);
    }

    public async Task<MissionPageResult> GetPagedAsync(
        Guid userId, 
        int page, 
        int limit, 
        string? status = null, 
        string? destination = null, 
        string sort = "created_at_desc", 
        CancellationToken ct = default)
    {
        var query = context.Missions.Where(m => m.UserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(m => m.Status == status);

        if (!string.IsNullOrWhiteSpace(destination))
            query = query.Where(m => m.Destination.Contains(destination));

        var total = await query.CountAsync(ct);

        query = sort.ToLower() switch
        {
            "score_desc" => query.OrderByDescending(m => m.MissionScore),
            "risk_score_asc" => query.OrderBy(m => m.RiskScore),
            _ => query.OrderByDescending(m => m.CreatedAt)
        };

        var items = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        return new MissionPageResult(items, total);
    }

    public async Task<Dictionary<Guid, int>> GetMissionCountsPerUserAsync(CancellationToken ct = default)
    {
        return await context.Missions
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
    }

    public async Task<MissionPageResult> GetAllPagedAsync(
        int page, int limit,
        string? status = null,
        string? destination = null,
        CancellationToken ct = default)
    {
        var query = context.Missions.Include(m => m.User).AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(m => m.Status == status);

        if (!string.IsNullOrWhiteSpace(destination))
            query = query.Where(m => m.Destination.Contains(destination));

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        return new MissionPageResult(items, total);
    }

    public async Task AddAsync(MissionEntity mission, CancellationToken ct = default)
    {
        await context.Missions.AddAsync(mission, ct);
    }

    public Task DeleteAsync(MissionEntity mission, CancellationToken ct = default)
    {
        context.Missions.Remove(mission);
        return Task.CompletedTask;
    }

    public async Task<MissionStatsProjection> GetUserStatsAsync(Guid userId, CancellationToken ct = default)
    {
        var missions = await context.Missions
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

        if (missions.Count == 0)
        {
            return new MissionStatsProjection(0, 0, 0, 0, 0, 0, 0, 0, 0, null, new Dictionary<string, int>());
        }

        var total = missions.Count;
        var success = missions.Count(m => m.Status == "success");
        var failed = missions.Count(m => m.Status == "failure");
        var aborted = missions.Count(m => m.Status == "aborted");
        
        var best = missions.Max(m => m.MissionScore);
        var worst = missions.Min(m => m.MissionScore);
        var avg = missions.Average(m => m.MissionScore);
        
        var totalDeltaV = missions.Sum(m => m.DeltaVKmS);
        var totalObstacles = missions.Sum(m => m.ObstaclesEncountered);

        var byDest = missions
            .GroupBy(m => m.Destination)
            .ToDictionary(g => g.Key, g => g.Count());

        var favorite = byDest.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;

        return new MissionStatsProjection(
            total, success, failed, aborted,
            best, worst, avg,
            totalDeltaV, totalObstacles,
            favorite, byDest
        );
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await context.SaveChangesAsync(ct);
    }
}
