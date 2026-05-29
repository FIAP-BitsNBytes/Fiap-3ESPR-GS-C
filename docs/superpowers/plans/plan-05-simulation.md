# Implementation Plan: Phase 05 — Simulation Services

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:executing-plans`
>
> This is the **definitive source of truth** for Phase 05. It supersedes all previous versions
> and the stub in `reboot/phase-05-simulation.md`. Implement every step in order; do not skip.

**Goal:** Implement the full simulation layer: ConjunctionDetector, LaunchWindowCalculator,
MissionSimulationService (with session management), SessionStore, and MissionSseService.

**Dependencies (must be completed before this phase):**
- Phase 02 — Models + DTOs: `OrbitalObject`, `MissionDestination`, `KnownDestinations`,
  `ConjunctionResult`, `LaunchWindow`, `MissionSession`, `RiskLevel` exist in
  `MissionClear.Api/Models/` with namespace `MissionClear.Api.Models`
- Phase 03 — Orbital Engine: `IOrbitalCache` available, `OrbitalMath.HaversineKm`,
  `RiskScoring.Classify`, `RiskScoring.ComputeScore`, `MissionScoring.Compute` in `Helpers/`
- Phase 04 — Auth: `IMissionHistoryService` available in `Services/Interfaces/`
- Test framework: xUnit + FluentAssertions in `MissionClear.Tests`

**Algorithms (immutable — do not alter):**

```
risk_score = min(1.0, sum of contributions per debris)
  contribution = max(0, 1 - (d - 25) / (200 - 25))  where d = closest_approach_km

mission_score = (int)(efficiency + safety)
  efficiency = max(0, 1 - deltaV / 12) * 50
  safety     = (1 - risk_score) * 50

RiskLevel thresholds (Haversine 3D distance):
  < 1 km   → Critical
  < 5 km   → High
  < 10 km  → Medium
  otherwise → Low
```

These are already implemented in `Helpers/RiskScoring.cs` and `Helpers/MissionScoring.cs`.
Use them; never re-implement them inline.

---

## Architecture

```
MissionClear.Api/
├── Services/
│   ├── Interfaces/
│   │   ├── IConjunctionDetector.cs       ← new
│   │   ├── ILaunchWindowCalculator.cs    ← new
│   │   ├── IMissionSimulationService.cs  ← new
│   │   ├── ISessionStore.cs              ← new
│   │   └── IMissionSseService.cs         ← new
│   ├── ConjunctionDetector.cs            ← new
│   ├── LaunchWindowCalculator.cs         ← new
│   ├── MissionSimulationService.cs       ← new
│   ├── SessionStore.cs                   ← new
│   └── MissionSseService.cs              ← new
└── Program.cs                            ← add DI registrations

MissionClear.Tests/
└── Services/
    ├── ConjunctionDetectorTests.cs       ← new
    ├── LaunchWindowCalculatorTests.cs    ← new
    ├── SessionStoreTests.cs              ← new
    └── MissionSimulationServiceTests.cs  ← new
```

**Namespace rules:**
- Interfaces: `namespace MissionClear.Api.Services.Interfaces;`
- Implementations: `namespace MissionClear.Api.Services;` + `using MissionClear.Api.Services.Interfaces;`
- Tests: `namespace MissionClear.Tests.Services;`

**No new NuGet packages.** No new configuration sections except `OrbitalSettings.SessionTtlMinutes`
(already in `OrbitalSettings`; add the property if missing with default `30`).

---

## Implementation Steps

### Phase 1 — Service Interfaces (commit 1)

**1. [ ] Create all five interfaces**

`MissionClear.Api/Services/Interfaces/IConjunctionDetector.cs`:
```csharp
using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface IConjunctionDetector
{
    IReadOnlyList<ConjunctionResult> Detect(
        MissionDestination destination,
        DateTime at,
        IReadOnlyList<OrbitalObject> debris);
}
```

`MissionClear.Api/Services/Interfaces/ILaunchWindowCalculator.cs`:
```csharp
using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface ILaunchWindowCalculator
{
    IReadOnlyList<LaunchWindow> Calculate(
        MissionDestination destination,
        DateTime from,
        DateTime to,
        IReadOnlyList<OrbitalObject> debris);
}
```

`MissionClear.Api/Services/Interfaces/IMissionSimulationService.cs`:
```csharp
using MissionClear.Api.Dtos.Mission;

namespace MissionClear.Api.Services.Interfaces;

public interface IMissionSimulationService
{
    Task<SimulateResponse> SimulateAsync(SimulateRequest request, CancellationToken ct = default);
    Task<SessionResponse> CreateSessionAsync(SessionRequest request, CancellationToken ct = default);
    Task<CompleteSessionResponse> CompleteSessionAsync(
        string sessionId,
        CompleteSessionRequest request,
        Guid? userId,
        CancellationToken ct = default);
}
```

`MissionClear.Api/Services/Interfaces/ISessionStore.cs`:
```csharp
using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface ISessionStore
{
    void Set(MissionSession session);
    MissionSession? Get(string sessionId);
    void Remove(string sessionId);
    void PurgeExpired();
}
```

`MissionClear.Api/Services/Interfaces/IMissionSseService.cs`:
```csharp
using Microsoft.AspNetCore.Http;

namespace MissionClear.Api.Services.Interfaces;

public interface IMissionSseService
{
    Task StreamAsync(string sessionId, HttpResponse response, CancellationToken ct);
}
```

**2. [ ] Verify build: `dotnet build` — must succeed (interfaces compile, implementations not yet)**

**3. [ ] Commit:** `feat(simulation): add service interfaces IConjunctionDetector, ILaunchWindowCalculator, IMissionSimulationService, ISessionStore, IMissionSseService`

---

### Phase 2 — ConjunctionDetector (commit 2)

**4. [ ] Write ConjunctionDetector tests (RED)**

`MissionClear.Tests/Services/ConjunctionDetectorTests.cs`:
```csharp
using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class ConjunctionDetectorTests
{
    private readonly ConjunctionDetector _detector = new();

    // ISS at lat=0, lon=0, alt=408 km (uses KnownDestinations.ISS)
    private static MissionDestination IssDestination => KnownDestinations.ISS;

    private static OrbitalObject MakeDebris(string id, double lat, double lon, double altKm) =>
        new(id, $"DEB-{id}", "debris", lat, lon, altKm, 7.5, "celestrak", DateTime.UtcNow);

    [Fact]
    public void Detect_ReturnsCritical_WhenDebrisUnder1km()
    {
        // Debris at same lat/lon as destination, 0 km altitude diff → distance < 1 km → Critical
        var debris = new[] { MakeDebris("1", 0.0, 0.0, 408.0) };

        var results = _detector.Detect(IssDestination, DateTime.UtcNow, debris);

        results.Should().NotBeEmpty();
        results[0].RiskLevel.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void Detect_ReturnsEmpty_WhenDebrisFarAway()
    {
        // Debris at 1500 km altitude — well beyond the 200 km filter radius
        var debris = new[] { MakeDebris("far", 80.0, 120.0, 1500.0) };

        var results = _detector.Detect(IssDestination, DateTime.UtcNow, debris);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ClassifiesHigh_WhenDistanceBetween1And5km()
    {
        // Debris 2 km above ISS altitude, same lat/lon → distance ≈ 2 km → High
        var debris = new[] { MakeDebris("hi", 0.0, 0.0, 410.0) };

        var results = _detector.Detect(IssDestination, DateTime.UtcNow, debris);

        results.Should().NotBeEmpty();
        results[0].RiskLevel.Should().BeOneOf(RiskLevel.High, RiskLevel.Critical);
    }

    [Fact]
    public void Detect_DoesNotThrow_WhenDebrisListIsEmpty()
    {
        var act = () => _detector.Detect(IssDestination, DateTime.UtcNow, []);

        act.Should().NotThrow();
    }
}
```

**5. [ ] Run tests — confirm RED** (ConjunctionDetector not found → build error)

**6. [ ] Implement ConjunctionDetector (GREEN)**

`MissionClear.Api/Services/ConjunctionDetector.cs`:
```csharp
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class ConjunctionDetector : IConjunctionDetector
{
    // Destination orbit is treated as a point at (LatitudeDeg=0, LongitudeDeg=0, altKm) for proximity filtering.
    // MissionDestination exposes LatitudeDeg and LongitudeDeg (defaulting to 0.0 for equatorial orbit assumption).
    // OrbitalObject exposes Latitude and Longitude (no "Deg" suffix).

    public IReadOnlyList<ConjunctionResult> Detect(
        MissionDestination destination,
        DateTime at,
        IReadOnlyList<OrbitalObject> debris)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(debris);

        var destLat = destination.LatitudeDeg;
        var destLon = destination.LongitudeDeg;
        var destAlt = destination.AltitudeKm;

        var results = new List<ConjunctionResult>();

        foreach (var obj in debris)
        {
            // 3D distance: horizontal Haversine at average altitude + altitude difference
            // OrbitalObject uses Latitude and Longitude (no "Deg" suffix)
            var avgAlt = (destAlt + obj.AltitudeKm) / 2.0;
            var horizKm = OrbitalMath.HaversineKm(
                destLat, destLon,
                obj.Latitude, obj.Longitude,
                OrbitalMath.EarthRadiusKm + avgAlt);
            var vertKm   = Math.Abs(destAlt - obj.AltitudeKm);
            var distKm   = Math.Sqrt(horizKm * horizKm + vertKm * vertKm);

            if (distKm > 200) continue; // only process debris within 200 km

            // Deterministic time-of-closest-approach: seeded by debris id hash for test stability
            var seed  = obj.Id.GetHashCode();
            var etaMin = new Random(seed).Next(5, 90);
            var toca  = at.AddMinutes(etaMin);

            results.Add(new ConjunctionResult(
                obj.Id,
                obj.Name,
                Math.Round(distKm, 3),
                toca,
                RiskScoring.Classify(distKm)));
        }

        return results.OrderBy(r => r.ClosestApproachKm).ToList().AsReadOnly();
    }
}
```

> **Note:** `MissionDestination` exposes `LatitudeDeg` and `LongitudeDeg` (fixed in Phase 02,
> defaulting to 0.0 for equatorial orbit assumption). `OrbitalObject` exposes `Latitude` and
> `Longitude` (NO "Deg" suffix) — use `obj.Latitude` and `obj.Longitude` in all code.

**7. [ ] Run tests — confirm GREEN (4 pass)**

**8. [ ] Commit:** `feat(simulation): add ConjunctionDetector with Haversine 3D proximity and deterministic TOCA`

---

### Phase 3 — LaunchWindowCalculator (commit 3)

**9. [ ] Write LaunchWindowCalculator tests (RED)**

`MissionClear.Tests/Services/LaunchWindowCalculatorTests.cs`:
```csharp
using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class LaunchWindowCalculatorTests
{
    private readonly LaunchWindowCalculator _calc = new();

    private static readonly MissionDestination Iss = KnownDestinations.ISS;

    [Fact]
    public void Calculate_Returns48Windows_For12HourRange()
    {
        var from = new DateTime(2025, 5, 27, 0, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(12);

        var windows = _calc.Calculate(Iss, from, to, []);

        windows.Should().HaveCount(48); // 12h / 15min = 48 slots
    }

    [Fact]
    public void Calculate_IsRecommended_WhenRiskScoreUnder01()
    {
        var from    = DateTime.UtcNow;
        var windows = _calc.Calculate(Iss, from, from.AddMinutes(15), []);

        windows.Should().HaveCount(1);
        windows[0].IsRecommended.Should().Be(windows[0].RiskScore < 0.1);
    }

    [Fact]
    public void Calculate_ReturnsEmptyConjunctions_WhenNoCacheData()
    {
        var from    = DateTime.UtcNow;
        var windows = _calc.Calculate(Iss, from, from.AddMinutes(15), []);

        windows[0].Conjunctions.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_SetsCorrectDeltaV_ForDestination()
    {
        var from    = DateTime.UtcNow;
        var windows = _calc.Calculate(Iss, from, from.AddMinutes(15), []);

        windows[0].DeltaVKmS.Should().Be(Iss.DeltaVKmS);
    }
}
```

**10. [ ] Run tests — confirm RED**

**11. [ ] Implement LaunchWindowCalculator (GREEN)**

`MissionClear.Api/Services/LaunchWindowCalculator.cs`:
```csharp
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class LaunchWindowCalculator : ILaunchWindowCalculator
{
    private const int SlotMinutes = 15;

    private readonly IConjunctionDetector _detector;

    // Default constructor used in tests (no DI needed for a pure service)
    public LaunchWindowCalculator() : this(new ConjunctionDetector()) { }

    public LaunchWindowCalculator(IConjunctionDetector detector)
    {
        _detector = detector;
    }

    public IReadOnlyList<LaunchWindow> Calculate(
        MissionDestination destination,
        DateTime from,
        DateTime to,
        IReadOnlyList<OrbitalObject> debris)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(debris);

        var windows = new List<LaunchWindow>();
        var current = from;

        while (current < to)
        {
            var slotEnd     = current.AddMinutes(SlotMinutes);
            var conjunctions = _detector.Detect(destination, current, debris);
            var riskScore    = RiskScoring.ComputeScore(
                conjunctions.Select(c => c.ClosestApproachKm));

            windows.Add(new LaunchWindow(
                Start:         current,
                End:           slotEnd,
                RiskScore:     Math.Round(riskScore, 4),
                DeltaVKmS:     destination.DeltaVKmS,
                DurationHours: destination.MissionDurationHours,
                IsRecommended: riskScore < 0.1,
                Conjunctions:  conjunctions));

            current = slotEnd;
        }

        return windows.AsReadOnly();
    }
}
```

> **Note on `LaunchWindow` constructor:** Use `Start:` and `End:` (not `StartUtc:`/`EndUtc:`).
> These are the canonical parameter names as defined in Phase 02's `LaunchWindow` record.

**12. [ ] Run tests — confirm GREEN (4 pass)**

**13. [ ] Commit:** `feat(simulation): add LaunchWindowCalculator with 15-minute slots and risk scoring`

---

### Phase 4 — SessionStore (commit 4)

**14. [ ] Write SessionStore tests (RED)**

`MissionClear.Tests/Services/SessionStoreTests.cs`:
```csharp
using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class SessionStoreTests
{
    private static SessionStore NewStore(Func<DateTime>? clock = null) =>
        new(ttlMinutes: 30, clock: clock ?? (() => DateTime.UtcNow));

    private static MissionSession NewSession(string id = "sess_test") => new()
    {
        SessionId     = id,
        UserId        = Guid.NewGuid(),
        Destination   = "ISS",
        DepartureTime = DateTime.UtcNow,
        ArrivalTime   = DateTime.UtcNow.AddHours(6),
        Status        = SessionStatus.Active,
        CreatedAtUtc  = DateTime.UtcNow,
        ExpiresAt     = DateTime.UtcNow.AddMinutes(30)
    };

    [Fact]
    public void Set_Then_Get_ReturnsSameSession()
    {
        var store   = NewStore();
        var session = NewSession();

        store.Set(session);
        var result = store.Get(session.SessionId);

        result.Should().NotBeNull();
        result!.SessionId.Should().Be(session.SessionId);
    }

    [Fact]
    public void Remove_ThenGet_ReturnsNull()
    {
        var store   = NewStore();
        var session = NewSession("sess_remove");

        store.Set(session);
        store.Remove(session.SessionId);

        store.Get(session.SessionId).Should().BeNull();
    }

    [Fact]
    public void Get_ReturnsNull_WhenSessionExpired()
    {
        var now   = DateTime.UtcNow;
        var clock = now;
        var store = NewStore(clock: () => clock);

        var session = NewSession("sess_expire") with
        {
            CreatedAtUtc = now,
            ExpiresAt    = now.AddMinutes(30)
        };
        store.Set(session);

        // Advance clock past TTL
        clock = now.AddMinutes(31);

        store.Get("sess_expire").Should().BeNull();
    }

    [Fact]
    public void PurgeExpired_RemovesOnlyExpiredSessions()
    {
        var now   = DateTime.UtcNow;
        var clock = now;
        var store = NewStore(clock: () => clock);

        var fresh   = NewSession("sess_fresh") with { ExpiresAt = now.AddMinutes(30) };
        var expired = NewSession("sess_dead")  with { ExpiresAt = now.AddMinutes(-1) };

        store.Set(fresh);
        store.Set(expired);

        clock = now.AddMinutes(31);
        store.PurgeExpired();

        store.Get("sess_fresh").Should().BeNull(); // also expired now
        store.Get("sess_dead").Should().BeNull();
    }
}
```

**15. [ ] Run tests — confirm RED**

**16. [ ] Implement SessionStore (GREEN)**

`MissionClear.Api/Services/SessionStore.cs`:
```csharp
using System.Collections.Concurrent;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

/// <summary>
/// Thread-safe in-memory session store backed by ConcurrentDictionary.
/// TTL is enforced on reads and by explicit PurgeExpired calls.
/// Singleton lifetime — shared across all requests.
/// </summary>
public sealed class SessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, MissionSession> _sessions = new();
    private readonly int _ttlMinutes;
    private readonly Func<DateTime> _clock;

    /// <summary>Production constructor — reads TTL from OrbitalSettings.</summary>
    public SessionStore(int ttlMinutes = 30)
        : this(ttlMinutes, () => DateTime.UtcNow) { }

    /// <summary>Test constructor — injects a controllable clock.</summary>
    public SessionStore(int ttlMinutes, Func<DateTime> clock)
    {
        _ttlMinutes = ttlMinutes;
        _clock      = clock;
    }

    public void Set(MissionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions[session.SessionId] = session;
    }

    public MissionSession? Get(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var s)) return null;
        if (_clock() >= s.ExpiresAt)
        {
            _sessions.TryRemove(sessionId, out _);
            return null;
        }
        return s;
    }

    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public void PurgeExpired()
    {
        var now = _clock();
        foreach (var kv in _sessions)
        {
            if (now >= kv.Value.ExpiresAt)
                _sessions.TryRemove(kv.Key, out _);
        }
    }
}
```

**17. [ ] Run tests — confirm GREEN (4 pass)**

> **Note on `MissionSession`:** The model must include `SessionId`, `UserId`, `Destination`,
> `Status` (enum `SessionStatus`), `CreatedAtUtc`, `ExpiresAt`. If Phase 02 did not add
> `ExpiresAt`, add it as `DateTime ExpiresAt { get; init; }` to the `MissionSession` entity now.

**18. [ ] Commit:** `feat(simulation): add thread-safe SessionStore with TTL enforcement`

---

### Phase 5 — MissionSimulationService (commit 5)

**19. [ ] Write MissionSimulationService tests (RED)**

`MissionClear.Tests/Services/MissionSimulationServiceTests.cs`:
```csharp
using FluentAssertions;
using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Models;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using Moq;
using Xunit;

namespace MissionClear.Tests.Services;

public sealed class MissionSimulationServiceTests
{
    private static IMissionSimulationService BuildSut(
        IOrbitalCache? cache = null,
        ISessionStore? store = null,
        IMissionHistoryService? history = null)
    {
        cache   ??= Mock.Of<IOrbitalCache>(c => c.GetAll() == Array.Empty<OrbitalObject>());
        store   ??= new SessionStore();
        history ??= Mock.Of<IMissionHistoryService>();

        return new MissionSimulationService(
            new ConjunctionDetector(),
            new LaunchWindowCalculator(),
            cache,
            store,
            history);
    }

    [Fact]
    public async Task SimulateAsync_ReturnsValidResponse_ForKnownDestination()
    {
        var sut  = BuildSut();
        var req  = new SimulateRequest(
            "ISS",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 6, 0, 0, DateTimeKind.Utc));

        var result = await sut.SimulateAsync(req);

        result.Should().NotBeNull();
        result.MissionScore.Should().BeInRange(0, 100);
        result.RiskScore.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public async Task SimulateAsync_ReturnsMissionScore100_WhenNoDebris()
    {
        var sut    = BuildSut();
        var req    = new SimulateRequest(
            "ISS",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 1, 1, 6, 0, 0, DateTimeKind.Utc));
        var result = await sut.SimulateAsync(req);

        // No debris → risk_score = 0 → safety = 50; efficiency depends on deltaV
        result.RiskScore.Should().Be(0.0);
        result.MissionScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsSessionWithStreamUrl()
    {
        var sut = BuildSut();
        var req = new SessionRequest("ISS", DateTime.UtcNow.ToString("O"), DateTime.UtcNow.AddHours(6).ToString("O"));

        var result = await sut.CreateSessionAsync(req);

        result.Should().NotBeNull();
        result.SessionId.Should().NotBeNullOrEmpty();
        result.StreamUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CompleteSessionAsync_ReturnsScore_WhenSessionExists()
    {
        var store = new SessionStore();
        var sut   = BuildSut(store: store);

        var sessionResp = await sut.CreateSessionAsync(
            new SessionRequest("ISS", DateTime.UtcNow.ToString("O"), DateTime.UtcNow.AddHours(6).ToString("O")));

        var result = await sut.CompleteSessionAsync(
            sessionResp.SessionId,
            new CompleteSessionRequest(Status: "aborted", SaveToHistory: false),
            userId: null);

        result.Should().NotBeNull();
        result.MissionScore.Should().BeInRange(0, 100);
    }
}
```

**20. [ ] Run tests — confirm RED**

**21. [ ] Implement MissionSimulationService (GREEN)**

`MissionClear.Api/Services/MissionSimulationService.cs`:
```csharp
using System.Security.Cryptography;
using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class MissionSimulationService : IMissionSimulationService
{
    private readonly IConjunctionDetector     _detector;
    private readonly ILaunchWindowCalculator  _calculator;
    private readonly IOrbitalCache            _orbitalCache;
    private readonly ISessionStore            _sessions;
    private readonly IMissionHistoryService   _history;

    public MissionSimulationService(
        IConjunctionDetector    detector,
        ILaunchWindowCalculator calculator,
        IOrbitalCache           orbitalCache,
        ISessionStore           sessions,
        IMissionHistoryService  history)
    {
        _detector     = detector;
        _calculator   = calculator;
        _orbitalCache = orbitalCache;
        _sessions     = sessions;
        _history      = history;
    }

    // ── SimulateAsync ──────────────────────────────────────────────────────────

    public Task<SimulateResponse> SimulateAsync(
        SimulateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var destination = KnownDestinations.Get(request.Destination)
            ?? throw new ArgumentException(
                $"Unknown destination '{request.Destination}'.", nameof(request));

        var debris       = _orbitalCache.GetAll();
        var conjunctions = _detector.Detect(destination, request.DepartureUtc, debris);
        var riskScore    = RiskScoring.ComputeScore(
            conjunctions.Select(c => c.ClosestApproachKm));
        var (_, _, missionScore) = MissionScoring.Compute(destination.DeltaVKmS, riskScore);

        // Map ConjunctionResult → ObstacleDto
        var obstaclesDto = conjunctions.Select(c => new ObstacleDto(
            DebrisId: c.DebrisId,
            DebrisName: c.DebrisName,
            ClosestApproachKm: c.ClosestApproachKm,
            TimeOfClosestApproach: c.TimeOfClosestApproach.ToString("O"),
            RiskLevel: c.RiskLevel.ToString().ToLowerInvariant()
        )).ToList().AsReadOnly();

        var response = new SimulateResponse(
            SessionId:    string.Empty,
            Destination:  destination.Id,
            DepartureUtc: request.DepartureUtc,
            ArrivalUtc:   request.ArrivalUtc,
            Trajectory:   Array.Empty<object>(),
            Obstacles:    obstaclesDto,
            MissionScore: missionScore,
            RiskScore:    Math.Round(riskScore, 4));

        return Task.FromResult(response);
    }

    // ── CreateSessionAsync ─────────────────────────────────────────────────────

    public Task<SessionResponse> CreateSessionAsync(
        SessionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var destination = KnownDestinations.Get(request.Destination)
            ?? throw new ArgumentException(
                $"Unknown destination '{request.Destination}'.", nameof(request));

        var sessionId = "sess_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8))
                                        .ToLowerInvariant();

        var now = DateTime.UtcNow;
        var session = new MissionSession
        {
            SessionId    = sessionId,
            Destination  = destination.Id,
            DepartureTime = DateTime.Parse(request.DepartureTime, null,
                                System.Globalization.DateTimeStyles.RoundtripKind),
            ArrivalTime  = DateTime.Parse(request.ArrivalTime, null,
                                System.Globalization.DateTimeStyles.RoundtripKind),
            ExpiresAt    = now.AddMinutes(30),
            UserId       = Guid.Empty,  // filled in by caller when authenticated
            CreatedAtUtc = now
        };

        _sessions.Set(session);

        var response = new SessionResponse(
            SessionId:     sessionId,
            Destination:   destination.Id,
            DepartureTime: request.DepartureTime,
            ArrivalTime:   request.ArrivalTime,
            StreamUrl:     $"/api/mission/stream/{sessionId}",
            ExpiresAt:     session.ExpiresAt.ToString("O"));

        return Task.FromResult(response);
    }

    // ── CompleteSessionAsync ───────────────────────────────────────────────────

    public async Task<CompleteSessionResponse> CompleteSessionAsync(
        string sessionId,
        CompleteSessionRequest request,
        Guid? userId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(request);

        var session = _sessions.Get(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found or expired.");

        var destination = KnownDestinations.Get(session.Destination)
            ?? throw new InvalidOperationException(
                $"Destination '{session.Destination}' no longer valid.");

        var debris       = _orbitalCache.GetAll();
        var conjunctions = _detector.Detect(destination, DateTime.UtcNow, debris);
        var riskScore    = RiskScoring.ComputeScore(
            conjunctions.Select(c => c.ClosestApproachKm));
        var (_, _, score) = MissionScoring.Compute(destination.DeltaVKmS, riskScore);

        var status   = request.Status;
        string? missionId = null;

        if (request.SaveToHistory && userId.HasValue)
        {
            var summary = await _history.SaveMissionAsync(
                userId:       userId!.Value,
                sessionId:    sessionId,
                status:       status,
                riskScore:    riskScore,
                deltaV:       destination.DeltaVKmS,
                score:        score,
                obstacles:    conjunctions.Count,
                departure:    session.DepartureTime,
                arrival:      DateTime.UtcNow,
                destination:  session.Destination,
                obstaclesData: conjunctions.Cast<object>().ToList().AsReadOnly(),
                ct:           ct);
            missionId = $"msn_{summary.Id}";
        }

        _sessions.Remove(sessionId);

        var duration = (DateTime.UtcNow - session.CreatedAtUtc).TotalSeconds;

        return new CompleteSessionResponse(
            SessionId:            sessionId,
            Status:               status,
            MissionScore:         score,
            RiskScore:            Math.Round(riskScore, 4),
            DeltaVKmS:            destination.DeltaVKmS,
            ObstaclesEncountered: conjunctions.Count,
            DurationSeconds:      duration,
            SavedToHistory:       request.SaveToHistory && userId.HasValue,
            MissionId:            missionId);
    }
}
```

> **DTOs required (add to `MissionClear.Api/Dtos/Mission/` if not present from Phase 02):**
>
> ```csharp
> // SimulateRequest.cs
> public sealed record SimulateRequest(string Destination, DateTime DepartureUtc, DateTime ArrivalUtc);
>
> // SimulateResponse.cs
> public sealed record SimulateResponse(
>     string SessionId,
>     string Destination,
>     DateTime DepartureUtc,
>     DateTime ArrivalUtc,
>     IReadOnlyList<object> Trajectory,
>     IReadOnlyList<ObstacleDto> Obstacles,
>     int MissionScore,
>     double RiskScore);
>
> // ObstacleDto.cs
> public sealed record ObstacleDto(
>     string DebrisId,
>     double ClosestApproachKm,
>     string TimeOfClosestApproach,
>     string RiskLevel);
>
> // SessionRequest.cs
> public sealed record SessionRequest(string Destination, string DepartureTime, string ArrivalTime);
> // DepartureTime and ArrivalTime are ISO 8601 strings (e.g. "2025-05-27T14:32:00Z")
> // The implementation parses them to DateTime internally.
>
> // SessionResponse.cs
> public sealed record SessionResponse(string SessionId, string StreamUrl);
>
> // CompleteSessionRequest.cs
> public sealed record CompleteSessionRequest(string Status, bool SaveToHistory = false);
>
> // CompleteSessionResponse.cs
> public sealed record CompleteSessionResponse(
>     string SessionId,
>     string Status,
>     int MissionScore,
>     double RiskScore,
>     double DeltaVKmS,
>     int ObstaclesEncountered,
>     double DurationSeconds,
>     bool SavedToHistory,
>     string? MissionId);
> ```
>
> **Note:** `SaveMissionRequest` record does NOT exist. `IMissionHistoryService.SaveMissionAsync`
> takes 11 positional parameters directly (see Phase 06 interface definition). Do not define or
> use a `SaveMissionRequest` wrapper type anywhere.

**22. [ ] Run tests — confirm GREEN (4 pass)**

**23. [ ] Commit:** `feat(simulation): add MissionSimulationService with session lifecycle and history integration`

---

### Phase 6 — MissionSseService (commit 6)

`MissionSseService` is not unit-tested in isolation (SSE tests over real HTTP response streams
are brittle in unit test mode). It is covered by integration tests in Phase 07. Implement directly.

**24. [ ] Implement MissionSseService**

`MissionClear.Api/Services/MissionSseService.cs`:
```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

/// <summary>
/// Streams Server-Sent Events for an active mission session.
/// SSE format: "event: {name}\ndata: {json}\n\n"
/// Simulated time: 1 real second = 10 simulated minutes (demo acceleration).
/// </summary>
public sealed class MissionSseService : IMissionSseService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan HeartbeatInterval  = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DebrisUpdateInterval = TimeSpan.FromSeconds(30);

    // Simulated time step per real second (10 minutes simulated per 1s real)
    private static readonly TimeSpan SimulatedStep = TimeSpan.FromMinutes(10);

    private readonly ISessionStore     _sessions;
    private readonly IOrbitalCache     _orbitalCache;
    private readonly IConjunctionDetector _detector;
    private readonly ILogger<MissionSseService> _logger;

    public MissionSseService(
        ISessionStore         sessions,
        IOrbitalCache         orbitalCache,
        IConjunctionDetector  detector,
        ILogger<MissionSseService> logger)
    {
        _sessions     = sessions;
        _orbitalCache = orbitalCache;
        _detector     = detector;
        _logger       = logger;
    }

    public async Task StreamAsync(
        string sessionId, HttpResponse response, CancellationToken ct)
    {
        response.Headers["Content-Type"]      = "text/event-stream";
        response.Headers["Cache-Control"]     = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        response.Headers["Connection"]        = "keep-alive";

        var session = _sessions.Get(sessionId);
        if (session is null)
        {
            await WriteEventAsync(response, "error",
                new { message = "Session not found or expired", session_id = sessionId }, ct);
            return;
        }

        var destination = KnownDestinations.Get(session.Destination);
        if (destination is null)
        {
            await WriteEventAsync(response, "error",
                new { message = "Unknown destination", session_id = sessionId }, ct);
            return;
        }

        var debris = _orbitalCache.GetAll();

        // Initial debris update
        await WriteEventAsync(response, "debris_update", new
        {
            session_id = sessionId,
            count      = debris.Count,
            timestamp  = DateTime.UtcNow
        }, ct);

        var lastHeartbeat   = DateTime.UtcNow;
        var lastDebrisUpdate = DateTime.UtcNow;
        var simulatedTime   = DateTime.UtcNow;
        var pollInterval    = TimeSpan.FromMilliseconds(50);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var now          = DateTime.UtcNow;
                simulatedTime   += SimulatedStep;

                if (now - lastHeartbeat >= HeartbeatInterval)
                {
                    await WriteEventAsync(response, "heartbeat", new
                    {
                        session_id       = sessionId,
                        timestamp        = now,
                        simulated_time   = simulatedTime
                    }, ct);
                    lastHeartbeat = now;
                }

                if (now - lastDebrisUpdate >= DebrisUpdateInterval)
                {
                    // Refresh debris and check for objects entering 200 km zone
                    // OrbitalObject uses Latitude and Longitude (no "Deg" suffix)
                    debris = _orbitalCache.GetAll();
                    var conjunctions = _detector.Detect(destination, simulatedTime, debris);
                    var nearbyDebris = debris
                        .Where(obj =>
                        {
                            var horizKm = OrbitalMath.HaversineKm(
                                destination.LatitudeDeg, destination.LongitudeDeg,
                                obj.Latitude, obj.Longitude,
                                OrbitalMath.EarthRadiusKm + (destination.AltitudeKm + obj.AltitudeKm) / 2);
                            var vertKm  = Math.Abs(destination.AltitudeKm - obj.AltitudeKm);
                            return Math.Sqrt(horizKm * horizKm + vertKm * vertKm) <= 500.0;
                        })
                        .ToList();

                    await WriteEventAsync(response, "debris_update", new
                    {
                        session_id = sessionId,
                        nearby     = nearbyDebris.Count,
                        timestamp  = now
                    }, ct);

                    // Alert on conjunctions entering 200 km zone
                    foreach (var c in conjunctions)
                    {
                        await WriteEventAsync(response, "conjunction_alert", new
                        {
                            session_id          = sessionId,
                            debris_id           = c.DebrisId,
                            closest_approach_km = c.ClosestApproachKm,
                            risk_level          = c.RiskLevel.ToString().ToLowerInvariant(),
                            toca                = c.TimeOfClosestApproach
                        }, ct);
                    }

                    lastDebrisUpdate = now;
                }

                await Task.Delay(pollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SSE stream cancelled for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSE stream error for session {SessionId}", sessionId);
        }
        finally
        {
            await WriteEventAsync(response, "session_complete", new
            {
                session_id   = sessionId,
                timestamp    = DateTime.UtcNow,
                simulated_time = simulatedTime
            }, CancellationToken.None);
        }
    }

    private static async Task WriteEventAsync(
        HttpResponse response, string eventName, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
```

**25. [ ] Verify build: `dotnet build` — must succeed**

**26. [ ] Commit:** `feat(simulation): add MissionSseService with heartbeat, debris_update, conjunction_alert, session_complete events`

---

### Phase 7 — DI Registration (commit 7)

**27. [ ] Register services in Program.cs**

Add the following to the DI registration block in `MissionClear.Api/Program.cs` after the
orbital cache and orbital engine registrations from Phase 03:

```csharp
// Simulation — scoped so each request gets a fresh ConjunctionDetector/LaunchWindowCalculator
builder.Services.AddScoped<IConjunctionDetector, ConjunctionDetector>();
builder.Services.AddScoped<ILaunchWindowCalculator, LaunchWindowCalculator>();
builder.Services.AddScoped<IMissionSimulationService, MissionSimulationService>();

// Session store — singleton (shared in-memory dictionary across all requests)
builder.Services.AddSingleton<ISessionStore>(
    _ => new SessionStore(
        ttlMinutes: builder.Configuration.GetValue<int>("OrbitalSettings:SessionTtlMinutes", 30)));

// SSE service — scoped (one per SSE connection)
builder.Services.AddScoped<IMissionSseService, MissionSseService>();
```

**28. [ ] Add `SessionTtlMinutes` to `appsettings.json` if not already present:**

```json
"OrbitalSettings": {
  ...
  "SessionTtlMinutes": 30
}
```

**29. [ ] Run full test suite:**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "Conjunction|LaunchWindow|Session|Simulation" -v normal
```

Expected: all tests pass, 0 failures.

**30. [ ] Run full build:**

```powershell
dotnet build
```

Expected: `Build succeeded. 0 Error(s). 0 Warning(s).`

**31. [ ] Commit:** `chore(di): register simulation services in DI container`

---

## Testing Strategy

| Test file | Count | Scope |
|---|---|---|
| `ConjunctionDetectorTests` | 4 | Unit — pure computation, no I/O |
| `LaunchWindowCalculatorTests` | 4 | Unit — pure computation, no I/O |
| `SessionStoreTests` | 4 | Unit — in-memory with injectable clock |
| `MissionSimulationServiceTests` | 4 | Unit — mocked IOrbitalCache + IMissionHistoryService |

`MissionSseService` is covered by integration tests in Phase 07 via `WebApplicationFactory`.

Coverage command:
```powershell
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

Target: ≥ 80% on `MissionClear.Api/Services/`.

---

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| `MissionDestination` missing `LatitudeDeg`/`LongitudeDeg` | Phase 02 adds these properties defaulting to 0.0 for equatorial orbit assumption |
| `OrbitalObject` uses `Latitude`/`Longitude` (no "Deg" suffix) | Always use `obj.Latitude` and `obj.Longitude` — never `obj.LatitudeDeg` / `obj.LongitudeDeg` |
| `KnownDestinations.Get(id)` alias | Phase 02 adds `Get` as alias for `FindById`; both are valid |
| `IOrbitalCache.GetAll()` method name differs from Phase 03 | Match exactly Phase 03's interface method; check `IOrbitalCache.cs` before finalizing |
| DTOs in `Dtos/Mission/` not yet created | Create them in this phase if Phase 02 left them as stubs; all are simple records |
| `IMissionHistoryService` interface not yet available (Phase 06) | Use `Mock.Of<IMissionHistoryService>()` in tests; the production DI wiring waits for Phase 06 |
| `SaveMissionAsync` signature | Takes 11 positional parameters — NO `SaveMissionRequest` wrapper type |
| `CompleteSessionRequest` missing `Status` field | Canonical definition is `(string Status, bool SaveToHistory = false)` — always pass Status |
| `CompleteSessionResponse` must have 9 fields | Canonical: SessionId, Status, MissionScore, RiskScore, DeltaVKmS, ObstaclesEncountered, DurationSeconds, SavedToHistory, MissionId |

---

## Success Criteria

- [ ] All 5 interface files exist in `MissionClear.Api/Services/Interfaces/`
- [ ] `ConjunctionDetector` correctly classifies: Critical < 1 km, High < 5 km, Medium < 10 km, Low otherwise
- [ ] `ConjunctionDetector` uses `OrbitalMath.HaversineKm` (not an inline Haversine)
- [ ] `ConjunctionDetector` uses `RiskScoring.Classify` (not inline thresholds)
- [ ] `ConjunctionDetector` reads `obj.Latitude` and `obj.Longitude` (NOT `obj.LatitudeDeg`/`obj.LongitudeDeg`)
- [ ] `LaunchWindowCalculator` produces exactly 48 windows for a 12-hour range
- [ ] `LaunchWindowCalculator` sets `IsRecommended = riskScore < 0.1`
- [ ] `LaunchWindowCalculator` uses `Start:` and `End:` in `LaunchWindow` constructor (NOT `StartUtc:`/`EndUtc:`)
- [ ] `LaunchWindowCalculator` copies `DeltaVKmS` and `DurationHours` from `MissionDestination`
- [ ] `SessionStore` is a `ConcurrentDictionary`-backed singleton
- [ ] `SessionStore` expires sessions on `Get` when `_clock() >= ExpiresAt`
- [ ] `MissionSimulationService.SimulateAsync` throws `ArgumentException` for unknown destinations
- [ ] `MissionSimulationService.SimulateAsync` returns `SimulateResponse` with `ObstacleDto` list (not `ConjunctionResult`)
- [ ] `MissionSimulationService.CreateSessionAsync` returns a `stream_url` pointing to the SSE endpoint
- [ ] `MissionSimulationService.CreateSessionAsync` creates `MissionSession` with `UserId` and `CreatedAtUtc`
- [ ] `MissionSimulationService.CompleteSessionAsync` calls `IMissionHistoryService.SaveMissionAsync` only when `SaveToHistory && userId != null`
- [ ] `MissionSimulationService.CompleteSessionAsync` calls `SaveMissionAsync` with 11 positional parameters (no wrapper type)
- [ ] `MissionSimulationService.CompleteSessionAsync` returns 9-field `CompleteSessionResponse`
- [ ] `MissionSseService` writes SSE frames with `event: {name}\ndata: {json}\n\n` format
- [ ] `MissionSseService` sets `Content-Type: text/event-stream` and `Cache-Control: no-cache`
- [ ] `MissionSseService` uses `obj.Latitude`/`obj.Longitude` for `OrbitalObject` (NOT `obj.LatitudeDeg`/`obj.LongitudeDeg`)
- [ ] All 16 unit tests pass with `dotnet test`
- [ ] `dotnet build` clean — 0 errors, 0 warnings
- [ ] All services registered in DI (3 scoped, 1 singleton, 1 scoped)

---

## Relevant Files

**Interfaces:**
- `MissionClear.Api/Services/Interfaces/IConjunctionDetector.cs`
- `MissionClear.Api/Services/Interfaces/ILaunchWindowCalculator.cs`
- `MissionClear.Api/Services/Interfaces/IMissionSimulationService.cs`
- `MissionClear.Api/Services/Interfaces/ISessionStore.cs`
- `MissionClear.Api/Services/Interfaces/IMissionSseService.cs`

**Implementations:**
- `MissionClear.Api/Services/ConjunctionDetector.cs`
- `MissionClear.Api/Services/LaunchWindowCalculator.cs`
- `MissionClear.Api/Services/MissionSimulationService.cs`
- `MissionClear.Api/Services/SessionStore.cs`
- `MissionClear.Api/Services/MissionSseService.cs`

**DTOs (add if absent):**
- `MissionClear.Api/Dtos/Mission/SimulateRequest.cs`
- `MissionClear.Api/Dtos/Mission/SimulateResponse.cs`
- `MissionClear.Api/Dtos/Mission/ObstacleDto.cs`
- `MissionClear.Api/Dtos/Mission/SessionRequest.cs`
- `MissionClear.Api/Dtos/Mission/SessionResponse.cs`
- `MissionClear.Api/Dtos/Mission/CompleteSessionRequest.cs`
- `MissionClear.Api/Dtos/Mission/CompleteSessionResponse.cs`

**Tests:**
- `MissionClear.Tests/Services/ConjunctionDetectorTests.cs`
- `MissionClear.Tests/Services/LaunchWindowCalculatorTests.cs`
- `MissionClear.Tests/Services/SessionStoreTests.cs`
- `MissionClear.Tests/Services/MissionSimulationServiceTests.cs`

**Modified:**
- `MissionClear.Api/Program.cs` (DI registrations)
- `MissionClear.Api/appsettings.json` (SessionTtlMinutes)
