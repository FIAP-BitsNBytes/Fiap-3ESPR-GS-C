# Plan 03 — Orbital Engine (Cache + SGP4 + Ingestion)

**Execution order:** After plan-00 + plan-02. Parallel with plan-04.
**Estimated time:** 90 minutes.
**Goal:** Implementar o motor orbital completo — cache thread-safe de TLEs e objetos propagados, ingestão de CelesTrak/KeepTrack, propagação SGP4 com stub funcional e serviço de background que mantém 30k+ objetos atualizados em memória.
**Dependencies:** `plan-00-scaffolding.md`, `plan-02-models.md`
**Unlocks:** `plan-05-mission.md`, `plan-07-controllers.md`

---

## Contexto

Este módulo é o coração do sistema. Tudo o que envolve detritos, conjunções e janelas de lançamento depende do `OrbitalCache` estar populado. A arquitetura é:

```
TleIngestionService (BackgroundService)
        │
        ├── a cada 60min ──> DataAggregatorService.FetchAndStoreAsync()
        │                          │
        │                          ├── HTTP GET CelesTrak (obrigatório)
        │                          ├── HTTP GET KeepTrack  (opcional, timeout 5s)
        │                          └── OrbitalCache.UpdateTles(...)
        │
        └── a cada 60s  ──> OrbitalEngineService.PropagateAll(cache.GetTles(), now)
                                   │
                                   └── OrbitalCache.UpdatePropagatedObjects(...)
```

**Regras invioláveis:**
- KeepTrack nunca derruba o sistema (try/catch + log warning).
- CelesTrak vence em conflito de NORAD_CAT_ID.
- TLEs com mais de 7 dias são purgados.
- SGP4 com erro retorna `null` — `PropagateAll` pula falhas.
- `IsReady = propagated.Count > 0` (não basta ter TLE, precisa ter posição propagada).

---

## Task 3.1: OrbitalCache (TDD)

**Files:**
- Create: `MissionClear.Api/Cache/OrbitalCache.cs`
- Create: `MissionClear.Tests/Cache/OrbitalCacheTests.cs`

### Step 1: Escrever os testes primeiro (RED)

Criar `MissionClear.Tests/Cache/OrbitalCacheTests.cs`:

```csharp
using FluentAssertions;
using MissionClear.Api.Cache;
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Models.Tle;
using Xunit;

namespace MissionClear.Tests.Cache;

public class OrbitalCacheTests
{
    private static TleRecord Tle(string id, string source = "celestrak", string name = "TEST")
        => new(id, name, "1 line1", "2 line2", source, DateTime.UtcNow);

    private static OrbitalObject Obj(string id)
        => new(id, "TEST", "satellite", 0, 0, 500, 7.5, "celestrak", DateTime.UtcNow);

    [Fact]
    public void IsReady_WhenEmpty_ReturnsFalse()
    {
        var cache = new OrbitalCache();
        cache.IsReady.Should().BeFalse();
    }

    [Fact]
    public void UpdateTles_StoresAllRecords_AndUpdatesLastFetch()
    {
        var cache = new OrbitalCache();
        var tles = new[] { Tle("1"), Tle("2"), Tle("3") };

        cache.UpdateTles(tles);

        cache.TleCount.Should().Be(3);
        cache.LastFetch.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        cache.GetTleById("2").Should().NotBeNull();
    }

    [Fact]
    public void UpdateTles_CelestrakWinsOverKeeptrack_OnConflict()
    {
        var cache = new OrbitalCache();
        cache.UpdateTles(new[] { Tle("42", source: "celestrak", name: "FROM_CELESTRAK") });

        // Try to overwrite with keeptrack
        cache.UpdateTles(new[] { Tle("42", source: "keeptrack", name: "FROM_KEEPTRACK") });

        cache.GetTleById("42")!.Source.Should().Be("celestrak");
        cache.GetTleById("42")!.Name.Should().Be("FROM_CELESTRAK");
    }

    [Fact]
    public void UpdateTles_CelestrakOverwritesKeeptrack()
    {
        var cache = new OrbitalCache();
        cache.UpdateTles(new[] { Tle("42", source: "keeptrack", name: "OLD") });

        cache.UpdateTles(new[] { Tle("42", source: "celestrak", name: "NEW") });

        cache.GetTleById("42")!.Source.Should().Be("celestrak");
        cache.GetTleById("42")!.Name.Should().Be("NEW");
    }

    [Fact]
    public void UpdateTles_PurgesRecordsOlderThanSevenDays()
    {
        var cache = new OrbitalCache();
        var stale = new TleRecord("old", "X", "1", "2", "celestrak", DateTime.UtcNow.AddDays(-8));
        var fresh = new TleRecord("new", "Y", "1", "2", "celestrak", DateTime.UtcNow);
        cache.UpdateTles(new[] { stale });

        cache.UpdateTles(new[] { fresh });

        cache.TleCount.Should().Be(1);
        cache.GetTleById("old").Should().BeNull();
        cache.GetTleById("new").Should().NotBeNull();
    }

    [Fact]
    public void UpdatePropagatedObjects_SetsIsReadyTrue()
    {
        var cache = new OrbitalCache();

        cache.UpdatePropagatedObjects(new[] { Obj("1"), Obj("2") });

        cache.IsReady.Should().BeTrue();
        cache.GetPropagatedObjects().Should().HaveCount(2);
        cache.LastPropagation.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GetObjectById_ReturnsNullWhenMissing()
    {
        var cache = new OrbitalCache();
        cache.UpdatePropagatedObjects(new[] { Obj("1") });

        cache.GetObjectById("999").Should().BeNull();
    }
}
```

Rodar: `dotnet test --filter OrbitalCacheTests` — deve falhar (classe não existe).

### Step 2: Implementar `OrbitalCache` (GREEN)

Criar `MissionClear.Api/Cache/OrbitalCache.cs`:

```csharp
using System.Collections.Concurrent;
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Models.Tle;

namespace MissionClear.Api.Cache;

/// <summary>
/// Thread-safe in-memory cache for TLE records and propagated orbital objects.
/// CelesTrak wins on NORAD_CAT_ID conflict. TLEs older than 7 days are purged.
/// </summary>
public sealed class OrbitalCache
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, TleRecord> _tles = new();
    private volatile IReadOnlyList<OrbitalObject> _propagated = Array.Empty<OrbitalObject>();
    private readonly ConcurrentDictionary<string, OrbitalObject> _propagatedById = new();

    public DateTime? LastFetch { get; private set; }
    public DateTime? LastPropagation { get; private set; }

    public int TleCount => _tles.Count;
    public bool IsReady => _propagated.Count > 0;

    public void UpdateTles(IEnumerable<TleRecord> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        foreach (var record in incoming)
        {
            _tles.AddOrUpdate(
                record.NoradCatId,
                _ => record,
                (_, existing) =>
                {
                    // CelesTrak wins: if existing is celestrak and new is not, keep existing.
                    if (string.Equals(existing.Source, "celestrak", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(record.Source, "celestrak", StringComparison.OrdinalIgnoreCase))
                    {
                        return existing;
                    }
                    return record;
                });
        }

        // Purge stale (>7 days)
        var cutoff = DateTime.UtcNow - StaleAfter;
        foreach (var kv in _tles)
        {
            if (kv.Value.FetchedAt < cutoff)
                _tles.TryRemove(kv.Key, out _);
        }

        LastFetch = DateTime.UtcNow;
    }

    public void UpdatePropagatedObjects(IReadOnlyList<OrbitalObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);

        _propagated = objects;

        _propagatedById.Clear();
        foreach (var obj in objects)
            _propagatedById[obj.NoradCatId] = obj;

        LastPropagation = DateTime.UtcNow;
    }

    public IReadOnlyCollection<TleRecord> GetTles() => _tles.Values.ToArray();

    public TleRecord? GetTleById(string noradCatId)
        => _tles.TryGetValue(noradCatId, out var tle) ? tle : null;

    public IReadOnlyList<OrbitalObject> GetPropagatedObjects() => _propagated;

    public OrbitalObject? GetObjectById(string noradCatId)
        => _propagatedById.TryGetValue(noradCatId, out var obj) ? obj : null;
}
```

### Step 3: Verificar verde

```bash
dotnet test --filter OrbitalCacheTests
# Esperado: Passed: 7
```

### Step 4: Registrar em DI

Em `Program.cs`:

```csharp
builder.Services.AddSingleton<OrbitalCache>();
```

### Step 5: Commit

```bash
git add .
git commit -m "feat: OrbitalCache thread-safe com purga 7d e CelesTrak-wins"
```

---

## Task 3.2: DataAggregatorService (TDD)

**Files:**
- Create: `MissionClear.Api/Services/DataAggregatorService.cs`
- Create: `MissionClear.Tests/Services/DataAggregatorServiceTests.cs`
- Create: `MissionClear.Tests/Helpers/MockHttpMessageHandler.cs`

### Step 1: Helper MockHttpMessageHandler

```csharp
using System.Net;

namespace MissionClear.Tests.Helpers;

public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handler = handler;

    public static MockHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });

    public static MockHttpMessageHandler Status(HttpStatusCode status)
        => new(_ => new HttpResponseMessage(status));

    public static MockHttpMessageHandler Throws(Exception ex)
        => new(_ => throw ex);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(_handler(request));
}
```

### Step 2: Testes (RED)

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MissionClear.Api.Cache;
using MissionClear.Api.Configuration;
using MissionClear.Api.Services;
using MissionClear.Tests.Helpers;
using System.Net;
using Xunit;

namespace MissionClear.Tests.Services;

public class DataAggregatorServiceTests
{
    private static IOptions<ExternalApiSettings> Settings() =>
        Options.Create(new ExternalApiSettings
        {
            CelesTrakDebrisUrl = "https://celestrak.org/test.json",
            KeepTrackBaseUrl = "https://keeptrack.space/api/test",
            KeepTrackApiKey = "test-key",
            KeepTrackTimeoutSeconds = 5
        });

    private static IHttpClientFactory Factory(HttpMessageHandler celestrak, HttpMessageHandler? keeptrack = null)
    {
        var factory = new TestHttpClientFactory();
        factory.Register("celestrak", new HttpClient(celestrak));
        factory.Register("keeptrack", new HttpClient(keeptrack ?? MockHttpMessageHandler.Status(HttpStatusCode.NotFound)));
        return factory;
    }

    [Fact]
    public async Task FetchAndStoreAsync_ParsesValidCelesTrakResponse()
    {
        var json = """
        [
          {
            "NORAD_CAT_ID": 25544,
            "OBJECT_NAME": "ISS (ZARYA)",
            "TLE_LINE1": "1 25544U 98067A   24001.00000000  .00000000  00000-0  00000-0 0  9999",
            "TLE_LINE2": "2 25544  51.6400 000.0000 0001000 000.0000 000.0000 15.50000000000000"
          }
        ]
        """;

        var cache = new OrbitalCache();
        var sut = new DataAggregatorService(
            Factory(MockHttpMessageHandler.Json(json)),
            cache, Settings(), NullLogger<DataAggregatorService>.Instance);

        await sut.FetchAndStoreAsync(CancellationToken.None);

        cache.TleCount.Should().Be(1);
        cache.GetTleById("25544").Should().NotBeNull();
        cache.GetTleById("25544")!.Source.Should().Be("celestrak");
    }

    [Fact]
    public async Task FetchAndStoreAsync_SkipsRecordsWithEmptyTle()
    {
        var json = """
        [
          { "NORAD_CAT_ID": 1, "OBJECT_NAME": "GOOD", "TLE_LINE1": "1 ...", "TLE_LINE2": "2 ..." },
          { "NORAD_CAT_ID": 2, "OBJECT_NAME": "BAD",  "TLE_LINE1": "",       "TLE_LINE2": "2 ..." }
        ]
        """;

        var cache = new OrbitalCache();
        var sut = new DataAggregatorService(
            Factory(MockHttpMessageHandler.Json(json)),
            cache, Settings(), NullLogger<DataAggregatorService>.Instance);

        await sut.FetchAndStoreAsync(CancellationToken.None);

        cache.TleCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAndStoreAsync_ThrowsOnCelesTrakFailure()
    {
        var cache = new OrbitalCache();
        var sut = new DataAggregatorService(
            Factory(MockHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable)),
            cache, Settings(), NullLogger<DataAggregatorService>.Instance);

        var act = () => sut.FetchAndStoreAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        cache.TleCount.Should().Be(0);
    }

    [Fact]
    public async Task FetchAndStoreAsync_KeepTrackFailure_DoesNotThrow_AndKeepsCelesTrakData()
    {
        var celestrakJson = """
        [{ "NORAD_CAT_ID": 99, "OBJECT_NAME": "OK", "TLE_LINE1": "1 ...", "TLE_LINE2": "2 ..." }]
        """;

        var cache = new OrbitalCache();
        var sut = new DataAggregatorService(
            Factory(
                MockHttpMessageHandler.Json(celestrakJson),
                MockHttpMessageHandler.Throws(new HttpRequestException("KeepTrack down"))),
            cache, Settings(), NullLogger<DataAggregatorService>.Instance);

        var act = () => sut.FetchAndStoreAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        cache.TleCount.Should().Be(1);
        cache.GetTleById("99")!.Source.Should().Be("celestrak");
    }
}

internal sealed class TestHttpClientFactory : IHttpClientFactory
{
    private readonly Dictionary<string, HttpClient> _clients = new();
    public void Register(string name, HttpClient client) => _clients[name] = client;
    public HttpClient CreateClient(string name) => _clients.TryGetValue(name, out var c) ? c : new HttpClient();
}
```

### Step 3: Implementação (GREEN)

```csharp
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionClear.Api.Cache;
using MissionClear.Api.Configuration;
using MissionClear.Api.Models.Tle;

namespace MissionClear.Api.Services;

public interface IDataAggregatorService
{
    Task FetchAndStoreAsync(CancellationToken ct);
}

public sealed class DataAggregatorService : IDataAggregatorService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly OrbitalCache _cache;
    private readonly ExternalApiSettings _settings;
    private readonly ILogger<DataAggregatorService> _logger;

    public DataAggregatorService(
        IHttpClientFactory httpFactory,
        OrbitalCache cache,
        IOptions<ExternalApiSettings> settings,
        ILogger<DataAggregatorService> logger)
    {
        _httpFactory = httpFactory;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task FetchAndStoreAsync(CancellationToken ct)
    {
        var celestrak = await FetchCelesTrakAsync(ct);
        _logger.LogInformation("CelesTrak: parsed {Count} valid TLE records", celestrak.Count);

        var keeptrack = await TryFetchKeepTrackAsync(ct);
        _logger.LogInformation("KeepTrack: parsed {Count} valid TLE records", keeptrack.Count);

        // CelesTrak first so it wins on conflict.
        _cache.UpdateTles(celestrak);
        _cache.UpdateTles(keeptrack);

        _logger.LogInformation("OrbitalCache now contains {Total} TLE records", _cache.TleCount);
    }

    private async Task<IReadOnlyList<TleRecord>> FetchCelesTrakAsync(CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("celestrak");
        _logger.LogInformation("Fetching CelesTrak: {Url}", _settings.CelesTrakDebrisUrl);

        using var response = await client.GetAsync(_settings.CelesTrakDebrisUrl, ct);
        response.EnsureSuccessStatusCode();

        var records = await response.Content.ReadFromJsonAsync<List<CelesTrakGpRecord>>(cancellationToken: ct);
        if (records is null) return Array.Empty<TleRecord>();

        var now = DateTime.UtcNow;
        return records
            .Where(r => !string.IsNullOrWhiteSpace(r.TLE_LINE1)
                     && !string.IsNullOrWhiteSpace(r.TLE_LINE2)
                     && r.NORAD_CAT_ID > 0)
            .Select(r => new TleRecord(
                NoradCatId: r.NORAD_CAT_ID.ToString(),
                Name: r.OBJECT_NAME ?? $"OBJECT-{r.NORAD_CAT_ID}",
                Line1: r.TLE_LINE1!,
                Line2: r.TLE_LINE2!,
                Source: "celestrak",
                FetchedAt: now))
            .ToList();
    }

    private async Task<IReadOnlyList<TleRecord>> TryFetchKeepTrackAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.KeepTrackBaseUrl))
        {
            _logger.LogDebug("KeepTrack URL not configured — skipping");
            return Array.Empty<TleRecord>();
        }

        try
        {
            var client = _httpFactory.CreateClient("keeptrack");
            client.Timeout = TimeSpan.FromSeconds(_settings.KeepTrackTimeoutSeconds);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_settings.KeepTrackTimeoutSeconds));

            using var response = await client.GetAsync(_settings.KeepTrackBaseUrl, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("KeepTrack returned {Status} — continuing without it", response.StatusCode);
                return Array.Empty<TleRecord>();
            }

            var records = await response.Content.ReadFromJsonAsync<List<CelesTrakGpRecord>>(cancellationToken: cts.Token);
            if (records is null) return Array.Empty<TleRecord>();

            var now = DateTime.UtcNow;
            return records
                .Where(r => !string.IsNullOrWhiteSpace(r.TLE_LINE1)
                         && !string.IsNullOrWhiteSpace(r.TLE_LINE2)
                         && r.NORAD_CAT_ID > 0)
                .Select(r => new TleRecord(
                    NoradCatId: r.NORAD_CAT_ID.ToString(),
                    Name: r.OBJECT_NAME ?? $"OBJECT-{r.NORAD_CAT_ID}",
                    Line1: r.TLE_LINE1!,
                    Line2: r.TLE_LINE2!,
                    Source: "keeptrack",
                    FetchedAt: now))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KeepTrack fetch failed — system continues without it");
            return Array.Empty<TleRecord>();
        }
    }
}
```

### Step 4: DI

Em `Program.cs`:

```csharp
builder.Services.AddHttpClient("celestrak");
builder.Services.AddHttpClient("keeptrack");
builder.Services.AddScoped<IDataAggregatorService, DataAggregatorService>();
```

### Step 5: Commit

```bash
git add .
git commit -m "feat: DataAggregatorService — ingere CelesTrak + KeepTrack opcional"
```

---

## Task 3.3: OrbitalEngineService com stub determinístico + ECI math (TDD)

### Step 1: Testes (RED)

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MissionClear.Api.Models.Tle;
using MissionClear.Api.Services;
using Xunit;

namespace MissionClear.Tests.Services;

public class OrbitalEngineServiceTests
{
    private readonly OrbitalEngineService _engine =
        new(NullLogger<OrbitalEngineService>.Instance);

    private static TleRecord ValidTle(string id = "25544", string name = "ISS (ZARYA)") => new(
        NoradCatId: id,
        Name: name,
        Line1: "1 25544U 98067A   24001.00000000  .00000000  00000-0  00000-0 0  9999",
        Line2: "2 25544  51.6400 000.0000 0001000 000.0000 000.0000 15.50000000000000",
        Source: "celestrak",
        FetchedAt: DateTime.UtcNow);

    [Fact]
    public void Propagate_ValidTle_ReturnsObjectWithRealisticAltitude()
    {
        var result = _engine.Propagate(ValidTle(), DateTime.UtcNow);

        result.Should().NotBeNull();
        result!.NoradCatId.Should().Be("25544");
        result.AltitudeKm.Should().BeInRange(200, 2000);
        result.VelocityKmS.Should().BeInRange(6.5, 8.5);
        result.LatitudeDeg.Should().BeInRange(-90, 90);
        result.LongitudeDeg.Should().BeInRange(-180, 180);
    }

    [Fact]
    public void Propagate_NameContainsDeb_ClassifiesAsDebris()
    {
        var result = _engine.Propagate(ValidTle(id: "1", name: "FENGYUN 1C DEB"), DateTime.UtcNow);
        result!.Type.Should().Be("debris");
    }

    [Fact]
    public void Propagate_NameContainsRB_ClassifiesAsRocketBody()
    {
        var result = _engine.Propagate(ValidTle(id: "2", name: "FALCON 9 R/B"), DateTime.UtcNow);
        result!.Type.Should().Be("rocket_body");
    }

    [Fact]
    public void Propagate_DefaultName_ClassifiesAsSatellite()
    {
        var result = _engine.Propagate(ValidTle(id: "3", name: "STARLINK-1234"), DateTime.UtcNow);
        result!.Type.Should().Be("satellite");
    }

    [Fact]
    public void Propagate_BadTle_ReturnsNull()
    {
        var bad = new TleRecord("bad", "X", "garbage", "more garbage", "celestrak", DateTime.UtcNow);
        _engine.Propagate(bad, DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void PropagateAll_SkipsFailures_AndReturnsValid()
    {
        var inputs = new[]
        {
            ValidTle("100"),
            new TleRecord("bad", "X", "garbage", "garbage", "celestrak", DateTime.UtcNow),
            ValidTle("200"),
            ValidTle("300")
        };

        var results = _engine.PropagateAll(inputs, DateTime.UtcNow);

        results.Should().HaveCount(3);
        results.Select(o => o.NoradCatId).Should().BeEquivalentTo(new[] { "100", "200", "300" });
    }

    [Fact]
    public void Propagate_DeterministicForSameNoradAndEpoch()
    {
        var tle = ValidTle("12345");
        var epoch = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var a = _engine.Propagate(tle, epoch);
        var b = _engine.Propagate(tle, epoch);

        a!.AltitudeKm.Should().Be(b!.AltitudeKm);
        a.LatitudeDeg.Should().Be(b.LatitudeDeg);
    }
}
```

### Step 2: Implementação (GREEN)

```csharp
using Microsoft.Extensions.Logging;
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Models.Tle;

namespace MissionClear.Api.Services;

public interface IOrbitalEngineService
{
    OrbitalObject? Propagate(TleRecord tle, DateTime utcInstant);
    IReadOnlyList<OrbitalObject> PropagateAll(IEnumerable<TleRecord> tles, DateTime utcInstant);
}

/// <summary>
/// Propagates TLE records to Earth-fixed (lat/lon/alt) positions.
///
/// Currently uses a deterministic stub (SimulateOrbit) so the service works without
/// a real SGP4 package installed. To swap in real SGP4:
///   1. Install NuGet package (e.g. SGP4 or Orbit.Sgp4).
///   2. Replace the SimulateOrbit() call in Propagate() with the package call.
///   3. The EciToGeodetic() and Gst() helpers below are real and stay as-is.
/// </summary>
public sealed class OrbitalEngineService : IOrbitalEngineService
{
    private const double EarthRadiusKm = 6378.137;
    private const double EarthFlattening = 1.0 / 298.257223563;
    private const double DegToRad = Math.PI / 180.0;
    private const double RadToDeg = 180.0 / Math.PI;

    private readonly ILogger<OrbitalEngineService> _logger;

    public OrbitalEngineService(ILogger<OrbitalEngineService> logger) => _logger = logger;

    public OrbitalObject? Propagate(TleRecord tle, DateTime utcInstant)
    {
        try
        {
            if (!IsTleShapeValid(tle)) return null;

            // === SGP4 STUB — replace with real package call when installed ===
            var (xKm, yKm, zKm, vxKmS, vyKmS, vzKmS) = SimulateOrbit(tle.NoradCatId, utcInstant);
            // ================================================================

            var (latDeg, lonDeg, altKm) = EciToGeodetic(xKm, yKm, zKm, utcInstant);
            var speedKmS = Math.Sqrt(vxKmS * vxKmS + vyKmS * vyKmS + vzKmS * vzKmS);

            return new OrbitalObject(
                NoradCatId: tle.NoradCatId,
                Name: tle.Name,
                Type: ClassifyType(tle.Name),
                LatitudeDeg: latDeg,
                LongitudeDeg: lonDeg,
                AltitudeKm: altKm,
                VelocityKmS: speedKmS,
                Source: tle.Source,
                PropagatedAt: utcInstant);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SGP4 propagation failed for {Norad} ({Name})", tle.NoradCatId, tle.Name);
            return null;
        }
    }

    public IReadOnlyList<OrbitalObject> PropagateAll(IEnumerable<TleRecord> tles, DateTime utcInstant)
    {
        ArgumentNullException.ThrowIfNull(tles);

        var snapshot = tles as IReadOnlyList<TleRecord> ?? tles.ToList();
        var results = new OrbitalObject?[snapshot.Count];

        Parallel.For(0, snapshot.Count, i =>
        {
            results[i] = Propagate(snapshot[i], utcInstant);
        });

        return results.Where(o => o is not null).Cast<OrbitalObject>().ToList();
    }

    internal static bool IsTleShapeValid(TleRecord tle)
        => !string.IsNullOrWhiteSpace(tle.Line1)
        && !string.IsNullOrWhiteSpace(tle.Line2)
        && tle.Line1.StartsWith("1 ")
        && tle.Line2.StartsWith("2 ");

    internal static string ClassifyType(string name)
    {
        if (string.IsNullOrEmpty(name)) return "satellite";
        var upper = name.ToUpperInvariant();
        if (upper.Contains("DEB") || upper.Contains("DEBRIS")) return "debris";
        if (upper.Contains("R/B") || upper.Contains("ROCKET")) return "rocket_body";
        return "satellite";
    }

    /// <summary>
    /// Deterministic placeholder propagation. Replace with real SGP4 when package is wired.
    /// Returns ECI position (km) and velocity (km/s).
    /// </summary>
    internal static (double X, double Y, double Z, double Vx, double Vy, double Vz)
        SimulateOrbit(string noradCatId, DateTime utcInstant)
    {
        var seed = (uint)noradCatId.Aggregate(2166136261u, (h, c) => (h ^ c) * 16777619u);
        var rng = new Random((int)(seed & 0x7FFFFFFF));

        var inclinationDeg = rng.NextDouble() * 110.0;
        var raanDeg = rng.NextDouble() * 360.0;
        var altitudeKm = 300.0 + rng.NextDouble() * 1500.0;
        var radiusKm = EarthRadiusKm + altitudeKm;

        var muKm3S2 = 398600.4418;
        var meanMotionRadS = Math.Sqrt(muKm3S2 / Math.Pow(radiusKm, 3));
        var initialPhase = rng.NextDouble() * Math.PI * 2.0;
        var t = (utcInstant - new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        var trueAnomaly = initialPhase + meanMotionRadS * t;

        var xOrb = radiusKm * Math.Cos(trueAnomaly);
        var yOrb = radiusKm * Math.Sin(trueAnomaly);

        var speedKmS = Math.Sqrt(muKm3S2 / radiusKm);
        var vxOrb = -speedKmS * Math.Sin(trueAnomaly);
        var vyOrb = speedKmS * Math.Cos(trueAnomaly);

        var incRad = inclinationDeg * DegToRad;
        var raanRad = raanDeg * DegToRad;

        var x1 = xOrb;
        var y1 = yOrb * Math.Cos(incRad);
        var z1 = yOrb * Math.Sin(incRad);
        var vx1 = vxOrb;
        var vy1 = vyOrb * Math.Cos(incRad);
        var vz1 = vyOrb * Math.Sin(incRad);

        var x = x1 * Math.Cos(raanRad) - y1 * Math.Sin(raanRad);
        var y = x1 * Math.Sin(raanRad) + y1 * Math.Cos(raanRad);
        var z = z1;
        var vx = vx1 * Math.Cos(raanRad) - vy1 * Math.Sin(raanRad);
        var vy = vx1 * Math.Sin(raanRad) + vy1 * Math.Cos(raanRad);
        var vz = vz1;

        return (x, y, z, vx, vy, vz);
    }

    /// <summary>
    /// Convert ECI (km) at a given UTC instant to geodetic lat (deg), lon (deg), alt (km).
    /// Uses WGS-84 and iterative Bowring latitude solve.
    /// </summary>
    internal static (double LatDeg, double LonDeg, double AltKm)
        EciToGeodetic(double x, double y, double z, DateTime utcInstant)
    {
        var gstRad = Gst(utcInstant);

        var xEcef = x * Math.Cos(gstRad) + y * Math.Sin(gstRad);
        var yEcef = -x * Math.Sin(gstRad) + y * Math.Cos(gstRad);
        var zEcef = z;

        var a = EarthRadiusKm;
        var f = EarthFlattening;
        var e2 = f * (2 - f);

        var lonRad = Math.Atan2(yEcef, xEcef);
        var p = Math.Sqrt(xEcef * xEcef + yEcef * yEcef);

        var latRad = Math.Atan2(zEcef, p * (1 - e2));
        double n = 0, altKm = 0;
        for (var i = 0; i < 5; i++)
        {
            var sinLat = Math.Sin(latRad);
            n = a / Math.Sqrt(1 - e2 * sinLat * sinLat);
            altKm = p / Math.Cos(latRad) - n;
            latRad = Math.Atan2(zEcef, p * (1 - e2 * n / (n + altKm)));
        }

        return (latRad * RadToDeg, NormalizeLongitudeDeg(lonRad * RadToDeg), altKm);
    }

    internal static double Gst(DateTime utcInstant)
    {
        var jd = ToJulianDate(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc));
        var t = (jd - 2451545.0) / 36525.0;

        var gmstSec = 67310.54841
                    + (876600.0 * 3600.0 + 8640184.812866) * t
                    + 0.093104 * t * t
                    - 6.2e-6 * t * t * t;

        gmstSec %= 86400.0;
        if (gmstSec < 0) gmstSec += 86400.0;

        var gmstDeg = (gmstSec / 240.0) % 360.0;
        return gmstDeg * DegToRad;
    }

    internal static double ToJulianDate(DateTime utc)
    {
        var y = utc.Year;
        var m = utc.Month;
        var d = utc.Day;
        if (m <= 2) { y -= 1; m += 12; }
        var a = y / 100;
        var b = 2 - a + a / 4;
        var jd = Math.Floor(365.25 * (y + 4716))
               + Math.Floor(30.6001 * (m + 1))
               + d + b - 1524.5;
        var dayFraction = (utc.Hour + utc.Minute / 60.0 + (utc.Second + utc.Millisecond / 1000.0) / 3600.0) / 24.0;
        return jd + dayFraction;
    }

    internal static double NormalizeLongitudeDeg(double lonDeg)
    {
        lonDeg %= 360.0;
        if (lonDeg > 180.0) lonDeg -= 360.0;
        else if (lonDeg < -180.0) lonDeg += 360.0;
        return lonDeg;
    }
}
```

### Step 3: DI

```csharp
builder.Services.AddSingleton<IOrbitalEngineService, OrbitalEngineService>();
```

### Step 4: Commit

```bash
git add .
git commit -m "feat: OrbitalEngineService com SGP4 stub determinístico + ECI/GST math"
```

---

## Task 3.4: TleIngestionService (BackgroundService)

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MissionClear.Api.Cache;
using MissionClear.Api.Services;

namespace MissionClear.Api.Services.Background;

/// <summary>
/// Owns the orbital data lifecycle:
///   - On startup: one immediate fetch + propagation cycle.
///   - Every 60 minutes: refresh TLEs from CelesTrak (+ KeepTrack if available).
///   - Every 60 seconds: re-propagate all cached TLEs to current time.
///
/// Never crashes the host. Each loop iteration is exception-isolated.
/// </summary>
public sealed class TleIngestionService : BackgroundService
{
    private static readonly TimeSpan FetchInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan PropagateInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceProvider _services;
    private readonly OrbitalCache _cache;
    private readonly ILogger<TleIngestionService> _logger;

    public TleIngestionService(IServiceProvider services, OrbitalCache cache, ILogger<TleIngestionService> logger)
    {
        _services = services;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TleIngestionService starting — running initial cycle");

        var initialFetchOk = await SafeFetchAsync(stoppingToken);
        if (!initialFetchOk)
            _logger.LogCritical("Initial CelesTrak fetch FAILED — cache is empty. Will retry in 60 minutes.");

        await SafePropagateAsync(stoppingToken);

        await Task.WhenAll(
            RunFetchLoopAsync(stoppingToken),
            RunPropagateLoopAsync(stoppingToken));
    }

    private async Task RunFetchLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(FetchInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SafeFetchAsync(ct);
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    private async Task RunPropagateLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PropagateInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SafePropagateAsync(ct);
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    private async Task<bool> SafeFetchAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("TleIngestion: starting TLE fetch cycle");
            using var scope = _services.CreateScope();
            var aggregator = scope.ServiceProvider.GetRequiredService<IDataAggregatorService>();
            await aggregator.FetchAndStoreAsync(ct);
            _logger.LogInformation("TleIngestion: fetch complete — {Count} TLEs", _cache.TleCount);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TleIngestion: fetch cycle failed — will retry next interval");
            return false;
        }
    }

    private async Task SafePropagateAsync(CancellationToken ct)
    {
        try
        {
            var tles = _cache.GetTles();
            if (tles.Count == 0) { _logger.LogDebug("No TLEs to propagate yet"); return; }

            using var scope = _services.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<IOrbitalEngineService>();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var propagated = await Task.Run(() => engine.PropagateAll(tles, DateTime.UtcNow), ct);
            sw.Stop();

            _cache.UpdatePropagatedObjects(propagated);
            _logger.LogInformation(
                "TleIngestion: propagated {Count}/{Total} objects in {Ms} ms",
                propagated.Count, tles.Count, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TleIngestion: propagation cycle failed — will retry next interval");
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("TleIngestionService stopping");
        await base.StopAsync(ct);
    }
}
```

### Step 2: DI

```csharp
builder.Services.AddHostedService<TleIngestionService>();
```

### Step 3: Commit

```bash
git add .
git commit -m "feat: TleIngestionService — background fetch 60min + propagate 60s"
```

---

## Checklist Final do Plano 03

- [ ] `OrbitalCache` thread-safe, CelesTrak vence, purga 7d
- [ ] `DataAggregatorService` ingere CelesTrak (obrigatório) + KeepTrack (opcional, timeout 5s)
- [ ] `OrbitalEngineService` com `Propagate`, `PropagateAll`, ECI→Geodetic, GST, classificação de tipo
- [ ] SGP4 stub `SimulateOrbit` determinístico (mesma posição para mesma seed + epoch)
- [ ] `TleIngestionService` BackgroundService: fetch 60min, propaga 60s, primeira execução imediata
- [ ] Serviço nunca crasha; cada loop tem try/catch isolado
- [ ] Log CRITICAL no fetch inicial falho (sem derrubar)
- [ ] 18+ testes verdes (7 cache + 4 aggregator + 7 engine)
- [ ] DI registrado em `Program.cs`
- [ ] 4 commits atômicos no histórico

## Risks & Mitigations

| Risco | Mitigação |
|---|---|
| CelesTrak fora do ar | `EnsureSuccessStatusCode` lança; loop loga error e retry em 60min |
| Pacote SGP4 indisponível | Stub `SimulateOrbit` mantém o sistema funcional para testes/demo |
| Propagação > 60s para 30k objetos | `Parallel.For` em `PropagateAll`; stub é O(1) por objeto |
| KeepTrack lento/instável | Timeout 5s via `CancellationTokenSource` + try/catch que retorna lista vazia |
| Cache crescer indefinidamente | Purga automática de TLEs > 7 dias em cada `UpdateTles` |
| Race entre fetch e propagate | `ConcurrentDictionary` + `volatile IReadOnlyList` garantem leituras seguras |
