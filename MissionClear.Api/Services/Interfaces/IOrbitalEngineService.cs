using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface IOrbitalEngineService
{
    /// <summary>
    /// Propagates a single object to the given UTC instant.
    /// Deterministic: same id + same atTime always returns the same position.
    /// Returns null if the object has no TLE lines (already propagated, no re-propagation needed).
    /// </summary>
    OrbitalObject? Propagate(OrbitalObject raw, DateTime atTime);

    /// <summary>
    /// Propagates all objects in parallel. Objects without TLE lines are passed through unchanged.
    /// Never throws — skips individual failures silently.
    /// </summary>
    IReadOnlyList<OrbitalObject> PropagateAll(IReadOnlyList<OrbitalObject> objects, DateTime atTime);
}
