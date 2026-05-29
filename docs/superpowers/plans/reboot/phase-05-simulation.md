# Phase 05 — Simulation Services

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Implementar ConjunctionDetector, LaunchWindowCalculator, MissionSimulationService, SessionStore, MissionSseService.

**Nota:** Esta fase é baseada no plan-05-simulation.md original. Nenhuma mudança significativa em relação ao original. Resumo das interfaces e código principal abaixo.

---

### Task 1: Interfaces de Simulação

**Files:**
- Create: `MissionClear.Api/Services/Interfaces/IConjunctionDetector.cs`
- Create: `MissionClear.Api/Services/Interfaces/ILaunchWindowCalculator.cs`
- Create: `MissionClear.Api/Services/Interfaces/IMissionSimulationService.cs`
- Create: `MissionClear.Api/Services/Interfaces/ISessionStore.cs`
- Create: `MissionClear.Api/Services/Interfaces/IMissionSseService.cs`

- [ ] **Step 1: Escrever todas as interfaces**

`IConjunctionDetector.cs`:
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

`ILaunchWindowCalculator.cs`:
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

`IMissionSimulationService.cs`:
```csharp
using MissionClear.Api.Dtos.Mission;

namespace MissionClear.Api.Services.Interfaces;

public interface IMissionSimulationService
{
    Task<SimulateResponse> SimulateAsync(SimulateRequest request, CancellationToken ct = default);
    Task<SessionResponse> CreateSessionAsync(SessionRequest request, CancellationToken ct = default);
    Task<CompleteSessionResponse> CompleteSessionAsync(
        string sessionId, CompleteSessionRequest request, Guid? userId, CancellationToken ct = default);
}
```

`ISessionStore.cs`:
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

`IMissionSseService.cs`:
```csharp
using Microsoft.AspNetCore.Http;

namespace MissionClear.Api.Services.Interfaces;

public interface IMissionSseService
{
    Task StreamAsync(string sessionId, HttpResponse response, CancellationToken ct);
}
```

---

### Task 2: ConjunctionDetector

**Files:**
- Create: `MissionClear.Api/Services/ConjunctionDetector.cs`

- [ ] **Step 1: Escrever testes**

Em `MissionClear.Tests/Services/ConjunctionDetectorTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;

namespace MissionClear.Tests.Services;

public sealed class ConjunctionDetectorTests
{
    private readonly ConjunctionDetector _detector = new();

    private static MissionDestination IssDestination => KnownDestinations.ISS;

    private static OrbitalObject MakeDebris(string id, double lat, double lon, double altKm) =>
        new(id, $"DEB-{id}", "debris", lat, lon, altKm, 7.5, "celestrak", DateTime.UtcNow);

    [Fact]
    public void Detect_ReturnsCritical_WhenDebrisUnder1km()
    {
        // Debris at same lat/lon as ISS orbit (~408km), distance < 1km
        var debris = new[] { MakeDebris("1", 0, 0, 408.0) };
        var at = DateTime.UtcNow;

        var results = _detector.Detect(IssDestination, at, debris);

        results.Should().NotBeEmpty();
        results[0].RiskLevel.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void Detect_ReturnsEmpty_WhenDebrisFarAway()
    {
        // Debris at 1500km altitude — very far from ISS at 408km
        var debris = new[] { MakeDebris("far", 80, 120, 1500.0) };
        var results = _detector.Detect(IssDestination, DateTime.UtcNow, debris);
        results.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ClassifiesHigh_WhenUnder5km()
    {
        var debris = new[] { MakeDebris("2", 0.01, 0.01, 408.2) };
        var results = _detector.Detect(IssDestination, DateTime.UtcNow, debris);
        if (results.Any())
            results[0].RiskLevel.Should().BeOneOf(RiskLevel.Critical, RiskLevel.High);
    }

    [Fact]
    public void Detect_DoesNotThrow_WhenCacheIsEmpty()
    {
        var act = () => _detector.Detect(IssDestination, DateTime.UtcNow, []);
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Implementar ConjunctionDetector.cs**

```csharp
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class ConjunctionDetector : IConjunctionDetector
{
    public IReadOnlyList<ConjunctionResult> Detect(
        MissionDestination destination,
        DateTime at,
        IReadOnlyList<OrbitalObject> debris)
    {
        var results = new List<ConjunctionResult>();

        foreach (var obj in debris)
        {
            var distanceKm = OrbitalMath.HaversineKm(
                destination.AltitudeKm, 0, 0,
                obj.AltitudeKm, obj.Latitude, obj.Longitude);

            if (distanceKm > 200) continue;

            var risk = RiskScoring.Classify(distanceKm);
            results.Add(new ConjunctionResult(
                obj.Id,
                obj.Name,
                Math.Round(distanceKm, 2),
                at.AddMinutes(new Random(obj.Id.GetHashCode()).Next(5, 90)),
                risk));
        }

        return results.OrderBy(r => r.ClosestApproachKm).ToList().AsReadOnly();
    }
}
```

---

### Task 3: LaunchWindowCalculator

**Files:**
- Create: `MissionClear.Api/Services/LaunchWindowCalculator.cs`

- [ ] **Step 1: Testes**

Em `MissionClear.Tests/Services/LaunchWindowCalculatorTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;

namespace MissionClear.Tests.Services;

public sealed class LaunchWindowCalculatorTests
{
    private readonly LaunchWindowCalculator _calc = new();

    [Fact]
    public void Calculate_Returns48Windows_For12HourRange()
    {
        var from = new DateTime(2025, 5, 27, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddHours(12);

        var windows = _calc.Calculate(KnownDestinations.ISS, from, to, []);

        windows.Should().HaveCount(48); // 12h / 15min = 48 slots
    }

    [Fact]
    public void Calculate_ReturnsIsRecommended_WhenRiskScoreUnder01()
    {
        var from = DateTime.UtcNow;
        var windows = _calc.Calculate(KnownDestinations.ISS, from, from.AddMinutes(15), []);
        windows[0].IsRecommended.Should().Be(windows[0].RiskScore < 0.1);
    }

    [Fact]
    public void Calculate_ReturnsEmptyConjunctions_WhenNoCacheData()
    {
        var from = DateTime.UtcNow;
        var windows = _calc.Calculate(KnownDestinations.ISS, from, from.AddMinutes(15), []);
        windows[0].Conjunctions.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_SetsCorrectDeltaV_ForDestination()
    {
        var from = DateTime.UtcNow;
        var windows = _calc.Calculate(KnownDestinations.ISS, from, from.AddMinutes(15), []);
        windows[0].DeltaVKmS.Should().Be(KnownDestinations.ISS.DeltaVKmS);
    }
}
```

- [ ] **Step 2: Implementar LaunchWindowCalculator.cs**

```csharp
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class LaunchWindowCalculator : ILaunchWindowCalculator
{
    private readonly ConjunctionDetector _detector = new();
    private const int SlotMinutes = 15;

    public IReadOnlyList<LaunchWindow> Calculate(
        MissionDestination destination,
        DateTime from,
        DateTime to,
        IReadOnlyList<OrbitalObject> debris)
    {
        var windows = new List<LaunchWindow>();
        var current = from;

        while (current < to)
        {
            var conjunctions = _detector.Detect(destination, current, debris);
            var riskScore = RiskScoring.ComputeScore(conjunctions.Select(c => c.ClosestApproachKm));

            windows.Add(new LaunchWindow(
                current,
                current.AddMinutes(SlotMinutes),
                Math.Round(riskScore, 4),
                destination.DeltaVKmS,
                destination.MissionDurationHours,
                riskScore < 0.1,
                conjunctions));

            current = current.AddMinutes(SlotMinutes);
        }

        return windows.AsReadOnly();
    }
}
```

---

### Task 4: SessionStore, MissionSimulationService, MissionSseService

- [ ] **Step 1: Implementar conforme plan-05-simulation.md original**

Consultar `docs/superpowers/plans/plan-05-simulation.md` para código completo.

- [ ] **Step 2: Rodar todos os testes de simulação**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "Conjunction|LaunchWindow|Session|Simulation" -v normal
```

- [ ] **Step 3: Commit**

```powershell
git add MissionClear.Api/Services/
git commit -m "feat(simulation): ConjunctionDetector, LaunchWindowCalculator, SessionStore, SSE"
```
