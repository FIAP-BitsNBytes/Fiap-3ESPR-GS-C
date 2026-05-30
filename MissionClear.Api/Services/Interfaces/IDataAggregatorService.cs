using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface IDataAggregatorService
{
    /// <summary>
    /// Fetches from CelesTrak (required) and KeepTrack (optional).
    /// Merges results with CelesTrak-wins deduplication.
    /// Calls IOrbitalCache.Update() with merged result.
    /// Throws HttpRequestException if CelesTrak fails.
    /// Never throws for KeepTrack failures.
    /// </summary>
    Task FetchAndMergeAsync(CancellationToken ct = default);
}
