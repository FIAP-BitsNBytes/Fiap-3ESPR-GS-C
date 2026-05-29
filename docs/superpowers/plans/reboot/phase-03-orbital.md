# Phase 03 — Orbital Engine

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Implementar OrbitalCache, DataAggregatorService, OrbitalEngineService (SGP4 stub), TleIngestionService.

**Nota:** Esta fase é baseada no plan-03-orbital.md original. As mudanças em relação ao original são:
- `DataAggregatorService` depende de `IHttpClientFactory` (sem alteração)
- `TleIngestionService` é BackgroundService registrado no DI (sem alteração)
- Nenhum acesso a repositório ou banco de dados aqui — orbital é cache-only

---

### Task 1: Interfaces de Serviços Orbitais

**Files:**
- Create: `MissionClear.Api/Services/Interfaces/IOrbitalCache.cs`
- Create: `MissionClear.Api/Services/Interfaces/IDataAggregatorService.cs`
- Create: `MissionClear.Api/Services/Interfaces/IOrbitalEngineService.cs`

- [ ] **Step 1: Criar diretório de interfaces**

```powershell
mkdir MissionClear.Api/Services
mkdir MissionClear.Api/Services/Interfaces
```

- [ ] **Step 2: Escrever IOrbitalCache.cs**

```csharp
using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface IOrbitalCache
{
    bool IsReady { get; }
    DateTime? LastFetch { get; }
    DateTime? LastPropagation { get; }
    IReadOnlyList<OrbitalObject> GetAll();
    OrbitalObject? GetById(string id);
    void Update(IReadOnlyList<OrbitalObject> objects);
    int Count { get; }
}
```

- [ ] **Step 3: Escrever IDataAggregatorService.cs**

```csharp
using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface IDataAggregatorService
{
    Task<IReadOnlyList<OrbitalObject>> FetchAndMergeAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Escrever IOrbitalEngineService.cs**

```csharp
using MissionClear.Api.Models;

namespace MissionClear.Api.Services.Interfaces;

public interface IOrbitalEngineService
{
    OrbitalObject Propagate(OrbitalObject raw, DateTime atTime);
    IReadOnlyList<OrbitalObject> PropagateAll(IReadOnlyList<OrbitalObject> objects, DateTime atTime);
}
```

---

### Task 2: OrbitalCache

**Files:**
- Create: `MissionClear.Api/Services/OrbitalCache.cs`

- [ ] **Step 1: Escrever OrbitalCache.cs**

```csharp
using System.Collections.Concurrent;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class OrbitalCache : IOrbitalCache
{
    private volatile IReadOnlyList<OrbitalObject> _objects = [];
    private readonly ConcurrentDictionary<string, OrbitalObject> _index = new();

    public bool IsReady { get; private set; }
    public DateTime? LastFetch { get; private set; }
    public DateTime? LastPropagation { get; private set; }
    public int Count => _objects.Count;

    public IReadOnlyList<OrbitalObject> GetAll() => _objects;

    public OrbitalObject? GetById(string id) =>
        _index.TryGetValue(id, out var obj) ? obj : null;

    public void Update(IReadOnlyList<OrbitalObject> objects)
    {
        var now = DateTime.UtcNow;
        var filtered = objects
            .Where(o => o.AltitudeKm >= 200 && o.AltitudeKm <= 2000)
            .Where(o => o.UpdatedAt > now.AddDays(-7))
            .ToList();

        _index.Clear();
        foreach (var obj in filtered)
            _index[obj.Id] = obj;

        _objects = filtered.AsReadOnly();
        IsReady = filtered.Count > 0;

        if (objects.Any(o => o.TleLine1 != null))
            LastFetch = now;
        else
            LastPropagation = now;
    }
}
```

- [ ] **Step 2: Escrever testes para OrbitalCache**

Em `MissionClear.Tests/Services/OrbitalCacheTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;

namespace MissionClear.Tests.Services;

public sealed class OrbitalCacheTests
{
    private static OrbitalObject MakeObject(string id, double altKm = 400) => new(
        id, $"OBJ-{id}", "debris", 0, 0, altKm, 7.5, "celestrak", DateTime.UtcNow);

    [Fact]
    public void IsReady_IsFalse_BeforeFirstUpdate()
    {
        var cache = new OrbitalCache();
        cache.IsReady.Should().BeFalse();
    }

    [Fact]
    public void Update_SetsIsReady_WhenObjectsAdded()
    {
        var cache = new OrbitalCache();
        cache.Update([MakeObject("1")]);
        cache.IsReady.Should().BeTrue();
    }

    [Fact]
    public void GetById_ReturnsObject_WhenExists()
    {
        var cache = new OrbitalCache();
        cache.Update([MakeObject("42")]);
        cache.GetById("42").Should().NotBeNull();
    }

    [Fact]
    public void Update_FiltersOutOfLEOAltitudes()
    {
        var cache = new OrbitalCache();
        cache.Update([MakeObject("low", 100), MakeObject("ok", 400), MakeObject("high", 3000)]);
        cache.Count.Should().Be(1);
        cache.GetById("ok").Should().NotBeNull();
    }

    [Fact]
    public void Update_FiltersStalObjects()
    {
        var cache = new OrbitalCache();
        var stale = MakeObject("stale") with { UpdatedAt = DateTime.UtcNow.AddDays(-8) };
        cache.Update([stale]);
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void GetAll_ReturnsAllLEOObjects()
    {
        var cache = new OrbitalCache();
        cache.Update([MakeObject("1"), MakeObject("2"), MakeObject("3")]);
        cache.GetAll().Should().HaveCount(3);
    }

    [Fact]
    public void Update_Replaces_PreviousObjects()
    {
        var cache = new OrbitalCache();
        cache.Update([MakeObject("old")]);
        cache.Update([MakeObject("new1"), MakeObject("new2")]);
        cache.Count.Should().Be(2);
        cache.GetById("old").Should().BeNull();
    }
}
```

---

### Task 3: OrbitalEngineService (SGP4 stub)

**Files:**
- Create: `MissionClear.Api/Services/OrbitalEngineService.cs`

- [ ] **Step 1: Escrever OrbitalEngineService.cs**

Nota: SGP4 real não está disponível via NuGet com API compatível. Usamos stub determinístico baseado em FNV hash do NORAD ID. O comportamento varia entre propagações para simular variação orbital.

```csharp
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;

namespace MissionClear.Api.Services;

public sealed class OrbitalEngineService : IOrbitalEngineService
{
    public OrbitalObject Propagate(OrbitalObject raw, DateTime atTime)
    {
        var seed = FnvHash(raw.Id) ^ (uint)atTime.Ticks;
        var rng = new Random((int)seed);

        var latDelta = (rng.NextDouble() - 0.5) * 2.0;
        var lonDelta = (rng.NextDouble() - 0.5) * 4.0;
        var altDelta = (rng.NextDouble() - 0.5) * 1.0;

        var lat = Math.Clamp(raw.Latitude + latDelta, -90, 90);
        var lon = ((raw.Longitude + lonDelta + 180) % 360) - 180;
        var alt = Math.Clamp(raw.AltitudeKm + altDelta, 200, 2000);

        return raw with
        {
            Latitude = Math.Round(lat, 4),
            Longitude = Math.Round(lon, 4),
            AltitudeKm = Math.Round(alt, 2),
            UpdatedAt = atTime
        };
    }

    public IReadOnlyList<OrbitalObject> PropagateAll(IReadOnlyList<OrbitalObject> objects, DateTime atTime) =>
        objects.Select(o => Propagate(o, atTime)).ToList().AsReadOnly();

    private static uint FnvHash(string input)
    {
        const uint FnvPrime = 16777619;
        const uint OffsetBasis = 2166136261;
        var hash = OffsetBasis;
        foreach (var c in input)
            hash = (hash ^ c) * FnvPrime;
        return hash;
    }
}
```

- [ ] **Step 2: Testes para OrbitalEngineService**

Em `MissionClear.Tests/Services/OrbitalEngineServiceTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Models;
using MissionClear.Api.Services;

namespace MissionClear.Tests.Services;

public sealed class OrbitalEngineServiceTests
{
    private readonly OrbitalEngineService _engine = new();

    private static OrbitalObject MakeRaw(string id = "12345") => new(
        id, "TEST DEB", "debris", 45.0, 90.0, 500.0, 7.5, "celestrak", DateTime.UtcNow);

    [Fact]
    public void Propagate_ReturnsNewObject_SameId()
    {
        var raw = MakeRaw();
        var propagated = _engine.Propagate(raw, DateTime.UtcNow);
        propagated.Id.Should().Be(raw.Id);
    }

    [Fact]
    public void Propagate_ProducesLatitudeInValidRange()
    {
        var raw = MakeRaw();
        var propagated = _engine.Propagate(raw, DateTime.UtcNow);
        propagated.Latitude.Should().BeInRange(-90, 90);
    }

    [Fact]
    public void Propagate_ProducesAltitudeInLEORange()
    {
        var raw = MakeRaw();
        var propagated = _engine.Propagate(raw, DateTime.UtcNow);
        propagated.AltitudeKm.Should().BeInRange(200, 2000);
    }

    [Fact]
    public void Propagate_IsDeterministic_ForSameTime()
    {
        var raw = MakeRaw();
        var t = new DateTime(2025, 5, 27, 14, 0, 0, DateTimeKind.Utc);
        var a = _engine.Propagate(raw, t);
        var b = _engine.Propagate(raw, t);
        a.Latitude.Should().Be(b.Latitude);
        a.Longitude.Should().Be(b.Longitude);
    }

    [Fact]
    public void PropagateAll_ReturnsAllObjects()
    {
        var objects = Enumerable.Range(1, 5)
            .Select(i => MakeRaw(i.ToString()))
            .ToList();
        var result = _engine.PropagateAll(objects, DateTime.UtcNow);
        result.Should().HaveCount(5);
    }
}
```

---

### Task 4: DataAggregatorService

**Files:**
- Create: `MissionClear.Api/Services/DataAggregatorService.cs`

Detalhes completos em plan-03-orbital.md original (Task 4). Delta para este reboot: nenhum.

- [ ] **Step 1: Implementar e testar conforme plan-03-orbital.md Task 4**

Consultar `docs/superpowers/plans/plan-03-orbital.md` para código completo com MockHttpMessageHandler.

---

### Task 5: TleIngestionService (BackgroundService)

**Files:**
- Create: `MissionClear.Api/Services/TleIngestionService.cs`

Detalhes completos em plan-03-orbital.md original (Task 5). Nenhuma mudança.

- [ ] **Step 1: Implementar conforme plan-03-orbital.md Task 5**

- [ ] **Step 2: Rodar todos os testes orbitais**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "Category=Orbital" -v normal
```

- [ ] **Step 3: Commit**

```powershell
git add MissionClear.Api/Services/
git commit -m "feat(orbital): OrbitalCache, OrbitalEngine SGP4 stub, DataAggregator, TleIngestion"
```
