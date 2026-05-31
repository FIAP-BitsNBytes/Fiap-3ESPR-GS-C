using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

/// <summary>
/// Thread-safe in-memory store for orbital objects.
/// Single Update() method handles both TLE ingestion and post-propagation results.
/// </summary>
public interface IOrbitalCache
{
    /// <summary>True when Count > 0.</summary>
    bool IsReady { get; }

    /// <summary>Set when Update() receives objects with TleLine1 != null.</summary>
    DateTime? LastFetch { get; }

    /// <summary>Set when Update() receives objects with TleLine1 == null (propagated).</summary>
    DateTime? LastPropagation { get; }

    int Count { get; }

    IReadOnlyList<OrbitalObject> GetAll();
    OrbitalObject? GetById(string id);

    /// <summary>
    /// Replaces the cache contents after applying LEO filter (200–2000 km)
    /// and age filter (UpdatedAt within last 7 days).
    /// During merge, CelesTrak source wins on Id conflict.
    /// Pass <paramref name="isFetch"/> = true when called from TLE ingestion,
    /// false when called from propagation — controls LastFetch/LastPropagation timestamps.
    /// </summary>
    void Update(IReadOnlyList<OrbitalObject> objects, bool isFetch = false);
}
