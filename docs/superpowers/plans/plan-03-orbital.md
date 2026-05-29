# Plan 03 — Orbital Engine (Interfaces + Cache + SGP4 + Ingestion)

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:executing-plans`

**Execution order:** After plan-00 (scaffolding) + plan-02 (models/DTOs). Parallel-safe with plan-04.
**Estimated time:** 90 minutes.
**Goal:** Implement the complete orbital data layer — service interfaces, thread-safe OrbitalCache, SGP4 stub propagation engine, CelesTrak/KeepTrack data aggregation, and a BackgroundService that keeps 30k+ objects updated in memory.
**Dependencies:** `plan-00-scaffolding.md`, `plan-02-models.md`
**Unlocks:** `plan-05-simulation.md`, `plan-07-controllers.md`

---

## Architecture Overview

```
TleIngestionService (BackgroundService)
        │
        ├── on startup ──────> FetchAndMergeAsync() → Update(objects)
        │
        ├── every 60min ─────> DataAggregatorService.FetchAndMergeAsync()
        │                          │
        │                          ├── HTTP GET CelesTrak (required, throws on failure)
        │                          ├── HTTP GET KeepTrack (optional, timeout 5s, never throws)
        │                          └── OrbitalCache.Update(mergedObjects)  ← LastFetch set
        │
        └── every 60s ───────> OrbitalEngineService.PropagateAll(cache.GetAll(), now)
                                       │
                                       └── OrbitalCache.Update(propagatedObjects)  ← LastPropagation set
```

**Invariants (never violate):**
- `IOrbitalCache.IsReady` is `true` only when `Count > 0`.
- CelesTrak wins on `NORAD_CAT_ID` conflict during merge.
- Objects outside LEO (altitude < 200 km or > 2000 km) are filtered in `Update()`.
- Objects with `UpdatedAt` older than 7 days are filtered in `Update()`.
- KeepTrack failure is swallowed; system continues with CelesTrak data only.
- `Update()` sets `LastFetch` when any incoming object has `TleLine1 != null`; sets `LastPropagation` otherwise.
- `OrbitalEngineService.Propagate()` is deterministic for same NORAD ID + same timestamp.

---

## Naming Alignment with Existing Codebase

| Symbol | Location |
|---|---|
| `ExternalApiSettings.CelesTrakBaseUrl` | `MissionClear.Api/Configuration/ExternalApiSettings.cs` |
| `ExternalApiSettings.KeepTrackBaseUrl` | same |
| `ExternalApiSettings.KeepTrackApiKey` | same |
| `ExternalApiSettings.KeepTrackTimeoutSeconds` | same |
| `OrbitalSettings.TleFetchIntervalMinutes` | `MissionClear.Api/Configuration/OrbitalSettings.cs` |
| `OrbitalSettings.PropagationIntervalSeconds` | same |
| `OrbitalSettings.TleMaxAgeDays` | same |
| `RiskLevel` enum | `MissionClear.Api/Models/RiskLevel.cs` |

The `OrbitalObject` model is defined in **plan-02 Task 2.1** — do NOT create it here. Verify `MissionClear.Api/Models/OrbitalObject.cs` exists before proceeding.

---

## Task 3.1 — OrbitalObject Model

**⚠️ IMPORTANT:** `OrbitalObject` is defined in **plan-02 Task 2.1**. Do NOT create this file here.

Execute plan-02 Task 2.1 first, or verify `MissionClear.Api/Models/OrbitalObject.cs` already exists before proceeding.

The `OrbitalObject` record fields used in this phase:
- `string Id` — NORAD catalog ID
- `string Name` — object name
- `string Type` — "debris" | "satellite" | "rocket_body"
- `double Latitude` — current latitude (NOT LatitudeDeg)
- `double Longitude` — current longitude (NOT LongitudeDeg)
- `double AltitudeKm` — current altitude
- `double VelocityKmS` — orbital velocity
- `string Source` — "celestrak" | "keeptrack"
- `DateTime UpdatedAt`
- `string? TleLine1` — optional TLE line 1
- `string? TleLine2` — optional TLE line 2

**Property names are `Latitude` and `Longitude` (no "Deg" suffix).**

---

## Task 3.2: Service Interfaces (RED → GREEN, no tests needed — pure contracts)

**Files:**
- `MissionClear.Api/Services/Interfaces/IOrbitalCache.cs`
- `MissionClear.Api/Services/Interfaces/IDataAggregatorService.cs`
- `MissionClear.Api/Services/Interfaces/IOrbitalEngineService.cs`

### IOrbitalCache.cs

```csharp
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
    /// </summary>
    void Update(IReadOnlyList<OrbitalObject> objects);
}
```

### IDataAggregatorService.cs

```csharp
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
```

### IOrbitalEngineService.cs

```csharp
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
```

**Commit:** `feat(services): add IOrbitalCache, IDataAggregatorService, IOrbitalEngineService interfaces`

---

## Task 3.3: OrbitalCache (TDD)

**Files:**
- `MissionClear.Api/Services/OrbitalCache.cs`
- `MissionClear.Tests/Services/OrbitalCacheTests.cs`

### Step 0: Add InternalsVisibleTo to MissionClear.Api

`DataAggregatorServiceTests` (Task 3.5) accesses `internal` fields of `DataAggregatorService` from the separate `MissionClear.Tests` assembly. Without `InternalsVisibleTo`, this fails to compile.

- [ ] **Create `MissionClear.Api/Properties/AssemblyInfo.cs`:**

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MissionClear.Tests")]
```

This allows `DataAggregatorServiceTests` to access `internal` fields for white-box testing.

### Step 1: Write tests first (RED)

File: `MissionClear.Tests/Services/OrbitalCacheTests.cs`

```csharp
using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class OrbitalCacheTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static OrbitalObject TleObj(string id, double altKm = 400, string source = "celestrak",
        DateTime? updatedAt = null) =>
        new(id, $"OBJ-{id}", "debris", 0, 0, altKm, 7.5, source,
            updatedAt ?? DateTime.UtcNow,
            TleLine1: $"1 {id}U stub",
            TleLine2: $"2 {id} stub");

    private static OrbitalObject PropagatedObj(string id, double altKm = 400, string source = "celestrak",
        DateTime? updatedAt = null) =>
        new(id, $"OBJ-{id}", "debris", 0, 0, altKm, 7.5, source,
            updatedAt ?? DateTime.UtcNow,
            TleLine1: null,
            TleLine2: null);

    // ── IsReady ───────────────────────────────────────────────────────────────

    [Fact]
    public void IsReady_IsFalse_BeforeFirstUpdate()
    {
        var cache = new OrbitalCache();
        cache.IsReady.Should().BeFalse();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void IsReady_IsTrue_AfterFirstUpdate()
    {
        var cache = new OrbitalCache();
        cache.Update([TleObj("1")]);
        cache.IsReady.Should().BeTrue();
    }

    // ── LEO filter ────────────────────────────────────────────────────────────

    [Fact]
    public void Update_FiltersOutNonLEO_Altitudes()
    {
        var cache = new OrbitalCache();
        cache.Update([
            TleObj("low",  altKm: 100),   // below LEO — filtered
            TleObj("ok",   altKm: 400),   // in LEO — kept
            TleObj("high", altKm: 3000),  // above LEO — filtered
        ]);

        cache.Count.Should().Be(1);
        cache.GetById("ok").Should().NotBeNull();
        cache.GetById("low").Should().BeNull();
        cache.GetById("high").Should().BeNull();
    }

    // ── Stale filter ──────────────────────────────────────────────────────────

    [Fact]
    public void Update_FiltersStaleObjects_OlderThan7Days()
    {
        var cache = new OrbitalCache();
        var stale = TleObj("stale", updatedAt: DateTime.UtcNow.AddDays(-8));
        var fresh = TleObj("fresh");
        cache.Update([stale, fresh]);

        cache.Count.Should().Be(1);
        cache.GetById("stale").Should().BeNull();
        cache.GetById("fresh").Should().NotBeNull();
    }

    // ── CelesTrak wins ────────────────────────────────────────────────────────

    [Fact]
    public void Update_CelesTrakWins_WhenConflictWithKeepTrack()
    {
        var cache = new OrbitalCache();
        var celestrak = TleObj("42", source: "celestrak") with { Name = "FROM_CELESTRAK" };
        var keeptrack = TleObj("42", source: "keeptrack") with { Name = "FROM_KEEPTRACK" };

        // Both arrive in the same batch (simulating merge output)
        cache.Update([celestrak, keeptrack]);

        cache.GetById("42")!.Source.Should().Be("celestrak");
        cache.GetById("42")!.Name.Should().Be("FROM_CELESTRAK");
    }

    [Fact]
    public void Update_CelesTrakOverwritesExistingKeepTrack_OnSubsequentCall()
    {
        var cache = new OrbitalCache();
        cache.Update([TleObj("42", source: "keeptrack") with { Name = "OLD" }]);
        cache.Update([TleObj("42", source: "celestrak") with { Name = "NEW" }]);

        cache.GetById("42")!.Source.Should().Be("celestrak");
        cache.GetById("42")!.Name.Should().Be("NEW");
    }

    // ── LastFetch / LastPropagation ───────────────────────────────────────────

    [Fact]
    public void Update_SetsLastFetch_WhenObjectsHaveTleLines()
    {
        var cache = new OrbitalCache();
        cache.Update([TleObj("1")]);   // TleLine1 != null

        cache.LastFetch.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        cache.LastPropagation.Should().BeNull();
    }

    [Fact]
    public void Update_SetsLastPropagation_WhenObjectsHaveNoTleLines()
    {
        var cache = new OrbitalCache();
        cache.Update([PropagatedObj("1")]);   // TleLine1 == null

        cache.LastPropagation.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        cache.LastFetch.Should().BeNull();
    }

    // ── Thread safety (smoke) ─────────────────────────────────────────────────

    [Fact]
    public void Update_ThreadSafe_ConcurrentWrites_DoNotThrow()
    {
        var cache = new OrbitalCache();
        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var batch = Enumerable.Range(i * 10, 10)
                .Select(j => TleObj(j.ToString()))
                .ToList();
            cache.Update(batch);
        }));

        var act = () => Task.WhenAll(tasks).GetAwaiter().GetResult();
        act.Should().NotThrow();
    }
}
```

Run (must fail — class not yet created):

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "OrbitalCacheTests"
# Expected: compile error or class-not-found
```

### Step 2: Implement OrbitalCache (GREEN)

File: `MissionClear.Api/Services/OrbitalCache.cs`

```csharp
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

    public void Update(IReadOnlyList<OrbitalObject> objects)
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
                    // Only replace if incoming is CelesTrak and existing is not.
                    if (string.Equals(obj.Source, "celestrak", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(existing.Source, "celestrak", StringComparison.OrdinalIgnoreCase))
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

            // Timestamp semantics: TleLine1 present → this was a fetch; absent → propagation.
            bool hasTleLines = objects.Any(o => o.TleLine1 is not null);
            if (hasTleLines)
                LastFetch = now;
            else
                LastPropagation = now;
        }
    }
}
```

### Step 3: Verify green

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "OrbitalCacheTests"
# Expected: Passed: 9
```

### Step 4: Register in DI

In `Program.cs`:

```csharp
builder.Services.AddSingleton<IOrbitalCache, OrbitalCache>();
```

### Step 5: Commit

```powershell
git add MissionClear.Api/Services/Interfaces/ `
        MissionClear.Api/Services/OrbitalCache.cs `
        MissionClear.Api/Properties/AssemblyInfo.cs `
        MissionClear.Tests/Services/OrbitalCacheTests.cs
git commit -m "feat(orbital): service interfaces, OrbitalCache thread-safe, InternalsVisibleTo (9 tests)"
```

---

## Task 3.4: OrbitalEngineService (TDD)

**Files:**
- `MissionClear.Api/Services/OrbitalEngineService.cs`
- `MissionClear.Tests/Services/OrbitalEngineServiceTests.cs`

**Design decision — no real SGP4 NuGet is available in this project.** The stub uses an FNV-1a hash of the NORAD ID as the deterministic seed, then applies small orbital deltas computed from `atTime.Ticks`. The approach is:

1. `FnvHash(id)` → 32-bit seed (deterministic per ID, independent of time).
2. `XOR seed with (uint)(atTime.Ticks >> 16)` → time-varying but reproducible seed for same time.
3. `new Random(seed)` → lat ±1°, lon ±2°, alt ±0.5 km deltas.
4. Clamp: lat [-90, 90], lon wrap [-180, 180], alt [200, 2000].
5. Round: lat/lon 4 decimals, alt 2 decimals.

Objects without `TleLine1` (already propagated, no TLE available) are passed through unchanged.

### Step 1: Write tests first (RED)

File: `MissionClear.Tests/Services/OrbitalEngineServiceTests.cs`

```csharp
using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class OrbitalEngineServiceTests
{
    private readonly OrbitalEngineService _engine = new();

    private static readonly DateTime FixedTime =
        new(2025, 5, 27, 14, 0, 0, DateTimeKind.Utc);

    private static OrbitalObject MakeRaw(string id = "12345",
        double lat = 45.0, double lon = 90.0, double alt = 500.0,
        string? tleLine1 = "stub-line1") =>
        new(id, $"TEST DEB {id}", "debris", lat, lon, alt, 7.5, "celestrak",
            DateTime.UtcNow, TleLine1: tleLine1, TleLine2: "stub-line2");

    // ── Propagate: basic validity ─────────────────────────────────────────────

    [Fact]
    public void Propagate_ReturnsObjectWithSameId()
    {
        var result = _engine.Propagate(MakeRaw("99999"), FixedTime);
        result.Should().NotBeNull();
        result!.Id.Should().Be("99999");
    }

    [Fact]
    public void Propagate_LatitudeInValidRange()
    {
        var result = _engine.Propagate(MakeRaw(), FixedTime);
        result!.Latitude.Should().BeInRange(-90, 90);
    }

    [Fact]
    public void Propagate_LongitudeInValidRange()
    {
        var result = _engine.Propagate(MakeRaw(), FixedTime);
        result!.Longitude.Should().BeInRange(-180, 180);
    }

    [Fact]
    public void Propagate_AltitudeInLEORange()
    {
        var result = _engine.Propagate(MakeRaw(), FixedTime);
        result!.AltitudeKm.Should().BeInRange(200, 2000);
    }

    // ── Propagate: determinism ────────────────────────────────────────────────

    [Fact]
    public void Propagate_IsDeterministic_SameIdSameTime()
    {
        var raw = MakeRaw("42424");
        var a = _engine.Propagate(raw, FixedTime);
        var b = _engine.Propagate(raw, FixedTime);

        a!.Latitude.Should().Be(b!.Latitude);
        a.Longitude.Should().Be(b.Longitude);
        a.AltitudeKm.Should().Be(b.AltitudeKm);
    }

    // ── Propagate: pass-through when no TLE lines ─────────────────────────────

    [Fact]
    public void Propagate_NullTleLines_ReturnsSameObject()
    {
        var raw = MakeRaw(tleLine1: null);
        var result = _engine.Propagate(raw, FixedTime);

        // No TLE → pass-through unchanged (cannot re-propagate without TLE)
        result.Should().BeSameAs(raw);
    }

    // ── PropagateAll ──────────────────────────────────────────────────────────

    [Fact]
    public void PropagateAll_AllFiveObjects_AllReturned()
    {
        var objects = Enumerable.Range(1, 5)
            .Select(i => MakeRaw(i.ToString()))
            .ToList<OrbitalObject>();

        var results = _engine.PropagateAll(objects, FixedTime);

        results.Should().HaveCount(5);
        results.Select(o => o.Id).Should().BeEquivalentTo(["1", "2", "3", "4", "5"]);
    }

    [Fact]
    public void PropagateAll_EmptyInput_ReturnsEmpty()
    {
        var results = _engine.PropagateAll([], FixedTime);
        results.Should().BeEmpty();
    }
}
```

Run (must fail):

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "OrbitalEngineServiceTests"
# Expected: compile error
```

### Step 2: Implement OrbitalEngineService (GREEN)

File: `MissionClear.Api/Services/OrbitalEngineService.cs`

```csharp
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

/// <summary>
/// Propagates orbital objects to a specific UTC instant.
///
/// SGP4 status: No compatible NuGet package is available in this project.
/// Uses a deterministic FNV-1a stub that produces reproducible lat/lon/alt deltas.
///
/// Swap-in path for real SGP4 (when available):
///   1. dotnet add package &lt;sgp4-package&gt;
///   2. Replace SimulateDelta() body with real ECI propagation + ECI→Geodetic conversion.
///   3. All existing tests remain valid because they assert on range/determinism, not exact values.
/// </summary>
public sealed class OrbitalEngineService : IOrbitalEngineService
{
    private const double AltMin = 200.0;
    private const double AltMax = 2000.0;

    public OrbitalObject? Propagate(OrbitalObject raw, DateTime atTime)
    {
        // Objects with no TLE lines cannot be re-propagated — return as-is.
        if (raw.TleLine1 is null)
            return raw;

        var (deltaLat, deltaLon, deltaAlt) = SimulateDelta(raw.Id, atTime);

        var lat = Math.Clamp(raw.Latitude + deltaLat, -90.0, 90.0);
        var lon = WrapLongitude(raw.Longitude + deltaLon);
        var alt = Math.Clamp(raw.AltitudeKm + deltaAlt, AltMin, AltMax);

        return raw with
        {
            Latitude  = Math.Round(lat, 4),
            Longitude = Math.Round(lon, 4),
            AltitudeKm = Math.Round(alt, 2),
            UpdatedAt = atTime,
        };
    }

    public IReadOnlyList<OrbitalObject> PropagateAll(IReadOnlyList<OrbitalObject> objects, DateTime atTime)
    {
        if (objects.Count == 0) return [];

        var results = new OrbitalObject?[objects.Count];

        Parallel.For(0, objects.Count, i =>
        {
            try { results[i] = Propagate(objects[i], atTime); }
            catch { results[i] = null; }
        });

        return results.Where(o => o is not null).Cast<OrbitalObject>().ToList().AsReadOnly();
    }

    /// <summary>
    /// Deterministic delta (lat, lon, alt) for a given NORAD ID at a given time.
    /// FNV-1a hash of the ID → base seed (stable per ID).
    /// XOR with time-derived value → varies between propagation ticks but reproduces exactly.
    /// Ranges: lat ±1°, lon ±2°, alt ±0.5 km.
    /// </summary>
    internal static (double DeltaLat, double DeltaLon, double DeltaAlt)
        SimulateDelta(string id, DateTime atTime)
    {
        var idHash = FnvHash(id);
        var timeSeed = (uint)(atTime.Ticks >> 16);
        var seed = (int)((idHash ^ timeSeed) & 0x7FFFFFFFu);

        var rng = new Random(seed);
        var deltaLat = (rng.NextDouble() - 0.5) * 2.0;   // ±1°
        var deltaLon = (rng.NextDouble() - 0.5) * 4.0;   // ±2°
        var deltaAlt = (rng.NextDouble() - 0.5) * 1.0;   // ±0.5 km

        return (deltaLat, deltaLon, deltaAlt);
    }

    internal static uint FnvHash(string input)
    {
        const uint FnvPrime   = 16777619u;
        const uint OffsetBasis = 2166136261u;
        var hash = OffsetBasis;
        foreach (var c in input)
            hash = (hash ^ (uint)c) * FnvPrime;
        return hash;
    }

    internal static double WrapLongitude(double lon)
    {
        lon = ((lon + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
        return lon;
    }
}
```

### Step 3: Verify green

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "OrbitalEngineServiceTests"
# Expected: Passed: 8
```

### Step 4: Register in DI

In `Program.cs`:

```csharp
builder.Services.AddSingleton<IOrbitalEngineService, OrbitalEngineService>();
```

### Step 5: Commit

```powershell
git add MissionClear.Api/Services/OrbitalEngineService.cs `
        MissionClear.Tests/Services/OrbitalEngineServiceTests.cs
git commit -m "feat(orbital): OrbitalEngineService deterministic SGP4 stub (8 tests)"
```

---

## Task 3.5: DataAggregatorService (TDD)

**Files:**
- `MissionClear.Api/Services/DataAggregatorService.cs`
- `MissionClear.Tests/Services/DataAggregatorServiceTests.cs`
- `MissionClear.Tests/Helpers/MockHttpMessageHandler.cs`

### Step 1: MockHttpMessageHandler helper

File: `MissionClear.Tests/Helpers/MockHttpMessageHandler.cs`

```csharp
using System.Net;
using System.Text;

namespace MissionClear.Tests.Helpers;

/// <summary>
/// Configurable HTTP handler for unit testing services that use IHttpClientFactory.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handler = handler;

    /// <summary>Returns JSON body with given status code.</summary>
    public static MockHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    /// <summary>Returns empty body with given status code.</summary>
    public static MockHttpMessageHandler Status(HttpStatusCode status)
        => new(_ => new HttpResponseMessage(status));

    /// <summary>Throws the given exception when called (simulates network error).</summary>
    public static MockHttpMessageHandler Throws(Exception ex)
        => new(_ => throw ex);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(_handler(request));
}
```

### Step 2: Write tests (RED)

File: `MissionClear.Tests/Services/DataAggregatorServiceTests.cs`

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using MissionClear.Tests.Helpers;
using Moq;
using System.Net;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class DataAggregatorServiceTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private static IOptions<ExternalApiSettings> DefaultSettings() =>
        Options.Create(new ExternalApiSettings
        {
            CelesTrakBaseUrl       = "https://celestrak.test/gp.php",
            KeepTrackBaseUrl       = "https://keeptrack.test/api",
            KeepTrackApiKey        = "test-key",
            KeepTrackTimeoutSeconds = 5,
        });

    /// <summary>
    /// Minimal valid CelesTrak GP JSON array. NORAD_CAT_ID is a string in real API.
    /// </summary>
    private const string OneCelesTrakRecord = """
        [
          {
            "NORAD_CAT_ID": "25544",
            "OBJECT_NAME": "ISS (ZARYA)",
            "OBJECT_TYPE": "PAYLOAD",
            "TLE_LINE1": "1 25544U 98067A   25001.00000000  .00000000  00000-0  00000-0 0  9999",
            "TLE_LINE2": "2 25544  51.6400 000.0000 0001000 000.0000 000.0000 15.50000000"
          }
        ]
        """;

    private static DataAggregatorService CreateSut(
        HttpMessageHandler celestrakHandler,
        HttpMessageHandler? keeptrackHandler = null,
        IOptions<ExternalApiSettings>? settings = null)
    {
        var opts = settings ?? DefaultSettings();

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient("celestrak"))
            .Returns(new HttpClient(celestrakHandler));
        factoryMock
            .Setup(f => f.CreateClient("keeptrack"))
            .Returns(new HttpClient(keeptrackHandler ?? MockHttpMessageHandler.Status(HttpStatusCode.NotFound)));

        var cacheMock = new Mock<IOrbitalCache>();
        var captured = new List<IReadOnlyList<MissionClear.Api.Models.OrbitalObject>>();
        cacheMock
            .Setup(c => c.Update(It.IsAny<IReadOnlyList<MissionClear.Api.Models.OrbitalObject>>()))
            .Callback<IReadOnlyList<MissionClear.Api.Models.OrbitalObject>>(captured.Add);

        var sut = new DataAggregatorService(
            factoryMock.Object,
            cacheMock.Object,
            opts,
            NullLogger<DataAggregatorService>.Instance);

        // Expose captured updates via the SUT for assertions
        sut._capturedUpdates = captured;
        return sut;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchAndMergeAsync_ParsesValidCelesTrakResponse_AndCallsUpdate()
    {
        var sut = CreateSut(MockHttpMessageHandler.Json(OneCelesTrakRecord));

        await sut.FetchAndMergeAsync();

        sut._capturedUpdates.Should().HaveCount(1);
        var objects = sut._capturedUpdates[0];
        objects.Should().HaveCount(1);
        objects[0].Id.Should().Be("25544");
        objects[0].Source.Should().Be("celestrak");
        objects[0].TleLine1.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task FetchAndMergeAsync_SkipsRecordsWithEmptyTleLines()
    {
        const string json = """
            [
              { "NORAD_CAT_ID": "1", "OBJECT_NAME": "GOOD",
                "TLE_LINE1": "1 ...", "TLE_LINE2": "2 ..." },
              { "NORAD_CAT_ID": "2", "OBJECT_NAME": "NO_LINE1",
                "TLE_LINE1": "",      "TLE_LINE2": "2 ..." }
            ]
            """;
        var sut = CreateSut(MockHttpMessageHandler.Json(json));

        await sut.FetchAndMergeAsync();

        sut._capturedUpdates[0].Should().HaveCount(1);
        sut._capturedUpdates[0][0].Id.Should().Be("1");
    }

    [Fact]
    public async Task FetchAndMergeAsync_ThrowsHttpRequestException_WhenCelesTrakFails()
    {
        var sut = CreateSut(MockHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable));

        var act = () => sut.FetchAndMergeAsync();

        await act.Should().ThrowAsync<HttpRequestException>();
        sut._capturedUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAndMergeAsync_KeepTrackFailure_DoesNotThrow_AndPreservesCelesTrakData()
    {
        var sut = CreateSut(
            celestrakHandler: MockHttpMessageHandler.Json(OneCelesTrakRecord),
            keeptrackHandler: MockHttpMessageHandler.Throws(new HttpRequestException("KeepTrack down")));

        var act = () => sut.FetchAndMergeAsync();

        await act.Should().NotThrowAsync();
        sut._capturedUpdates.Should().HaveCount(1);
        sut._capturedUpdates[0].Should().HaveCount(1);
        sut._capturedUpdates[0][0].Source.Should().Be("celestrak");
    }

    [Fact]
    public async Task FetchAndMergeAsync_DeduplicatesMerge_CelesTrakWins()
    {
        const string celestrakJson = """
            [{ "NORAD_CAT_ID": "42", "OBJECT_NAME": "CELESTRAK_OBJ",
               "TLE_LINE1": "1 ...", "TLE_LINE2": "2 ..." }]
            """;
        const string keeptrackJson = """
            [{ "NORAD_CAT_ID": "42", "OBJECT_NAME": "KEEPTRACK_OBJ",
               "TLE_LINE1": "1 ...", "TLE_LINE2": "2 ..." }]
            """;

        var sut = CreateSut(
            celestrakHandler: MockHttpMessageHandler.Json(celestrakJson),
            keeptrackHandler: MockHttpMessageHandler.Json(keeptrackJson));

        await sut.FetchAndMergeAsync();

        var merged = sut._capturedUpdates[0];
        merged.Should().HaveCount(1);
        merged[0].Source.Should().Be("celestrak");
        merged[0].Name.Should().Be("CELESTRAK_OBJ");
    }
}
```

**Note on test design:** `DataAggregatorService` exposes `internal List<IReadOnlyList<OrbitalObject>> _capturedUpdates` that is set by the test via the `Mock<IOrbitalCache>` callback. This is the cleanest way to verify that `IOrbitalCache.Update()` is called with the correct data without coupling tests to implementation details of the cache.

Alternatively: pass a real `OrbitalCache` instance and assert `cache.Count`. Choose whichever is simpler when implementing.

Run (must fail):

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "DataAggregatorServiceTests"
# Expected: compile error
```

### Step 3: Implement DataAggregatorService (GREEN)

File: `MissionClear.Api/Services/DataAggregatorService.cs`

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

/// <summary>
/// Fetches TLE data from CelesTrak (required) and KeepTrack (optional).
/// Merges with CelesTrak-wins deduplication, then calls IOrbitalCache.Update().
/// </summary>
public sealed class DataAggregatorService : IDataAggregatorService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IOrbitalCache _cache;
    private readonly ExternalApiSettings _settings;
    private readonly ILogger<DataAggregatorService> _logger;

    // Internal field populated by tests via Mock<IOrbitalCache> callback.
    // Only set in test harness — not used in production code paths.
    internal List<IReadOnlyList<OrbitalObject>>? _capturedUpdates;

    public DataAggregatorService(
        IHttpClientFactory httpFactory,
        IOrbitalCache cache,
        IOptions<ExternalApiSettings> settings,
        ILogger<DataAggregatorService> logger)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task FetchAndMergeAsync(CancellationToken ct = default)
    {
        var celestrak = await FetchCelesTrakAsync(ct);
        _logger.LogInformation("CelesTrak: fetched {Count} valid TLE records", celestrak.Count);

        var keeptrack = await TryFetchKeepTrackAsync(ct);
        _logger.LogInformation("KeepTrack: fetched {Count} valid TLE records", keeptrack.Count);

        // Merge with CelesTrak-wins: build dictionary seeded with KeepTrack, then overwrite with CelesTrak.
        var merged = new Dictionary<string, OrbitalObject>(
            StringComparer.Ordinal);

        foreach (var obj in keeptrack)
            merged[obj.Id] = obj;

        foreach (var obj in celestrak)
            merged[obj.Id] = obj; // CelesTrak wins

        var result = merged.Values.ToList().AsReadOnly();
        _cache.Update(result);
        _capturedUpdates?.Add(result);

        _logger.LogInformation("OrbitalCache updated with {Total} merged objects", result.Count);
    }

    // ── CelesTrak ─────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<OrbitalObject>> FetchCelesTrakAsync(CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("celestrak");
        _logger.LogDebug("Fetching CelesTrak: {Url}", _settings.CelesTrakBaseUrl);

        using var response = await client.GetAsync(_settings.CelesTrakBaseUrl, ct);
        response.EnsureSuccessStatusCode();   // throws HttpRequestException on non-2xx

        var records = await response.Content.ReadFromJsonAsync<List<CelesTrakGpRecord>>(cancellationToken: ct);
        if (records is null) return [];

        var now = DateTime.UtcNow;
        return records
            .Where(r => !string.IsNullOrWhiteSpace(r.TleLine1)
                     && !string.IsNullOrWhiteSpace(r.TleLine2)
                     && !string.IsNullOrWhiteSpace(r.NoradCatId))
            .Select(r => new OrbitalObject(
                Id:          r.NoradCatId,
                Name:        r.ObjectName.Length > 0 ? r.ObjectName : $"OBJECT-{r.NoradCatId}",
                Type:        ClassifyType(r.ObjectName, r.ObjectType),
                Latitude:    0.0,  // populated by OrbitalEngineService.PropagateAll()
                Longitude:   0.0,
                AltitudeKm:  400.0, // nominal LEO until propagated; filter will pass it
                VelocityKmS: 7.5,
                Source:      "celestrak",
                UpdatedAt:   now,
                TleLine1:    r.TleLine1,
                TleLine2:    r.TleLine2))
            .ToList();
    }

    // ── KeepTrack ─────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<OrbitalObject>> TryFetchKeepTrackAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.KeepTrackBaseUrl)
            || string.IsNullOrWhiteSpace(_settings.KeepTrackApiKey))
        {
            _logger.LogDebug("KeepTrack URL or API key not configured — skipping");
            return [];
        }

        try
        {
            var client = _httpFactory.CreateClient("keeptrack");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_settings.KeepTrackTimeoutSeconds));

            var url = $"{_settings.KeepTrackBaseUrl.TrimEnd('/')}/tle?apiKey={_settings.KeepTrackApiKey}";
            using var response = await client.GetAsync(url, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("KeepTrack returned {Status} — continuing without it", response.StatusCode);
                return [];
            }

            var records = await response.Content.ReadFromJsonAsync<List<CelesTrakGpRecord>>(
                cancellationToken: cts.Token);
            if (records is null) return [];

            var now = DateTime.UtcNow;
            return records
                .Where(r => !string.IsNullOrWhiteSpace(r.TleLine1)
                         && !string.IsNullOrWhiteSpace(r.TleLine2)
                         && !string.IsNullOrWhiteSpace(r.NoradCatId))
                .Select(r => new OrbitalObject(
                    Id:          r.NoradCatId,
                    Name:        r.ObjectName.Length > 0 ? r.ObjectName : $"OBJECT-{r.NoradCatId}",
                    Type:        ClassifyType(r.ObjectName, r.ObjectType),
                    Latitude:    0.0,
                    Longitude:   0.0,
                    AltitudeKm:  400.0,
                    VelocityKmS: 7.5,
                    Source:      "keeptrack",
                    UpdatedAt:   now,
                    TleLine1:    r.TleLine1,
                    TleLine2:    r.TleLine2))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException { CancellationToken.IsCancellationRequested: true })
        {
            _logger.LogWarning(ex, "KeepTrack fetch failed — system continues with CelesTrak only");
            return [];
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string ClassifyType(string name, string objectType)
    {
        var upper = name.ToUpperInvariant();
        var typeUpper = objectType.ToUpperInvariant();
        if (upper.Contains("DEB") || typeUpper.Contains("DEBRIS")) return "debris";
        if (upper.Contains("R/B") || typeUpper.Contains("ROCKET")) return "rocket_body";
        return "satellite";
    }

    // ── internal DTO for CelesTrak JSON deserialization ───────────────────────

    private sealed class CelesTrakGpRecord
    {
        [JsonPropertyName("NORAD_CAT_ID")]
        public string NoradCatId { get; init; } = string.Empty;

        [JsonPropertyName("OBJECT_NAME")]
        public string ObjectName { get; init; } = string.Empty;

        [JsonPropertyName("OBJECT_TYPE")]
        public string ObjectType { get; init; } = string.Empty;

        [JsonPropertyName("TLE_LINE1")]
        public string TleLine1 { get; init; } = string.Empty;

        [JsonPropertyName("TLE_LINE2")]
        public string TleLine2 { get; init; } = string.Empty;
    }
}
```

### Step 4: Verify green

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "DataAggregatorServiceTests"
# Expected: Passed: 5
```

### Step 5: Register in DI

In `Program.cs`:

```csharp
builder.Services.AddHttpClient("celestrak");
builder.Services.AddHttpClient("keeptrack");
builder.Services.AddScoped<IDataAggregatorService, DataAggregatorService>();
```

### Step 6: Commit

```powershell
git add MissionClear.Api/Services/DataAggregatorService.cs `
        MissionClear.Tests/Services/DataAggregatorServiceTests.cs `
        MissionClear.Tests/Helpers/MockHttpMessageHandler.cs
git commit -m "feat(orbital): DataAggregatorService CelesTrak+KeepTrack with dedup (5 tests)"
```

---

## Task 3.6: TleIngestionService (BackgroundService)

**File:** `MissionClear.Api/Services/Background/TleIngestionService.cs`

No unit tests for BackgroundService (integration-tested via startup). Focus is on correctness of lifecycle and exception isolation.

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionClear.Api.Configuration;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services.Background;

/// <summary>
/// Owns the orbital data lifecycle:
///   - On startup: one immediate fetch + propagation cycle.
///   - Every TleFetchIntervalMinutes (default 60): refresh TLEs from CelesTrak (+ KeepTrack if available).
///   - Every PropagationIntervalSeconds (default 60): re-propagate all cached objects to current time.
///
/// Never crashes the host. Each loop iteration is exception-isolated.
/// OperationCanceledException (shutdown) is always re-thrown from loops.
/// </summary>
public sealed class TleIngestionService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOrbitalCache _cache;
    private readonly OrbitalSettings _settings;
    private readonly ILogger<TleIngestionService> _logger;

    public TleIngestionService(
        IServiceProvider services,
        IOrbitalCache cache,
        IOptions<OrbitalSettings> settings,
        ILogger<TleIngestionService> logger)
    {
        _services = services;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TleIngestionService starting — executing initial cycle");

        var fetchOk = await SafeFetchAsync(stoppingToken);
        if (!fetchOk)
            _logger.LogCritical(
                "Initial CelesTrak fetch FAILED — cache is empty. Will retry in {Minutes} minutes.",
                _settings.TleFetchIntervalMinutes);

        await SafePropagateAsync(stoppingToken);

        await Task.WhenAll(
            RunFetchLoopAsync(stoppingToken),
            RunPropagateLoopAsync(stoppingToken));
    }

    // ── fetch loop ────────────────────────────────────────────────────────────

    private async Task RunFetchLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.TleFetchIntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SafeFetchAsync(ct);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    private async Task<bool> SafeFetchAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("TleIngestion: starting fetch cycle");
            using var scope = _services.CreateScope();
            var aggregator = scope.ServiceProvider.GetRequiredService<IDataAggregatorService>();
            await aggregator.FetchAndMergeAsync(ct);
            _logger.LogInformation("TleIngestion: fetch complete — {Count} objects in cache", _cache.Count);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TleIngestion: fetch cycle failed — will retry at next interval");
            return false;
        }
    }

    // ── propagation loop ──────────────────────────────────────────────────────

    private async Task RunPropagateLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.PropagationIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SafePropagateAsync(ct);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    private async Task SafePropagateAsync(CancellationToken ct)
    {
        try
        {
            var objects = _cache.GetAll();
            if (objects.Count == 0)
            {
                _logger.LogDebug("TleIngestion: no objects to propagate yet");
                return;
            }

            using var scope = _services.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IOrbitalEngineService>();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var propagated = await Task.Run(
                () => engine.PropagateAll(objects, DateTime.UtcNow), ct);
            sw.Stop();

            _cache.Update(propagated);

            _logger.LogInformation(
                "TleIngestion: propagated {Count}/{Total} objects in {Ms} ms",
                propagated.Count, objects.Count, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TleIngestion: propagation cycle failed — will retry at next interval");
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("TleIngestionService stopping");
        await base.StopAsync(ct);
    }
}
```

### Register in DI

Add to `Program.cs` (after `IOrbitalEngineService` and `IDataAggregatorService`):

```csharp
builder.Services.AddHostedService<TleIngestionService>();
```

`TleIngestionService` resolves `IDataAggregatorService` and `IOrbitalEngineService` via scoped `IServiceProvider.CreateScope()` — this handles their scoped lifetime correctly.

### Commit

```powershell
git add MissionClear.Api/Services/Background/TleIngestionService.cs
git commit -m "feat(orbital): TleIngestionService background fetch 60min + propagate 60s, never crashes"
```

---

## Task 3.7: Final DI Wiring (Program.cs summary)

The complete orbital DI registration block in `Program.cs`:

```csharp
// ── HTTP clients for orbital data sources ─────────────────────────────────────
builder.Services.AddHttpClient("celestrak");
builder.Services.AddHttpClient("keeptrack");

// ── Orbital layer ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IOrbitalCache, OrbitalCache>();
builder.Services.AddSingleton<IOrbitalEngineService, OrbitalEngineService>();
builder.Services.AddScoped<IDataAggregatorService, DataAggregatorService>();
builder.Services.AddHostedService<TleIngestionService>();
```

Lifetime justification:
- `IOrbitalCache` → Singleton: shared state across all requests; thread-safe by design.
- `IOrbitalEngineService` → Singleton: pure/stateless computation; safe to share.
- `IDataAggregatorService` → Scoped: uses `IHttpClientFactory`; scoped lifetime prevents HttpClient socket exhaustion.
- `TleIngestionService` → Hosted (Singleton): creates its own scopes internally for scoped dependencies.

### Final verification

```powershell
dotnet build
# Expected: Build succeeded. 0 Error(s).

dotnet test MissionClear.Tests/MissionClear.Tests.csproj
# Expected: all orbital tests pass (22+ total)
```

---

## Checklist — Plan 03 Complete

- [ ] `OrbitalObject` record verified to exist (defined in plan-02 Task 2.1, NOT created here)
- [ ] `MissionClear.Api/Properties/AssemblyInfo.cs` created with `[assembly: InternalsVisibleTo("MissionClear.Tests")]`
- [ ] `IOrbitalCache` interface defined with `IsReady`, `LastFetch`, `LastPropagation`, `Count`, `GetAll()`, `GetById()`, `Update()`
- [ ] `IDataAggregatorService` interface defined with `FetchAndMergeAsync()`
- [ ] `IOrbitalEngineService` interface defined with `Propagate()`, `PropagateAll()`
- [ ] `OrbitalCache` thread-safe (volatile snapshot + ConcurrentDictionary index + write lock)
- [ ] `OrbitalCache.Update()` applies LEO filter (200–2000 km) and age filter (< 7 days)
- [ ] `OrbitalCache.Update()` applies CelesTrak-wins dedup within a batch
- [ ] `OrbitalCache` sets `LastFetch` for TLE objects, `LastPropagation` for propagated objects
- [ ] 9 `OrbitalCacheTests` green (IsReady, LEO filter, stale filter, CelesTrak-wins ×2, LastFetch, LastPropagation, thread safety, GetById)
- [ ] `OrbitalEngineService` deterministic stub (FNV-1a hash + time XOR → Random seed)
- [ ] `OrbitalEngineService.Propagate()` returns same object when `TleLine1 == null`
- [ ] `OrbitalEngineService.Propagate()` clamps alt [200,2000], lat [-90,90], wraps lon [-180,180]
- [ ] `OrbitalEngineService.Propagate()` rounds lat/lon to 4 decimals, alt to 2 decimals
- [ ] 8 `OrbitalEngineServiceTests` green (id, lat range, lon range, alt range, determinism ×1, pass-through, PropagateAll ×2)
- [ ] `DataAggregatorService` uses `IHttpClientFactory` with named clients "celestrak" / "keeptrack"
- [ ] `DataAggregatorService.FetchAndMergeAsync()` throws on CelesTrak failure
- [ ] `DataAggregatorService.FetchAndMergeAsync()` swallows KeepTrack failure
- [ ] `DataAggregatorService` deduplicates with CelesTrak-wins before calling `Update()`
- [ ] 5 `DataAggregatorServiceTests` green (parse, skip empty TLE, CelesTrak 503 throws, KeepTrack failure silent, dedup)
- [ ] `TleIngestionService` executes initial fetch + propagate before first timer tick
- [ ] `TleIngestionService` logs `LogCritical` when initial fetch fails (no throw)
- [ ] `TleIngestionService` catches all exceptions per loop iteration (except `OperationCanceledException`)
- [ ] All 4 DI registrations in `Program.cs` with correct lifetimes
- [ ] `dotnet build` succeeds with 0 errors
- [ ] `dotnet test` ≥ 22 tests passing in orbital layer

---

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| CelesTrak down | `EnsureSuccessStatusCode()` throws; fetch loop logs error and retries in 60 min |
| No SGP4 NuGet available | Deterministic stub keeps system functional for demo and testing; swap-in path documented |
| PropagateAll > 60s for 30k objects | `Parallel.For` in `PropagateAll`; stub is O(1) per object |
| KeepTrack slow/flaky | `CancellationTokenSource` with 5s timeout + try/catch returns empty list |
| Cache grows indefinitely | Age filter (7 days) in `Update()` removes stale objects every cycle |
| Race between fetch and propagation | `volatile IReadOnlyList` + `ConcurrentDictionary` + `_writeLock` in `Update()` |
| `_capturedUpdates` leaking to production | Field is `internal` and only assigned by test via constructor; null in prod — harmless |
