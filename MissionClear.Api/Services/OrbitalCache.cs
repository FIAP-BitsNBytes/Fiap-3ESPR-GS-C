using System.Collections.Concurrent;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

/// <summary>
/// Thread-safe in-memory cache for orbital objects.
///
/// Design notes:
///   - _snapshot is written atomically via volatile assignment (no reader lock needed).
///   - _index uses ConcurrentDictionary for O(1) GetById without locking.
///   - Update() holds _writeLock to serialise merges (CelesTrak-wins logic must be atomic).
///   - LEO filter: 200 ≤ altitudeKm ≤ 2000.
///   - Age filter: UpdatedAt within last 7 days.
///   - LastFetch is set when any incoming object has TleLine1 != null.
///   - LastPropagation is set when all incoming objects have TleLine1 == null.
/// </summary>
public sealed class OrbitalCache : IOrbitalCache
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private const double AltMin = 200.0;
    private const double AltMax = 2000.0;

    private volatile IReadOnlyList<OrbitalObject> _snapshot = [];
    private readonly ConcurrentDictionary<string, OrbitalObject> _index = new(StringComparer.Ordinal);
    private readonly object _writeLock = new();

    public bool IsReady => _snapshot.Count > 0;
    public DateTime? LastFetch { get; private set; }
    public DateTime? LastPropagation { get; private set; }
    public int Count => _snapshot.Count;

    public IReadOnlyList<OrbitalObject> GetAll() => _snapshot;

    public OrbitalObject? GetById(string id) =>
        _index.TryGetValue(id, out var obj) ? obj : null;

    public void Update(IReadOnlyList<OrbitalObject> objects, bool isFetch = false)
    {
        ArgumentNullException.ThrowIfNull(objects);

        var now = DateTime.UtcNow;
        var cutoff = now - MaxAge;

        lock (_writeLock)
        {
            // Build deduplication map: CelesTrak wins on Id conflict.
            var merged = new Dictionary<string, OrbitalObject>(objects.Count, StringComparer.Ordinal);
            foreach (var obj in objects)
            {
                if (obj.AltitudeKm < AltMin || obj.AltitudeKm > AltMax) continue;
                if (obj.UpdatedAt < cutoff) continue;

                if (merged.TryGetValue(obj.Id, out var existing))
                {
                    // CelesTrak sources use "celestrak" or "celestrak-{label}" (e.g. "celestrak-stations").
                    // KeepTrack uses "keeptrack". CelesTrak always wins.
                    var incomingIsCelesTrak = obj.Source.StartsWith("celestrak", StringComparison.OrdinalIgnoreCase);
                    var existingIsCelesTrak = existing.Source.StartsWith("celestrak", StringComparison.OrdinalIgnoreCase);
                    if (incomingIsCelesTrak && !existingIsCelesTrak)
                    {
                        merged[obj.Id] = obj;
                    }
                    // Existing CelesTrak is never overwritten by KeepTrack.
                }
                else
                {
                    merged[obj.Id] = obj;
                }
            }

            var filtered = merged.Values.ToList();

            // Rebuild index atomically from new filtered set.
            _index.Clear();
            foreach (var obj in filtered)
                _index[obj.Id] = obj;

            _snapshot = filtered.AsReadOnly();

            if (isFetch || objects.Any(o => o.TleLine1 is not null))
                LastFetch = now;
            else
                LastPropagation = now;
        }
    }
}
