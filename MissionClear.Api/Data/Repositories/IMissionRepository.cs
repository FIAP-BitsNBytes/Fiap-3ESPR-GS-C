using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public sealed record MissionPageResult(
    IEnumerable<MissionEntity> Items,
    int TotalCount
);

public sealed record MissionStatsProjection(
    int TotalMissions,
    int SuccessfulMissions,
    int FailedMissions,
    int AbortedMissions,
    int BestScore,
    int WorstScore,
    double AverageScore,
    double TotalDeltaV,
    int TotalObstacles,
    string? FavoriteDestination,
    IDictionary<string, int> MissionsByDestination
);

public interface IMissionRepository
{
    Task<MissionEntity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<MissionPageResult> GetPagedAsync(
        Guid userId, 
        int page, 
        int limit, 
        string? status = null, 
        string? destination = null, 
        string sort = "created_at_desc", 
        CancellationToken ct = default);
        
    Task<Dictionary<Guid, int>> GetMissionCountsPerUserAsync(CancellationToken ct = default);
    Task<MissionPageResult> GetAllPagedAsync(
        int page, int limit,
        string? status = null,
        string? destination = null,
        CancellationToken ct = default);
    Task AddAsync(MissionEntity mission, CancellationToken ct = default);
    Task DeleteAsync(MissionEntity mission, CancellationToken ct = default);
    Task<MissionStatsProjection> GetUserStatsAsync(Guid userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
