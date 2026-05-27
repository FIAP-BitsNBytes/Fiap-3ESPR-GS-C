# Implementation Plan: Mission Simulation Services (Plan-05)

## Overview
Implements the mission simulation layer of the Mission Clear backend: conjunction detection (debris proximity to mission trajectory), launch window calculation (15-minute risk-scored slots), full mission simulation (trajectory + score), in-memory session store, and SSE streaming for real-time telemetry. All services follow strict TDD and feed the `/api/mission/*` and `/api/launch-windows*` controllers (built in plan-07).

## Requirements
- ASP.NET Core 8 (no external dependencies beyond what plan-03 already added)
- Domain types from plan-02 (`OrbitalObject`, `ConjunctionResult`, `LaunchWindow`, `MissionSession`, `SessionStatus`, `RiskLevel`)
- `IOrbitalCache` / `OrbitalEngineService` (plan-03) available — consumed only via injection at controller layer, services here receive `IReadOnlyList<OrbitalObject>` as input
- `KnownDestinations` registry (plan-02): `ISS`, `Hubble`, `Tiangong`, etc., each with `LatitudeDeg`, `LongitudeDeg`, `AltitudeKm`, `DeltaVKmS`, `MissionDurationHours`
- `RiskLevelClassifier.Classify(double km)` static helper (plan-02)
- Test framework: xUnit + FluentAssertions (already in `MissionClear.Tests`)
- Coverage target: 80%+ on Services

## Architecture Changes

```
MissionClear.Api/
├── Interfaces/
│   ├── IConjunctionDetector.cs
│   ├── ILaunchWindowCalculator.cs
│   └── IMissionSimulationService.cs
├── Services/
│   ├── ConjunctionDetector.cs
│   ├── LaunchWindowCalculator.cs
│   ├── MissionSimulationService.cs
│   ├── SessionStore.cs
│   ├── SessionStoreOptions.cs
│   └── MissionSseService.cs
└── Program.cs (DI registration additions)

MissionClear.Tests/
└── Services/
    ├── ConjunctionDetectorTests.cs
    ├── LaunchWindowCalculatorTests.cs
    ├── MissionSimulationServiceTests.cs
    ├── SessionStoreTests.cs
    └── MissionSseServiceTests.cs
```

> **Regra:** interfaces em `Interfaces/`, implementações em `Services/`. Nunca misturar.

> **Namespaces corretos:**
> - Arquivos em `Interfaces/` → `namespace MissionClear.Api.Interfaces;`
> - Arquivos em `Services/` → `namespace MissionClear.Api.Services;` + `using MissionClear.Api.Interfaces;`
> - Os snippets de código neste plano usam o namespace de interface; nas implementações trocar para `MissionClear.Api.Services`.

No new NuGet packages. No new configuration sections except `SessionStoreOptions` (TTL).

---

## Implementation Steps

### Phase 1: ConjunctionDetector (commit 1)

1. **[ ] Write ConjunctionDetector tests (RED)** (`MissionClear.Tests/Services/ConjunctionDetectorTests.cs`)
   - 6 tests covering empty input, no detections, Medium (50 km), High (3 km), Critical (0.5 km), outside-radius filter.
   - Use `OrbitalObject` factory helper to place debris at a known lat/lon/alt relative to a mission point.
   - Why: Lock in the 3D-distance contract before implementation.
   - Dependencies: plan-02 domain types.
   - Risk: Medium — Haversine math is easy to get wrong; tests must use precomputed expected distances.

   ```csharp
   using FluentAssertions;
   using MissionClear.Api.Domain;
   using MissionClear.Api.Services.Conjunctions;
   using Xunit;

   namespace MissionClear.Tests.Services;

   public class ConjunctionDetectorTests
   {
       private static OrbitalObject Debris(string id, double lat, double lon, double altKm) =>
           new(id, $"DEB-{id}", "debris", lat, lon, altKm, 7.6, "celestrak", DateTime.UtcNow);

       private readonly IConjunctionDetector _sut = new ConjunctionDetector();

       [Fact]
       public void Returns_empty_when_debris_list_is_empty()
       {
           var result = _sut.Detect(Array.Empty<OrbitalObject>(), 0, 0, 400);
           result.Should().BeEmpty();
       }

       [Fact]
       public void Returns_empty_when_no_debris_within_safe_radius()
       {
           var debris = new[] { Debris("1", 45.0, 45.0, 400) }; // far away
           var result = _sut.Detect(debris, 0, 0, 400);
           result.Should().BeEmpty();
       }

       [Fact]
       public void Detects_debris_at_about_50km_as_medium_or_low()
       {
           // ~0.45 degrees latitude at 400km ≈ 50 km horizontal
           var debris = new[] { Debris("med", 0.45, 0, 400) };
           var result = _sut.Detect(debris, 0, 0, 400, safeRadiusKm: 200);
           result.Should().HaveCount(1);
           result[0].ClosestApproachKm.Should().BeInRange(45, 55);
           result[0].Risk.Should().BeOneOf(RiskLevel.Low, RiskLevel.Medium);
       }

       [Fact]
       public void Detects_debris_at_3km_as_high()
       {
           var debris = new[] { Debris("hi", 0, 0, 403) }; // 3 km vertical
           var result = _sut.Detect(debris, 0, 0, 400);
           result.Should().HaveCount(1);
           result[0].ClosestApproachKm.Should().BeApproximately(3.0, 0.1);
           result[0].Risk.Should().Be(RiskLevel.High);
       }

       [Fact]
       public void Detects_debris_at_half_km_as_critical()
       {
           var debris = new[] { Debris("crit", 0, 0, 400.5) };
           var result = _sut.Detect(debris, 0, 0, 400);
           result[0].Risk.Should().Be(RiskLevel.Critical);
       }

       [Fact]
       public void Ignores_debris_outside_safe_radius()
       {
           var debris = new[] { Debris("far", 0, 0, 700) }; // 300 km vertical
           var result = _sut.Detect(debris, 0, 0, 400, safeRadiusKm: 200);
           result.Should().BeEmpty();
       }
   }
   ```

2. **[ ] Run tests — confirm RED** (all 6 fail with type-not-found)

3. **[ ] Implement IConjunctionDetector + ConjunctionDetector (GREEN)**

   ```csharp
   // IConjunctionDetector.cs
   using MissionClear.Api.Domain;

   namespace MissionClear.Api.Interfaces;

   public interface IConjunctionDetector
   {
       IReadOnlyList<ConjunctionResult> Detect(
           IEnumerable<OrbitalObject> debris,
           double missionLatDeg,
           double missionLonDeg,
           double missionAltKm,
           double safeRadiusKm = 200.0);
   }
   ```

   ```csharp
   // ConjunctionDetector.cs
   using MissionClear.Api.Domain;

   namespace MissionClear.Api.Interfaces;

   public sealed class ConjunctionDetector : IConjunctionDetector
   {
       private const double EarthRadiusKm = 6371.0;
       private const double RelativeVelocityKmS = 14.0;

       public IReadOnlyList<ConjunctionResult> Detect(
           IEnumerable<OrbitalObject> debris,
           double missionLatDeg,
           double missionLonDeg,
           double missionAltKm,
           double safeRadiusKm = 200.0)
       {
           ArgumentNullException.ThrowIfNull(debris);
           var now = DateTime.UtcNow;
           var results = new List<ConjunctionResult>();

           foreach (var obj in debris)
           {
               var avgAlt = (missionAltKm + obj.AltitudeKm) / 2.0;
               var horizKm = HaversineKm(
                   missionLatDeg, missionLonDeg,
                   obj.LatitudeDeg, obj.LongitudeDeg,
                   EarthRadiusKm + avgAlt);
               var vertKm = Math.Abs(missionAltKm - obj.AltitudeKm);
               var distance = Math.Sqrt(horizKm * horizKm + vertKm * vertKm);

               if (distance >= safeRadiusKm) continue;

               var etaMinutes = distance / RelativeVelocityKmS / 60.0;
               results.Add(new ConjunctionResult(
                   DebrisId: obj.NoradCatId,
                   DebrisName: obj.Name,
                   ClosestApproachKm: Math.Round(distance, 3),
                   TimeOfClosestApproachUtc: now.AddMinutes(etaMinutes),
                   Risk: RiskLevelClassifier.Classify(distance)));
           }

           return results;
       }

       private static double HaversineKm(double lat1, double lon1, double lat2, double lon2, double radiusKm)
       {
           var dLat = ToRad(lat2 - lat1);
           var dLon = ToRad(lon2 - lon1);
           var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
           var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
           return radiusKm * c;
       }

       private static double ToRad(double deg) => deg * Math.PI / 180.0;
   }
   ```

4. **[ ] Run tests — confirm GREEN**

5. **[ ] Commit:** `feat(services): add ConjunctionDetector with Haversine 3D proximity`

---

### Phase 2: LaunchWindowCalculator (commit 2)

6. **[ ] Write LaunchWindowCalculator tests (RED)** (`MissionClear.Tests/Services/LaunchWindowCalculatorTests.cs`)
   - 6 tests: windows in range, sorted ascending, zero-risk window recommended, populated risk_score, maxWindows cap, invalid destination returns empty.

   ```csharp
   using FluentAssertions;
   using MissionClear.Api.Domain;
   using MissionClear.Api.Services.Conjunctions;
   using MissionClear.Api.Services.LaunchWindows;
   using Xunit;

   namespace MissionClear.Tests.Services;

   public class LaunchWindowCalculatorTests
   {
       private readonly ILaunchWindowCalculator _sut =
           new LaunchWindowCalculator(new ConjunctionDetector());

       [Fact]
       public void Returns_windows_within_specified_range()
       {
           var from = DateTime.UtcNow;
           var to = from.AddHours(2);
           var windows = _sut.Calculate("ISS", from, to,
               Array.Empty<OrbitalObject>(), windowSlotMinutes: 15);
           windows.Should().OnlyContain(w => w.StartUtc >= from && w.EndUtc <= to);
       }

       [Fact]
       public void Windows_are_sorted_by_risk_score_ascending()
       {
           var from = DateTime.UtcNow;
           var windows = _sut.Calculate("ISS", from, from.AddHours(4),
               Array.Empty<OrbitalObject>());
           windows.Select(w => w.RiskScore).Should().BeInAscendingOrder();
       }

       [Fact]
       public void Zero_conjunctions_yields_risk_zero_and_recommended()
       {
           var windows = _sut.Calculate("ISS", DateTime.UtcNow, DateTime.UtcNow.AddHours(1),
               Array.Empty<OrbitalObject>());
           windows.Should().OnlyContain(w => w.RiskScore == 0.0 && w.IsRecommended);
       }

       [Fact]
       public void Close_conjunctions_raise_risk_score()
       {
           var iss = KnownDestinations.Get("ISS")!;
           var close = Enumerable.Range(0, 5).Select(i =>
               new OrbitalObject($"c{i}", $"D{i}", "debris",
                   iss.LatitudeDeg, iss.LongitudeDeg, iss.AltitudeKm + 0.5,
                   7.6, "celestrak", DateTime.UtcNow)).ToList();
           var windows = _sut.Calculate("ISS", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30), close);
           windows.Should().Contain(w => w.RiskScore > 0);
       }

       [Fact]
       public void Total_windows_capped_by_maxWindows()
       {
           var windows = _sut.Calculate("ISS", DateTime.UtcNow,
               DateTime.UtcNow.AddDays(5), Array.Empty<OrbitalObject>(),
               windowSlotMinutes: 15, maxWindows: 10);
           windows.Should().HaveCountLessThanOrEqualTo(10);
       }

       [Fact]
       public void Invalid_destination_returns_empty()
       {
           var windows = _sut.Calculate("NOPE", DateTime.UtcNow,
               DateTime.UtcNow.AddHours(1), Array.Empty<OrbitalObject>());
           windows.Should().BeEmpty();
       }
   }
   ```

7. **[ ] Implement ILaunchWindowCalculator + LaunchWindowCalculator (GREEN)**

   ```csharp
   // ILaunchWindowCalculator.cs
   using MissionClear.Api.Domain;

   namespace MissionClear.Api.Interfaces;

   public interface ILaunchWindowCalculator
   {
       IReadOnlyList<LaunchWindow> Calculate(
           string destinationId,
           DateTime fromUtc,
           DateTime toUtc,
           IReadOnlyList<OrbitalObject> currentDebris,
           int windowSlotMinutes = 15,
           int maxWindows = 48);
   }
   ```

   ```csharp
   // LaunchWindowCalculator.cs
   using MissionClear.Api.Domain;
   using MissionClear.Api.Services.Conjunctions;

   namespace MissionClear.Api.Interfaces;

   public sealed class LaunchWindowCalculator : ILaunchWindowCalculator
   {
       private const double SafeKm = 25.0;
       private const double MaxKm = 200.0;
       private const double RecommendedThreshold = 0.05;

       private readonly IConjunctionDetector _detector;

       public LaunchWindowCalculator(IConjunctionDetector detector) => _detector = detector;

       public IReadOnlyList<LaunchWindow> Calculate(
           string destinationId,
           DateTime fromUtc,
           DateTime toUtc,
           IReadOnlyList<OrbitalObject> currentDebris,
           int windowSlotMinutes = 15,
           int maxWindows = 48)
       {
           var dest = KnownDestinations.Get(destinationId);
           if (dest is null || toUtc <= fromUtc) return Array.Empty<LaunchWindow>();

           var slot = TimeSpan.FromMinutes(windowSlotMinutes);
           var windows = new List<LaunchWindow>();

           for (var start = fromUtc; start + slot <= toUtc && windows.Count < maxWindows * 4; start += slot)
           {
               var end = start + slot;
               var conjunctions = _detector.Detect(
                   currentDebris,
                   dest.LatitudeDeg, dest.LongitudeDeg, dest.AltitudeKm,
                   safeRadiusKm: MaxKm);

               var riskScore = ComputeRiskScore(conjunctions);
               windows.Add(new LaunchWindow(
                   StartUtc: start,
                   EndUtc: end,
                   RiskScore: Math.Round(riskScore, 4),
                   DeltaVKmS: dest.DeltaVKmS,
                   DurationHours: dest.MissionDurationHours,
                   IsRecommended: riskScore < RecommendedThreshold,
                   Conjunctions: conjunctions));
           }

           return windows
               .OrderBy(w => w.RiskScore)
               .Take(maxWindows)
               .ToList();
       }

       private static double ComputeRiskScore(IReadOnlyList<ConjunctionResult> conjunctions)
       {
           if (conjunctions.Count == 0) return 0.0;
           double total = 0.0;
           foreach (var c in conjunctions)
           {
               if (c.ClosestApproachKm >= MaxKm) continue;
               var contrib = 1.0 - (c.ClosestApproachKm - SafeKm) / (MaxKm - SafeKm);
               total += Math.Max(0.0, contrib);
           }
           return Math.Min(1.0, total);
       }
   }
   ```

8. **[ ] Run tests — confirm GREEN**

9. **[ ] Commit:** `feat(services): add LaunchWindowCalculator with risk-weighted slots`

---

### Phase 3: MissionSimulationService (commit 3)

10. **[ ] Write MissionSimulationService tests (RED)** (`MissionClear.Tests/Services/MissionSimulationServiceTests.cs`)
    - 5 tests: 10-point trajectory, score in [0,100], invalid destination throws `ArgumentException`, empty debris → ~100, many close debris → low score.

    ```csharp
    using FluentAssertions;
    using MissionClear.Api.Domain;
    using MissionClear.Api.Dtos;
    using MissionClear.Api.Services.Conjunctions;
    using MissionClear.Api.Services.Missions;
    using Xunit;

    namespace MissionClear.Tests.Services;

    public class MissionSimulationServiceTests
    {
        private readonly IMissionSimulationService _sut =
            new MissionSimulationService(new ConjunctionDetector());

        private static MissionSimulateRequest Req(string dest = "ISS") =>
            new(dest, DateTime.UtcNow, DateTime.UtcNow.AddHours(6));

        [Fact]
        public void Returns_trajectory_with_10_points()
        {
            var resp = _sut.Simulate(Req(), Array.Empty<OrbitalObject>());
            resp.Trajectory.Should().HaveCount(10);
        }

        [Fact]
        public void Valid_destination_returns_score_in_range()
        {
            var resp = _sut.Simulate(Req(), Array.Empty<OrbitalObject>());
            resp.MissionScore.Should().BeInRange(0, 100);
        }

        [Fact]
        public void Invalid_destination_throws_ArgumentException()
        {
            var act = () => _sut.Simulate(Req("NOPE"), Array.Empty<OrbitalObject>());
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Empty_debris_yields_high_score()
        {
            var resp = _sut.Simulate(Req(), Array.Empty<OrbitalObject>());
            resp.RiskScore.Should().Be(0.0);
            resp.MissionScore.Should().BeGreaterThan(50);
        }

        [Fact]
        public void Many_close_debris_yields_low_score()
        {
            var iss = KnownDestinations.Get("ISS")!;
            var crowd = Enumerable.Range(0, 30).Select(i =>
                new OrbitalObject($"d{i}", $"D{i}", "debris",
                    iss.LatitudeDeg, iss.LongitudeDeg, iss.AltitudeKm + 0.2,
                    7.6, "celestrak", DateTime.UtcNow)).ToList();

            var resp = _sut.Simulate(Req(), crowd);
            resp.RiskScore.Should().BeGreaterThan(0.5);
            resp.MissionScore.Should().BeLessThan(60);
        }
    }
    ```

11. **[ ] Implement IMissionSimulationService + MissionSimulationService (GREEN)**

    ```csharp
    // IMissionSimulationService.cs
    using MissionClear.Api.Domain;
    using MissionClear.Api.Dtos;

    namespace MissionClear.Api.Interfaces;

    public interface IMissionSimulationService
    {
        MissionSimulateResponse Simulate(
            MissionSimulateRequest request,
            IReadOnlyList<OrbitalObject> currentDebris);
    }
    ```

    ```csharp
    // MissionSimulationService.cs
    using MissionClear.Api.Domain;
    using MissionClear.Api.Dtos;
    using MissionClear.Api.Services.Conjunctions;

    namespace MissionClear.Api.Interfaces;

    public sealed class MissionSimulationService : IMissionSimulationService
    {
        private const int TrajectoryPoints = 10;
        private const double SafeKm = 25.0;
        private const double MaxKm = 200.0;

        private readonly IConjunctionDetector _detector;

        public MissionSimulationService(IConjunctionDetector detector) => _detector = detector;

        public MissionSimulateResponse Simulate(
            MissionSimulateRequest request,
            IReadOnlyList<OrbitalObject> currentDebris)
        {
            ArgumentNullException.ThrowIfNull(request);
            var dest = KnownDestinations.Get(request.DestinationId)
                ?? throw new ArgumentException(
                    $"Unknown destination '{request.DestinationId}'", nameof(request));

            var trajectory = BuildTrajectory(request.DepartureUtc, request.ArrivalUtc, dest);
            var aggregated = new Dictionary<string, ConjunctionResult>(StringComparer.Ordinal);

            foreach (var p in trajectory)
            {
                var hits = _detector.Detect(currentDebris, p.LatitudeDeg, p.LongitudeDeg, p.AltitudeKm);
                foreach (var h in hits)
                {
                    if (!aggregated.TryGetValue(h.DebrisId, out var existing)
                        || h.ClosestApproachKm < existing.ClosestApproachKm)
                    {
                        aggregated[h.DebrisId] = h;
                    }
                }
            }

            var conjunctions = aggregated.Values
                .OrderBy(c => c.ClosestApproachKm)
                .ToList();

            var riskScore = ComputeRiskScore(conjunctions);
            var efficiency = Math.Max(0.0, 1.0 - dest.DeltaVKmS / 12.0) * 50.0;
            var safety = (1.0 - riskScore) * 50.0;
            var missionScore = Math.Clamp((int)Math.Round(efficiency + safety), 0, 100);

            return new MissionSimulateResponse(
                Trajectory: trajectory,
                Obstacles: conjunctions,
                RiskScore: Math.Round(riskScore, 4),
                MissionScore: missionScore,
                Destination: dest.Id,
                DeltaVKmS: dest.DeltaVKmS,
                DurationHours: dest.MissionDurationHours);
        }

        private static IReadOnlyList<TrajectoryPointDto> BuildTrajectory(
            DateTime departure, DateTime arrival, DestinationProfile dest)
        {
            var points = new List<TrajectoryPointDto>(TrajectoryPoints);
            var totalSeconds = Math.Max(1, (arrival - departure).TotalSeconds);

            for (var i = 0; i < TrajectoryPoints; i++)
            {
                var t = (double)i / (TrajectoryPoints - 1);
                var lat = Lerp(0.0, dest.LatitudeDeg, t);
                var lon = Lerp(0.0, dest.LongitudeDeg, t);
                var alt = Lerp(0.0, dest.AltitudeKm, t);
                var ts = departure.AddSeconds(totalSeconds * t);
                points.Add(new TrajectoryPointDto(lat, lon, alt, ts));
            }
            return points;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static double ComputeRiskScore(IReadOnlyList<ConjunctionResult> conjunctions)
        {
            if (conjunctions.Count == 0) return 0.0;
            double total = 0.0;
            foreach (var c in conjunctions)
            {
                if (c.ClosestApproachKm >= MaxKm) continue;
                total += Math.Max(0.0, 1.0 - (c.ClosestApproachKm - SafeKm) / (MaxKm - SafeKm));
            }
            return Math.Min(1.0, total);
        }
    }
    ```

12. **[ ] Run tests — confirm GREEN**

13. **[ ] Commit:** `feat(services): add MissionSimulationService with trajectory + scoring`

---

### Phase 4: SessionStore (commit 4)

14. **[ ] Write SessionStore tests (RED)** (`MissionClear.Tests/Services/SessionStoreTests.cs`)

    ```csharp
    using FluentAssertions;
    using Microsoft.Extensions.Options;
    using MissionClear.Api.Domain;
    using MissionClear.Api.Services.Sessions;
    using Xunit;

    namespace MissionClear.Tests.Services;

    public class SessionStoreTests
    {
        private static SessionStore NewStore(int ttlMinutes = 30) =>
            new(Options.Create(new SessionStoreOptions { TtlMinutes = ttlMinutes }),
                () => DateTime.UtcNow);

        [Fact]
        public void CreateSession_returns_active_session_with_prefix()
        {
            var s = NewStore().CreateSession("user1", new SessionRequest("ISS", "easy"));
            s.SessionId.Should().StartWith("sess_");
            s.Status.Should().Be(SessionStatus.Active);
            s.UserId.Should().Be("user1");
        }

        [Fact]
        public void GetSession_returns_null_for_unknown_id()
        {
            NewStore().GetSession("sess_nope").Should().BeNull();
        }

        [Fact]
        public void TryCompleteSession_updates_status_and_scores()
        {
            var store = NewStore();
            var s = store.CreateSession(null, new SessionRequest("ISS", "easy"));
            var ok = store.TryCompleteSession(s.SessionId, SessionStatus.Success, 87, 0.1, 9.4, 3, 12.5);
            ok.Should().BeTrue();
            var reloaded = store.GetSession(s.SessionId)!;
            reloaded.Status.Should().Be(SessionStatus.Success);
            reloaded.FinalMissionScore.Should().Be(87);
        }

        [Fact]
        public void TryCompleteSession_returns_false_for_unknown_session()
        {
            NewStore().TryCompleteSession("sess_x", SessionStatus.Success, 0, 0, 0, 0, 0)
                .Should().BeFalse();
        }

        [Fact]
        public void Expired_sessions_return_null()
        {
            var now = DateTime.UtcNow;
            var clock = now;
            var store = new SessionStore(
                Options.Create(new SessionStoreOptions { TtlMinutes = 30 }),
                () => clock);

            var s = store.CreateSession(null, new SessionRequest("ISS", "easy"));
            clock = now.AddMinutes(31);
            store.GetSession(s.SessionId).Should().BeNull();
        }
    }
    ```

15. **[ ] Implement SessionStore + SessionStoreOptions (GREEN)**

    ```csharp
    // SessionStoreOptions.cs
    namespace MissionClear.Api.Services.Sessions;

    public sealed class SessionStoreOptions
    {
        public int TtlMinutes { get; set; } = 30;
    }
    ```

    ```csharp
    // SessionStore.cs
    using System.Collections.Concurrent;
    using System.Security.Cryptography;
    using Microsoft.Extensions.Options;
    using MissionClear.Api.Domain;

    namespace MissionClear.Api.Services.Sessions;

    public sealed class SessionStore
    {
        private readonly ConcurrentDictionary<string, MissionSession> _sessions = new();
        private readonly SessionStoreOptions _options;
        private readonly Func<DateTime> _clock;

        public SessionStore(IOptions<SessionStoreOptions> options)
            : this(options, () => DateTime.UtcNow) { }

        public SessionStore(IOptions<SessionStoreOptions> options, Func<DateTime> clock)
        {
            _options = options.Value;
            _clock = clock;
        }

        public MissionSession CreateSession(string? userId, SessionRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            PurgeExpiredSessions();

            var session = new MissionSession
            {
                SessionId = "sess_" + RandomToken(16),
                UserId = userId,
                DestinationId = request.DestinationId,
                Difficulty = request.Difficulty,
                Status = SessionStatus.Active,
                CreatedAtUtc = _clock(),
            };
            _sessions[session.SessionId] = session;
            return session;
        }

        public MissionSession? GetSession(string sessionId)
        {
            PurgeExpiredSessions();
            if (!_sessions.TryGetValue(sessionId, out var s)) return null;
            if (IsExpired(s))
            {
                _sessions.TryRemove(sessionId, out _);
                return null;
            }
            return s;
        }

        public bool TryCompleteSession(
            string sessionId,
            SessionStatus status,
            double score,
            double risk,
            double deltaV,
            int obstacles,
            double durationSeconds)
        {
            if (!_sessions.TryGetValue(sessionId, out var s)) return false;
            s.Status = status;
            s.FinalMissionScore = score;
            s.FinalRiskScore = risk;
            s.DeltaVKmS = deltaV;
            s.ObstaclesEncountered = obstacles;
            s.DurationSeconds = durationSeconds;
            s.CompletedAtUtc = _clock();
            return true;
        }

        public void PurgeExpiredSessions()
        {
            foreach (var kv in _sessions)
            {
                if (IsExpired(kv.Value))
                    _sessions.TryRemove(kv.Key, out _);
            }
        }

        private bool IsExpired(MissionSession s) =>
            s.Status == SessionStatus.Active
            && _clock() - s.CreatedAtUtc > TimeSpan.FromMinutes(_options.TtlMinutes);

        private static string RandomToken(int bytes)
        {
            var buf = RandomNumberGenerator.GetBytes(bytes);
            return Convert.ToHexString(buf).ToLowerInvariant();
        }
    }
    ```

16. **[ ] Run tests — confirm GREEN**

17. **[ ] Commit:** `feat(services): add in-memory SessionStore with TTL and thread-safety`

---

### Phase 5: MissionSseService (commit 5)

18. **[ ] Write MissionSseService tests (RED)** (`MissionClear.Tests/Services/MissionSseServiceTests.cs`)

    ```csharp
    using System.Text;
    using FluentAssertions;
    using Microsoft.AspNetCore.Http;
    using MissionClear.Api.Domain;
    using MissionClear.Api.Services.Streaming;
    using Xunit;

    namespace MissionClear.Tests.Services;

    public class MissionSseServiceTests
    {
        private static (HttpResponse Resp, MemoryStream Body) NewResponse()
        {
            var ctx = new DefaultHttpContext();
            var body = new MemoryStream();
            ctx.Response.Body = body;
            return (ctx.Response, body);
        }

        private static readonly OrbitalObject[] Sample = new[]
        {
            new OrbitalObject("1", "TEST", "debris", 0, 0, 400, 7.6, "celestrak", DateTime.UtcNow)
        };

        [Fact]
        public async Task First_event_is_debris_update()
        {
            var (resp, body) = NewResponse();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var sut = new MissionSseService(NullLoggerFactory());

            await sut.StreamAsync("sess_test", resp, Sample, cts.Token);

            var text = Encoding.UTF8.GetString(body.ToArray());
            text.Should().StartWith("event: debris_update");
        }

        [Fact]
        public async Task Frames_use_event_and_data_with_blank_separator()
        {
            var (resp, body) = NewResponse();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var sut = new MissionSseService(NullLoggerFactory());

            await sut.StreamAsync("sess_test", resp, Sample, cts.Token);

            var text = Encoding.UTF8.GetString(body.ToArray());
            text.Should().Contain("event: debris_update\ndata: ");
            text.Should().Contain("\n\n");
        }

        [Fact]
        public async Task Heartbeat_contains_session_id_and_timestamp()
        {
            var (resp, body) = NewResponse();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            var sut = new MissionSseService(NullLoggerFactory(),
                debrisInterval: TimeSpan.FromMilliseconds(50),
                heartbeatInterval: TimeSpan.FromMilliseconds(80));

            await sut.StreamAsync("sess_test", resp, Sample, cts.Token);

            var text = Encoding.UTF8.GetString(body.ToArray());
            text.Should().Contain("event: heartbeat");
            text.Should().Contain("sess_test");
        }

        [Fact]
        public async Task Stream_stops_when_token_cancelled()
        {
            var (resp, body) = NewResponse();
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var sut = new MissionSseService(NullLoggerFactory());

            var t = sut.StreamAsync("sess_test", resp, Sample, cts.Token);
            await t;
            t.IsCompletedSuccessfully.Should().BeTrue();
        }

        private static Microsoft.Extensions.Logging.ILoggerFactory NullLoggerFactory() =>
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
    }
    ```

19. **[ ] Implement MissionSseService (GREEN)** (`MissionClear.Api/Services/Streaming/MissionSseService.cs`)
    - Default intervals: 30s debris, 15s heartbeat. Test constructor overload for short intervals.
    - Headers: `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`.

    ```csharp
    using System.Text.Json;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;
    using MissionClear.Api.Domain;

    namespace MissionClear.Api.Services.Streaming;

    public sealed class MissionSseService
    {
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        private readonly ILogger<MissionSseService> _logger;
        private readonly TimeSpan _debrisInterval;
        private readonly TimeSpan _heartbeatInterval;

        public MissionSseService(ILoggerFactory loggerFactory)
            : this(loggerFactory, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15)) { }

        public MissionSseService(
            ILoggerFactory loggerFactory,
            TimeSpan debrisInterval,
            TimeSpan heartbeatInterval)
        {
            _logger = loggerFactory.CreateLogger<MissionSseService>();
            _debrisInterval = debrisInterval;
            _heartbeatInterval = heartbeatInterval;
        }

        public async Task StreamAsync(
            string sessionId,
            HttpResponse response,
            IReadOnlyList<OrbitalObject> initialDebris,
            CancellationToken ct)
        {
            response.Headers["Content-Type"] = "text/event-stream";
            response.Headers["Cache-Control"] = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";

            await WriteEventAsync(response, "debris_update", new
            {
                objects = initialDebris,
                count = initialDebris.Count,
                timestamp = DateTime.UtcNow,
            }, ct);

            var lastDebris = DateTime.UtcNow;
            var lastHeartbeat = DateTime.UtcNow;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;

                    if (now - lastHeartbeat >= _heartbeatInterval)
                    {
                        await WriteEventAsync(response, "heartbeat", new
                        {
                            session_id = sessionId,
                            timestamp = now,
                        }, ct);
                        lastHeartbeat = now;
                    }

                    if (now - lastDebris >= _debrisInterval)
                    {
                        await WriteEventAsync(response, "debris_update", new
                        {
                            objects = initialDebris,
                            count = initialDebris.Count,
                            timestamp = now,
                        }, ct);
                        lastDebris = now;
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SSE stream cancelled for {SessionId}", sessionId);
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

20. **[ ] Run tests — confirm GREEN**

21. **[ ] Commit:** `feat(services): add MissionSseService for real-time streaming`

---

### Phase 6: DI registration + smoke (commit 6)

22. **[ ] Register all services in Program.cs**

    ```csharp
    builder.Services.Configure<SessionStoreOptions>(
        builder.Configuration.GetSection("Sessions"));

    builder.Services.AddScoped<IConjunctionDetector, ConjunctionDetector>();
    builder.Services.AddScoped<ILaunchWindowCalculator, LaunchWindowCalculator>();
    builder.Services.AddScoped<IMissionSimulationService, MissionSimulationService>();
    builder.Services.AddSingleton<SessionStore>();
    builder.Services.AddScoped<MissionSseService>();
    ```

23. **[ ] Add default Sessions section to appsettings.json**
    ```json
    "Sessions": { "TtlMinutes": 30 }
    ```

24. **[ ] Run full test suite + `dotnet build`** — all green, no warnings.

25. **[ ] Commit:** `chore(di): wire mission simulation services into DI container`

---

## Testing Strategy

- **Unit tests (xUnit + FluentAssertions):** every service has a dedicated test file. Target coverage 80%+.
- **Integration tests:** deferred to plan-07. Services here are pure (no I/O, no HTTP) except `MissionSseService` exercised via `DefaultHttpContext`.
- **Coverage check:** `dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura`

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Haversine math errors yield wrong distances | Tests use precomputed values at the equator (lat≈0) with `BeApproximately` tolerance |
| `LaunchWindowCalculator` same risk for every slot | MVP simplification — real variability arrives when SGP4 runs per slot post-MVP |
| `SessionStore` memory growth under load | TTL purge runs on every `GetSession`/`CreateSession`; acceptable for MVP |
| SSE tests flaky on slow CI | Constructor overload accepts short intervals; CTS cancels within 150-400 ms |
| `ArgumentException` vs `DomainException` | Tests pin `ArgumentException`; controller layer maps to HTTP 404 |
| Risk score formula misalignment with mobile | Constants `SAFE_KM=25`, `MAX_KM=200` documented in code comments |

---

## Success Criteria

- [ ] All 5 service test files exist (6+6+5+5+4 = 26 tests minimum)
- [ ] `dotnet test` passes with 0 failures
- [ ] Coverage on `MissionClear.Api/Services/**` ≥ 80%
- [ ] `dotnet build` clean (0 warnings, 0 errors)
- [ ] `ConjunctionDetector` correctly classifies: Critical < 1km, High < 5km, Medium < 10km
- [ ] `LaunchWindowCalculator` returns sorted, capped, recommended-flagged windows
- [ ] `MissionSimulationService` throws `ArgumentException` for unknown destinations and produces 10-point trajectories
- [ ] `SessionStore` is thread-safe (`ConcurrentDictionary`), enforces TTL, exposes test-seam clock
- [ ] `MissionSseService` writes spec-conformant `event:`/`data:`/blank-line frames and cancels cleanly
- [ ] All 5 services registered in DI (3 scoped, 1 singleton, 1 scoped streaming)
- [ ] 6 atomic commits with conventional-commit messages

---

## Relevant Files

- `MissionClear.Api/Interfaces/IConjunctionDetector.cs`
- `MissionClear.Api/Services/ConjunctionDetector.cs`
- `MissionClear.Api/Interfaces/ILaunchWindowCalculator.cs`
- `MissionClear.Api/Services/LaunchWindowCalculator.cs`
- `MissionClear.Api/Interfaces/IMissionSimulationService.cs`
- `MissionClear.Api/Services/MissionSimulationService.cs`
- `MissionClear.Api/Services/SessionStore.cs`
- `MissionClear.Api/Services/SessionStoreOptions.cs`
- `MissionClear.Api/Services/MissionSseService.cs`
- `MissionClear.Api/Program.cs`
- `MissionClear.Api/appsettings.json`
- `MissionClear.Tests/Services/ConjunctionDetectorTests.cs`
- `MissionClear.Tests/Services/LaunchWindowCalculatorTests.cs`
- `MissionClear.Tests/Services/MissionSimulationServiceTests.cs`
- `MissionClear.Tests/Services/SessionStoreTests.cs`
- `MissionClear.Tests/Services/MissionSseServiceTests.cs`
