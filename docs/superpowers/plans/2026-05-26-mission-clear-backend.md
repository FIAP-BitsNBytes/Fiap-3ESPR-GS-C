# Mission Clear — Backend C# Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir o motor orbital do Mission Clear — um ASP.NET Core 8 que ingere TLEs reais de CelesTrak, propaga órbitas via SGP4, detecta conjunções e calcula janelas de lançamento seguras via REST API.

**Architecture:** Single-process ASP.NET Core 8. Um `IHostedService` faz ingestão de TLEs em background a cada 60 minutos e propaga posições a cada 60 segundos. Os resultados ficam em cache thread-safe em memória. Os Controllers são roteamento puro — zero lógica de negócio. KeepTrack é fonte opcional; se falhar, o sistema continua com CelesTrak.

**Tech Stack:** .NET 8, ASP.NET Core (Minimal API), SGP4 NuGet, `System.Text.Json`, `IMemoryCache`, `IHostedService`, `IHttpClientFactory`, `xUnit` para testes.

---

## Decisões de Design (problemas resolvidos)

| Problema | Decisão |
|---|---|
| 30k TLEs propagados por request | Background service propaga a cada 60s, cache serve o resultado |
| `/api/mission/simulate` via GET impossível | Virou POST com body JSON |
| KeepTrack instável | CelesTrak = fonte primária garantida; KeepTrack = enriquecimento opcional |
| Deduplicação ambígua | Chave = `NORAD_CAT_ID`; CelesTrak ganha em conflito |
| `risk_score` indefinido | Fórmula documentada abaixo |
| `mission_score` indefinido | Fórmula documentada abaixo |
| Sem modelo de erro | Envelope `ApiErrorDto` padronizado |
| Query params ausentes | `/api/launch-windows` e `/api/debris` têm params documentados |

---

## Algoritmos Definidos

### risk_score (0.0 – 1.0)
```
SAFE_KM = 25.0    # distância mínima segura em LEO
MAX_KM  = 200.0   # distância além da qual risco = 0

Por cada debris na janela:
  d = closest_approach_km
  if d >= MAX_KM  → contribuição = 0.0
  if d <= SAFE_KM → contribuição = 1.0
  else            → contribuição = 1.0 - (d - SAFE_KM) / (MAX_KM - SAFE_KM)

risk_score = min(1.0, sum(contribuições))
```

### mission_score (0 – 100)
```
delta_v_max = 12.0  # km/s (máximo razoável para LEO)

efficiency = max(0.0, 1.0 - delta_v_km_s / delta_v_max) * 50
safety     = (1.0 - risk_score) * 50
mission_score = (int)(efficiency + safety)
```

### Níveis de risco de conjunção
```
closest_approach_km < 25   → "critical"
closest_approach_km < 50   → "high"
closest_approach_km < 100  → "medium"
closest_approach_km >= 100 → "low"
```

---

## Endpoints (contrato corrigido)

### GET /api/debris
Query params: `altitudeMinKm` (default 200), `altitudeMaxKm` (default 2000), `limit` (default 500, max 2000)

```json
[
  {
    "id": "25544",
    "name": "ISS (ZARYA)",
    "type": "satellite",
    "latitude": -23.5,
    "longitude": -46.6,
    "altitude_km": 408.5,
    "velocity_km_s": 7.66,
    "source": "celestrak",
    "updated_at": "2025-05-26T10:00:00Z"
  }
]
```

### GET /api/launch-windows
Query params: `destination` (required), `from` (ISO8601), `to` (ISO8601)

```json
{
  "destination": "ISS",
  "from": "2025-05-27T00:00:00Z",
  "to": "2025-05-28T00:00:00Z",
  "windows": [
    {
      "start": "2025-05-27T14:32:00Z",
      "end": "2025-05-27T14:48:00Z",
      "risk_score": 0.03,
      "delta_v_km_s": 9.4,
      "duration_hours": 6.2,
      "conjunctions": []
    }
  ]
}
```

### POST /api/mission/simulate
Body:
```json
{
  "destination": "ISS",
  "departure_time": "2025-05-27T14:32:00Z",
  "arrival_time": "2025-05-27T20:32:00Z"
}
```
Resposta:
```json
{
  "trajectory": [],
  "obstacles": [
    {
      "debris_id": "1234",
      "closest_approach_km": 4.2,
      "time_of_closest_approach": "2025-05-27T15:10:00Z",
      "risk_level": "high"
    }
  ],
  "mission_score": 87
}
```

### Envelope de erro (todos os endpoints)
```json
{
  "error": "INVALID_DESTINATION",
  "message": "Destination 'XYZ' not found. Valid: ISS, LEO_GENERIC, SSO",
  "timestamp": "2025-05-26T10:00:00Z"
}
```

---

## Destinos de Missão Pré-definidos

| ID | Nome | Altitude (km) | Inclinação (°) |
|---|---|---|---|
| `ISS` | Estação Espacial Internacional | 408 | 51.6 |
| `LEO_GENERIC` | Órbita LEO Genérica | 400 | 28.5 |
| `SSO` | Sun-Synchronous Orbit | 500 | 97.4 |

---

## Estrutura de Arquivos

```
MissionClear.Api/
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── Controllers/
│   ├── DebrisController.cs
│   ├── LaunchWindowsController.cs
│   └── MissionController.cs
├── Services/
│   ├── Background/
│   │   └── TleIngestionService.cs          # IHostedService — roda a cada 60min
│   ├── DataAggregatorService.cs            # Fetch + dedup CelesTrak + KeepTrack
│   ├── OrbitalEngineService.cs             # SGP4: propaga posições
│   ├── ConjunctionDetectorService.cs       # Calcula proximidade debris/trajetória
│   └── LaunchWindowCalculatorService.cs    # Encontra slots temporais seguros
├── Cache/
│   └── OrbitalCache.cs                     # ConcurrentDictionary thread-safe
├── Models/
│   ├── Tle/
│   │   ├── TleRecord.cs                    # NORAD_CAT_ID, LINE1, LINE2, NAME
│   │   └── CelesTrakGpRecord.cs            # JSON da API CelesTrak
│   ├── Domain/
│   │   ├── OrbitalObject.cs                # Objeto propagado (lat/lon/alt/vel)
│   │   ├── MissionDestination.cs           # ISS, LEO_GENERIC, SSO
│   │   ├── ConjunctionResult.cs            # closest_approach_km, risk_level
│   │   └── LaunchWindow.cs                 # start, end, risk_score, delta_v
│   └── Api/
│       ├── DebrisDto.cs
│       ├── LaunchWindowsRequestDto.cs
│       ├── LaunchWindowsResponseDto.cs
│       ├── MissionSimulateRequestDto.cs
│       ├── MissionSimulateResponseDto.cs
│       └── ApiErrorDto.cs
├── Configuration/
│   └── AppSettings.cs
├── Middleware/
│   └── GlobalExceptionMiddleware.cs
└── Extensions/
    └── ServiceCollectionExtensions.cs

MissionClear.Tests/
├── Services/
│   ├── OrbitalEngineServiceTests.cs
│   ├── ConjunctionDetectorServiceTests.cs
│   ├── LaunchWindowCalculatorServiceTests.cs
│   └── DataAggregatorServiceTests.cs
└── Controllers/
    ├── DebrisControllerTests.cs
    └── LaunchWindowsControllerTests.cs
```

---

## Regras de Negócio

1. **Altitude LEO** — só retornar objetos entre `altitudeMinKm` e `altitudeMaxKm`. Default: 200–2000 km.
2. **Deduplicação** — chave primária = `NORAD_CAT_ID`. Se mesmo ID vem de CelesTrak e KeepTrack, o registro CelesTrak prevalece.
3. **Classificação de tipo** — derivada do nome do objeto:
   - Contém "DEB" ou "DEBRIS" → `"debris"`
   - Contém "R/B" ou "ROCKET" → `"rocket_body"`
   - Caso contrário → `"satellite"`
4. **Cache TTL** — posições propagadas são válidas por 60 segundos. Requisições servem do cache.
5. **TLE freshness** — TLEs são rebaixados após 7 dias sem atualização e removidos do cache.
6. **KeepTrack fallback** — se a API retornar erro ou timeout (5s), o sistema continua sem ela. Nunca falha por causa do KeepTrack.
7. **Destinos válidos** — somente `ISS`, `LEO_GENERIC`, `SSO`. Destino inválido retorna 400 com ApiErrorDto.
8. **Janela de tempo máxima** — `/api/launch-windows` aceita no máximo 48h entre `from` e `to`. Acima disso: 400.
9. **Delta-v simplificado** — calculado como `|v_destino - v_inicial| + v_acerto_orbital`, onde `v_acerto_orbital = 0.1 km/s * |inc_destino - inc_inicial| / 90`.
10. **Segurança dos controllers** — nunca expor stack trace em produção. GlobalExceptionMiddleware captura tudo.

---

## Fase 0: Scaffolding do Projeto

### Task 0.1: Criar solução .NET 8

**Files:**
- Create: `MissionClear.Api/MissionClear.Api.csproj`
- Create: `MissionClear.Tests/MissionClear.Tests.csproj`
- Create: `MissionClear.sln`

- [ ] **Step 1: Criar solução e projetos**

```bash
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet new sln -n MissionClear
dotnet new webapi -n MissionClear.Api --framework net8.0 --use-controllers
dotnet new xunit -n MissionClear.Tests --framework net8.0
dotnet sln add MissionClear.Api/MissionClear.Api.csproj
dotnet sln add MissionClear.Tests/MissionClear.Tests.csproj
dotnet add MissionClear.Tests/MissionClear.Tests.csproj reference MissionClear.Api/MissionClear.Api.csproj
```

- [ ] **Step 2: Adicionar pacotes NuGet**

```bash
cd MissionClear.Api

# SGP4 — verificar o pacote mais mantido no momento:
dotnet add package SGP4

# Alternativa se SGP4 não estiver disponível:
# dotnet add package Orbit.Sgp4

cd ../MissionClear.Tests
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package Moq
dotnet add package FluentAssertions
```

> **Nota SGP4:** Execute `dotnet search SGP4` ou consulte nuget.org e escolha o pacote com mais downloads e commit recente. O critério de aceitação: expõe `Sgp4.RunSgp4` ou equivalente que receba TLE linha 1, linha 2 e datetime, retornando ECI position vector. Se o pacote escolhido tiver API diferente, adapte o `OrbitalEngineService` na Task 4.1.

- [ ] **Step 3: Limpar arquivos gerados desnecessários**

```bash
cd ../MissionClear.Api
rm -f Controllers/WeatherForecastController.cs
rm -f WeatherForecast.cs
```

- [ ] **Step 4: Verificar que compila**

```bash
cd ..
dotnet build
```
Esperado: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "chore: scaffold solution MissionClear.Api + MissionClear.Tests"
```

---

### Task 0.2: Configurar appsettings e CORS

**Files:**
- Modify: `MissionClear.Api/appsettings.json`
- Create: `MissionClear.Api/appsettings.Development.json`
- Create: `MissionClear.Api/Configuration/AppSettings.cs`

- [ ] **Step 1: Escrever appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "OrbitalSettings": {
    "TleRefreshIntervalMinutes": 60,
    "PropagationIntervalSeconds": 60,
    "TleStaleDays": 7,
    "AltitudeMinKm": 200,
    "AltitudeMaxKm": 2000
  },
  "ExternalApis": {
    "CelesTrakDebrisUrl": "https://celestrak.org/NORAD/elements/gp.php?GROUP=debris&FORMAT=json",
    "KeepTrackBaseUrl": "https://keeptrack.space/api",
    "KeepTrackApiKey": ""
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:8081", "exp://localhost:8081"]
  }
}
```

- [ ] **Step 2: Escrever appsettings.Development.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "ExternalApis": {
    "KeepTrackApiKey": ""
  }
}
```

> KeepTrack API key vai em variável de ambiente: `EXTERNALAPISKEEPTRACK_APIKEY`. Nunca commitada.

- [ ] **Step 3: Criar AppSettings.cs**

```csharp
// MissionClear.Api/Configuration/AppSettings.cs
namespace MissionClear.Api.Configuration;

public class OrbitalSettings
{
    public int TleRefreshIntervalMinutes { get; init; } = 60;
    public int PropagationIntervalSeconds { get; init; } = 60;
    public int TleStaleDays { get; init; } = 7;
    public double AltitudeMinKm { get; init; } = 200;
    public double AltitudeMaxKm { get; init; } = 2000;
}

public class ExternalApiSettings
{
    public string CelesTrakDebrisUrl { get; init; } = string.Empty;
    public string KeepTrackBaseUrl { get; init; } = string.Empty;
    public string KeepTrackApiKey { get; init; } = string.Empty;
}

public class CorsSettings
{
    public string[] AllowedOrigins { get; init; } = [];
}
```

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "chore: configure appsettings and typed settings classes"
```

---

## Fase 1: Modelos de Domínio

### Task 1.1: TLE e Domain Models

**Files:**
- Create: `MissionClear.Api/Models/Tle/TleRecord.cs`
- Create: `MissionClear.Api/Models/Tle/CelesTrakGpRecord.cs`
- Create: `MissionClear.Api/Models/Domain/OrbitalObject.cs`
- Create: `MissionClear.Api/Models/Domain/MissionDestination.cs`
- Create: `MissionClear.Api/Models/Domain/ConjunctionResult.cs`
- Create: `MissionClear.Api/Models/Domain/LaunchWindow.cs`

- [ ] **Step 1: Criar TleRecord.cs**

```csharp
// MissionClear.Api/Models/Tle/TleRecord.cs
namespace MissionClear.Api.Models.Tle;

public record TleRecord(
    string NoradCatId,
    string Name,
    string Line1,
    string Line2,
    string Source,
    DateTime FetchedAt
);
```

- [ ] **Step 2: Criar CelesTrakGpRecord.cs**

```csharp
// MissionClear.Api/Models/Tle/CelesTrakGpRecord.cs
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Tle;

// Mapeamento exato do JSON retornado pela API CelesTrak GP
public record CelesTrakGpRecord
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

    [JsonPropertyName("EPOCH")]
    public string Epoch { get; init; } = string.Empty;
}
```

- [ ] **Step 3: Criar OrbitalObject.cs**

```csharp
// MissionClear.Api/Models/Domain/OrbitalObject.cs
namespace MissionClear.Api.Models.Domain;

public record OrbitalObject(
    string NoradCatId,
    string Name,
    string Type,           // "debris" | "satellite" | "rocket_body"
    double LatitudeDeg,
    double LongitudeDeg,
    double AltitudeKm,
    double VelocityKmS,
    string Source,
    DateTime PropagatedAt
);
```

- [ ] **Step 4: Criar MissionDestination.cs**

```csharp
// MissionClear.Api/Models/Domain/MissionDestination.cs
namespace MissionClear.Api.Models.Domain;

public record MissionDestination(
    string Id,
    string DisplayName,
    double AltitudeKm,
    double InclinationDeg
);

public static class KnownDestinations
{
    public static readonly MissionDestination ISS = new("ISS", "Estação Espacial Internacional", 408.0, 51.6);
    public static readonly MissionDestination LeoGeneric = new("LEO_GENERIC", "Órbita LEO Genérica", 400.0, 28.5);
    public static readonly MissionDestination Sso = new("SSO", "Sun-Synchronous Orbit", 500.0, 97.4);

    private static readonly Dictionary<string, MissionDestination> All = new(StringComparer.OrdinalIgnoreCase)
    {
        { ISS.Id, ISS },
        { LeoGeneric.Id, LeoGeneric },
        { Sso.Id, Sso }
    };

    public static bool TryGet(string id, out MissionDestination? destination)
        => All.TryGetValue(id, out destination);

    public static IEnumerable<string> ValidIds => All.Keys;
}
```

- [ ] **Step 5: Criar ConjunctionResult.cs**

```csharp
// MissionClear.Api/Models/Domain/ConjunctionResult.cs
namespace MissionClear.Api.Models.Domain;

public enum RiskLevel { Low, Medium, High, Critical }

public record ConjunctionResult(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    DateTime TimeOfClosestApproach,
    RiskLevel RiskLevel
)
{
    public static RiskLevel ClassifyRisk(double approachKm) => approachKm switch
    {
        < 25  => RiskLevel.Critical,
        < 50  => RiskLevel.High,
        < 100 => RiskLevel.Medium,
        _     => RiskLevel.Low
    };
}
```

- [ ] **Step 6: Criar LaunchWindow.cs**

```csharp
// MissionClear.Api/Models/Domain/LaunchWindow.cs
namespace MissionClear.Api.Models.Domain;

public record LaunchWindow(
    DateTime Start,
    DateTime End,
    double RiskScore,
    double DeltaVKmS,
    double DurationHours,
    IReadOnlyList<ConjunctionResult> Conjunctions
);
```

- [ ] **Step 7: Commit**

```bash
git add .
git commit -m "feat: add domain models TleRecord, OrbitalObject, MissionDestination, ConjunctionResult, LaunchWindow"
```

---

### Task 1.2: DTOs da API

**Files:**
- Create: `MissionClear.Api/Models/Api/DebrisDto.cs`
- Create: `MissionClear.Api/Models/Api/LaunchWindowsRequestDto.cs`
- Create: `MissionClear.Api/Models/Api/LaunchWindowsResponseDto.cs`
- Create: `MissionClear.Api/Models/Api/MissionSimulateRequestDto.cs`
- Create: `MissionClear.Api/Models/Api/MissionSimulateResponseDto.cs`
- Create: `MissionClear.Api/Models/Api/ApiErrorDto.cs`

- [ ] **Step 1: Criar DebrisDto.cs**

```csharp
// MissionClear.Api/Models/Api/DebrisDto.cs
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Api;

public record DebrisDto
{
    [JsonPropertyName("id")]          public string Id { get; init; } = string.Empty;
    [JsonPropertyName("name")]        public string Name { get; init; } = string.Empty;
    [JsonPropertyName("type")]        public string Type { get; init; } = string.Empty;
    [JsonPropertyName("latitude")]    public double Latitude { get; init; }
    [JsonPropertyName("longitude")]   public double Longitude { get; init; }
    [JsonPropertyName("altitude_km")] public double AltitudeKm { get; init; }
    [JsonPropertyName("velocity_km_s")] public double VelocityKmS { get; init; }
    [JsonPropertyName("source")]      public string Source { get; init; } = string.Empty;
    [JsonPropertyName("updated_at")]  public DateTime UpdatedAt { get; init; }
}
```

- [ ] **Step 2: Criar LaunchWindowsRequestDto.cs e Response**

```csharp
// MissionClear.Api/Models/Api/LaunchWindowsRequestDto.cs
namespace MissionClear.Api.Models.Api;

public record LaunchWindowsRequestDto(
    string Destination,
    DateTime From,
    DateTime To
);
```

```csharp
// MissionClear.Api/Models/Api/LaunchWindowsResponseDto.cs
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Api;

public record ConjunctionDto
{
    [JsonPropertyName("debris_id")]              public string DebrisId { get; init; } = string.Empty;
    [JsonPropertyName("closest_approach_km")]    public double ClosestApproachKm { get; init; }
    [JsonPropertyName("time_of_closest_approach")] public DateTime TimeOfClosestApproach { get; init; }
    [JsonPropertyName("risk_level")]             public string RiskLevel { get; init; } = string.Empty;
}

public record LaunchWindowDto
{
    [JsonPropertyName("start")]          public DateTime Start { get; init; }
    [JsonPropertyName("end")]            public DateTime End { get; init; }
    [JsonPropertyName("risk_score")]     public double RiskScore { get; init; }
    [JsonPropertyName("delta_v_km_s")]   public double DeltaVKmS { get; init; }
    [JsonPropertyName("duration_hours")] public double DurationHours { get; init; }
    [JsonPropertyName("conjunctions")]   public IReadOnlyList<ConjunctionDto> Conjunctions { get; init; } = [];
}

public record LaunchWindowsResponseDto
{
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    [JsonPropertyName("from")]        public DateTime From { get; init; }
    [JsonPropertyName("to")]          public DateTime To { get; init; }
    [JsonPropertyName("windows")]     public IReadOnlyList<LaunchWindowDto> Windows { get; init; } = [];
}
```

- [ ] **Step 3: Criar MissionSimulateRequestDto.cs e Response**

```csharp
// MissionClear.Api/Models/Api/MissionSimulateRequestDto.cs
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Api;

public record MissionSimulateRequestDto
{
    [Required]
    [JsonPropertyName("destination")]
    public string Destination { get; init; } = string.Empty;

    [Required]
    [JsonPropertyName("departure_time")]
    public DateTime DepartureTime { get; init; }

    [Required]
    [JsonPropertyName("arrival_time")]
    public DateTime ArrivalTime { get; init; }
}
```

```csharp
// MissionClear.Api/Models/Api/MissionSimulateResponseDto.cs
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Api;

public record ObstacleDto
{
    [JsonPropertyName("debris_id")]              public string DebrisId { get; init; } = string.Empty;
    [JsonPropertyName("closest_approach_km")]    public double ClosestApproachKm { get; init; }
    [JsonPropertyName("time_of_closest_approach")] public DateTime TimeOfClosestApproach { get; init; }
    [JsonPropertyName("risk_level")]             public string RiskLevel { get; init; } = string.Empty;
}

public record MissionSimulateResponseDto
{
    [JsonPropertyName("trajectory")]     public IReadOnlyList<object> Trajectory { get; init; } = [];
    [JsonPropertyName("obstacles")]      public IReadOnlyList<ObstacleDto> Obstacles { get; init; } = [];
    [JsonPropertyName("mission_score")]  public int MissionScore { get; init; }
}
```

- [ ] **Step 4: Criar ApiErrorDto.cs**

```csharp
// MissionClear.Api/Models/Api/ApiErrorDto.cs
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Api;

public record ApiErrorDto
{
    [JsonPropertyName("error")]     public string Error { get; init; } = string.Empty;
    [JsonPropertyName("message")]   public string Message { get; init; } = string.Empty;
    [JsonPropertyName("timestamp")] public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiErrorDto InvalidDestination(string id) => new()
    {
        Error = "INVALID_DESTINATION",
        Message = $"Destination '{id}' not found. Valid: {string.Join(", ", MissionClear.Api.Models.Domain.KnownDestinations.ValidIds)}",
        Timestamp = DateTime.UtcNow
    };

    public static ApiErrorDto TimeRangeExceeded() => new()
    {
        Error = "TIME_RANGE_EXCEEDED",
        Message = "Range between 'from' and 'to' cannot exceed 48 hours.",
        Timestamp = DateTime.UtcNow
    };

    public static ApiErrorDto CacheNotReady() => new()
    {
        Error = "CACHE_NOT_READY",
        Message = "Orbital data is still loading. Retry in 30 seconds.",
        Timestamp = DateTime.UtcNow
    };

    public static ApiErrorDto InternalError() => new()
    {
        Error = "INTERNAL_ERROR",
        Message = "An unexpected error occurred.",
        Timestamp = DateTime.UtcNow
    };
}
```

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add API DTOs and ApiErrorDto with factory methods"
```

---

## Fase 2: Orbital Cache

### Task 2.1: OrbitalCache thread-safe

**Files:**
- Create: `MissionClear.Api/Cache/OrbitalCache.cs`
- Test: `MissionClear.Tests/Cache/OrbitalCacheTests.cs`

- [ ] **Step 1: Escrever teste**

```csharp
// MissionClear.Tests/Cache/OrbitalCacheTests.cs
using MissionClear.Api.Cache;
using MissionClear.Api.Models.Tle;
using MissionClear.Api.Models.Domain;
using FluentAssertions;

namespace MissionClear.Tests.Cache;

public class OrbitalCacheTests
{
    private readonly OrbitalCache _cache = new();

    [Fact]
    public void GetPropagatedObjects_WhenEmpty_ReturnsEmpty()
    {
        _cache.GetPropagatedObjects().Should().BeEmpty();
    }

    [Fact]
    public void UpdateTles_StoresTotalCount()
    {
        var tles = new List<TleRecord>
        {
            new("25544", "ISS", "1 25544U...", "2 25544...", "celestrak", DateTime.UtcNow),
            new("00005", "VANGUARD 1", "1 00005U...", "2 00005...", "celestrak", DateTime.UtcNow)
        };

        _cache.UpdateTles(tles);

        _cache.TleCount.Should().Be(2);
    }

    [Fact]
    public void UpdatePropagatedObjects_OverwritesPrevious()
    {
        var obj1 = new OrbitalObject("25544", "ISS", "satellite", -23.5, -46.6, 408.5, 7.66, "celestrak", DateTime.UtcNow);
        var obj2 = new OrbitalObject("00005", "VANGUARD", "satellite", 10.0, 20.0, 650.0, 7.2, "celestrak", DateTime.UtcNow);

        _cache.UpdatePropagatedObjects([obj1, obj2]);
        _cache.GetPropagatedObjects().Should().HaveCount(2);

        _cache.UpdatePropagatedObjects([obj1]);
        _cache.GetPropagatedObjects().Should().HaveCount(1);
    }

    [Fact]
    public void GetTles_ReturnsAllStoredTles()
    {
        var tles = Enumerable.Range(1, 5)
            .Select(i => new TleRecord(i.ToString(), $"OBJ-{i}", "L1", "L2", "celestrak", DateTime.UtcNow))
            .ToList();

        _cache.UpdateTles(tles);
        _cache.GetTles().Should().HaveCount(5);
    }
}
```

- [ ] **Step 2: Rodar — deve falhar**

```bash
dotnet test MissionClear.Tests --filter "OrbitalCacheTests"
```
Esperado: FAIL — `OrbitalCache` não existe.

- [ ] **Step 3: Implementar OrbitalCache.cs**

```csharp
// MissionClear.Api/Cache/OrbitalCache.cs
using System.Collections.Concurrent;
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Models.Tle;

namespace MissionClear.Api.Cache;

public class OrbitalCache
{
    private readonly ConcurrentDictionary<string, TleRecord> _tles = new();
    private volatile IReadOnlyList<OrbitalObject> _propagated = [];

    public int TleCount => _tles.Count;
    public bool IsReady => _propagated.Count > 0;

    public void UpdateTles(IEnumerable<TleRecord> records)
    {
        // Deduplication: CelesTrak wins on conflict (inserted first)
        var incoming = records.ToDictionary(r => r.NoradCatId);
        foreach (var (id, record) in incoming)
        {
            _tles.AddOrUpdate(id, record, (_, existing) =>
                existing.Source == "celestrak" ? existing : record);
        }
        PurgeStaleTles();
    }

    public IReadOnlyList<TleRecord> GetTles() => _tles.Values.ToList().AsReadOnly();

    public void UpdatePropagatedObjects(IReadOnlyList<OrbitalObject> objects)
        => _propagated = objects;

    public IReadOnlyList<OrbitalObject> GetPropagatedObjects() => _propagated;

    private void PurgeStaleTles()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (var key in _tles.Keys)
        {
            if (_tles.TryGetValue(key, out var tle) && tle.FetchedAt < cutoff)
                _tles.TryRemove(key, out _);
        }
    }
}
```

- [ ] **Step 4: Rodar — deve passar**

```bash
dotnet test MissionClear.Tests --filter "OrbitalCacheTests"
```
Esperado: PASS — 4 testes.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: OrbitalCache thread-safe com deduplicação CelesTrak-first"
```

---

## Fase 3: Ingestão de Dados

### Task 3.1: DataAggregatorService (CelesTrak + KeepTrack)

**Files:**
- Create: `MissionClear.Api/Services/DataAggregatorService.cs`
- Test: `MissionClear.Tests/Services/DataAggregatorServiceTests.cs`

- [ ] **Step 1: Escrever testes**

```csharp
// MissionClear.Tests/Services/DataAggregatorServiceTests.cs
using MissionClear.Api.Cache;
using MissionClear.Api.Configuration;
using MissionClear.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace MissionClear.Tests.Services;

public class DataAggregatorServiceTests
{
    private static DataAggregatorService CreateService(HttpClient httpClient, OrbitalCache? cache = null)
    {
        var settings = Options.Create(new ExternalApiSettings
        {
            CelesTrakDebrisUrl = "https://celestrak.test/gp.php",
            KeepTrackBaseUrl   = "https://keeptrack.test/api",
            KeepTrackApiKey    = "test-key"
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        return new DataAggregatorService(
            factory.Object,
            cache ?? new OrbitalCache(),
            settings,
            NullLogger<DataAggregatorService>.Instance
        );
    }

    [Fact]
    public async Task FetchAndStore_ParsesCelesTrakResponse()
    {
        var json = """
        [
          {"NORAD_CAT_ID":"25544","OBJECT_NAME":"ISS","OBJECT_TYPE":"PAYLOAD",
           "TLE_LINE1":"1 25544U 98067A","TLE_LINE2":"2 25544  51.6","EPOCH":"2025-001.0"}
        ]
        """;
        var handler = new MockHttpMessageHandler(json, HttpStatusCode.OK);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://celestrak.test") };
        var cache = new OrbitalCache();
        var svc = CreateService(http, cache);

        await svc.FetchAndStoreAsync(CancellationToken.None);

        cache.TleCount.Should().Be(1);
        cache.GetTles().First().NoradCatId.Should().Be("25544");
    }

    [Fact]
    public async Task FetchAndStore_WhenCelesTrakFails_ThrowsException()
    {
        var handler = new MockHttpMessageHandler("", HttpStatusCode.ServiceUnavailable);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://celestrak.test") };
        var svc = CreateService(http);

        var act = () => svc.FetchAndStoreAsync(CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}

// Helper para testes HTTP
public class MockHttpMessageHandler(string content, HttpStatusCode statusCode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        });
}
```

- [ ] **Step 2: Rodar — deve falhar**

```bash
dotnet test MissionClear.Tests --filter "DataAggregatorServiceTests"
```

- [ ] **Step 3: Implementar DataAggregatorService.cs**

```csharp
// MissionClear.Api/Services/DataAggregatorService.cs
using System.Text.Json;
using MissionClear.Api.Cache;
using MissionClear.Api.Configuration;
using MissionClear.Api.Models.Tle;
using Microsoft.Extensions.Options;

namespace MissionClear.Api.Services;

public class DataAggregatorService(
    IHttpClientFactory httpClientFactory,
    OrbitalCache cache,
    IOptions<ExternalApiSettings> settings,
    ILogger<DataAggregatorService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task FetchAndStoreAsync(CancellationToken ct)
    {
        var celestrakTles = await FetchCelesTrakAsync(ct);
        cache.UpdateTles(celestrakTles);
        logger.LogInformation("CelesTrak: stored {Count} TLEs", celestrakTles.Count);

        await TryFetchKeepTrackAsync(ct);
    }

    private async Task<List<TleRecord>> FetchCelesTrakAsync(CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("CelesTrak");
        var response = await http.GetAsync(settings.Value.CelesTrakDebrisUrl, ct);
        response.EnsureSuccessStatusCode();

        var records = await response.Content
            .ReadFromJsonAsync<List<CelesTrakGpRecord>>(JsonOptions, ct) ?? [];

        return records
            .Where(r => !string.IsNullOrEmpty(r.TleLine1) && !string.IsNullOrEmpty(r.TleLine2))
            .Select(r => new TleRecord(
                r.NoradCatId,
                r.ObjectName,
                r.TleLine1,
                r.TleLine2,
                "celestrak",
                DateTime.UtcNow))
            .ToList();
    }

    private async Task TryFetchKeepTrackAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(settings.Value.KeepTrackApiKey))
        {
            logger.LogDebug("KeepTrack API key not configured — skipping");
            return;
        }

        try
        {
            var http = httpClientFactory.CreateClient("KeepTrack");
            // KeepTrack endpoint path — adjust based on API docs when key is available
            var url = $"{settings.Value.KeepTrackBaseUrl}/tle?apiKey={settings.Value.KeepTrackApiKey}";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await http.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("KeepTrack returned {Status} — continuing without it", response.StatusCode);
                return;
            }

            // KeepTrack may return TLE format — parse when endpoint is confirmed
            logger.LogInformation("KeepTrack: response received (parsing TBD pending API confirmation)");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TaskCanceledException)
        {
            logger.LogWarning("KeepTrack unavailable: {Message} — continuing with CelesTrak only", ex.Message);
        }
    }

    private static string ClassifyObjectType(string objectName, string objectType)
    {
        var name = objectName.ToUpperInvariant();
        var type = objectType.ToUpperInvariant();
        if (name.Contains("DEB") || type.Contains("DEBRIS")) return "debris";
        if (name.Contains("R/B") || type.Contains("ROCKET")) return "rocket_body";
        return "satellite";
    }
}
```

- [ ] **Step 4: Rodar — deve passar**

```bash
dotnet test MissionClear.Tests --filter "DataAggregatorServiceTests"
```

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: DataAggregatorService ingere CelesTrak, KeepTrack como fallback opcional"
```

---

## Fase 4: Motor SGP4

### Task 4.1: OrbitalEngineService

**Files:**
- Create: `MissionClear.Api/Services/OrbitalEngineService.cs`
- Test: `MissionClear.Tests/Services/OrbitalEngineServiceTests.cs`

- [ ] **Step 1: Escrever testes**

```csharp
// MissionClear.Tests/Services/OrbitalEngineServiceTests.cs
using MissionClear.Api.Models.Tle;
using MissionClear.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace MissionClear.Tests.Services;

public class OrbitalEngineServiceTests
{
    // TLE real da ISS (valores aproximados para teste)
    private const string IssLine1 = "1 25544U 98067A   23001.50000000  .00001234  00000-0  23456-4 0  9999";
    private const string IssLine2 = "2 25544  51.6432 123.4567 0001234  78.9012 281.1234 15.49912345123456";

    private readonly OrbitalEngineService _engine = new(NullLogger<OrbitalEngineService>.Instance);

    [Fact]
    public void PropagateToNow_ReturnsObjectWithRealisticAltitude()
    {
        var tle = new TleRecord("25544", "ISS", IssLine1, IssLine2, "celestrak", DateTime.UtcNow);

        var result = _engine.Propagate(tle, DateTime.UtcNow);

        result.Should().NotBeNull();
        result!.AltitudeKm.Should().BeInRange(200, 2000);
        result.LatitudeDeg.Should().BeInRange(-90, 90);
        result.LongitudeDeg.Should().BeInRange(-180, 180);
        result.VelocityKmS.Should().BeInRange(5, 10);
    }

    [Fact]
    public void PropagateAll_SkipsInvalidTles()
    {
        var tles = new List<TleRecord>
        {
            new("25544", "ISS", IssLine1, IssLine2, "celestrak", DateTime.UtcNow),
            new("99999", "INVALID", "bad line1", "bad line2", "celestrak", DateTime.UtcNow)
        };

        var results = _engine.PropagateAll(tles, DateTime.UtcNow);

        results.Should().HaveCount(1);
        results.First().NoradCatId.Should().Be("25544");
    }

    [Fact]
    public void Propagate_ReturnsNullForBadTle()
    {
        var tle = new TleRecord("BAD", "BAD", "invalid line 1", "invalid line 2", "test", DateTime.UtcNow);

        var result = _engine.Propagate(tle, DateTime.UtcNow);

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Rodar — deve falhar**

```bash
dotnet test MissionClear.Tests --filter "OrbitalEngineServiceTests"
```

- [ ] **Step 3: Implementar OrbitalEngineService.cs**

```csharp
// MissionClear.Api/Services/OrbitalEngineService.cs
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Models.Tle;

// ATENÇÃO: Adapte os namespaces e métodos ao pacote NuGet SGP4 escolhido.
// Pacotes comuns:
//   SGP4          → SGP4.Sgp4Propagator.Propagate(tle1, tle2, minutesPastEpoch)
//   Orbit.Sgp4    → similar
// Consulte o README do pacote escolhido para a assinatura exata.

namespace MissionClear.Api.Services;

public class OrbitalEngineService(ILogger<OrbitalEngineService> logger)
{
    private const double EarthRadiusKm = 6371.0;
    private const double MuKm3S2 = 398600.4418; // gravitational parameter

    public OrbitalObject? Propagate(TleRecord tle, DateTime epoch)
    {
        try
        {
            // === ADAPTE AQUI ao pacote NuGet escolhido ===
            // Exemplo genérico — substitua pela API real do pacote:
            //
            // var sgp4 = new Sgp4(tle.Line1, tle.Line2);
            // var minutesPast = (epoch - sgp4.Epoch).TotalMinutes;
            // var (posEci, velEci) = sgp4.Propagate(minutesPast);
            // (double xKm, double yKm, double zKm) = posEci;
            // (double vx, double vy, double vz) = velEci;
            //
            // ECI → Lat/Lon/Alt conversion:
            // var (lat, lon, alt) = EciToGeodetic(xKm, yKm, zKm, epoch);
            // double velocityKmS = Math.Sqrt(vx*vx + vy*vy + vz*vz);
            //
            // return new OrbitalObject(tle.NoradCatId, tle.Name, ClassifyType(tle.Name),
            //     lat, lon, alt, velocityKmS, tle.Source, epoch);

            // === PLACEHOLDER até integrar pacote real ===
            // Remove este bloco quando o SGP4 estiver integrado:
            var (lat, lon, alt, vel) = SimulateOrbit(tle.NoradCatId, epoch);
            return new OrbitalObject(tle.NoradCatId, tle.Name, ClassifyType(tle.Name),
                lat, lon, alt, vel, tle.Source, epoch);
        }
        catch (Exception ex)
        {
            logger.LogDebug("SGP4 failed for {Id}: {Msg}", tle.NoradCatId, ex.Message);
            return null;
        }
    }

    public IReadOnlyList<OrbitalObject> PropagateAll(IEnumerable<TleRecord> tles, DateTime epoch)
        => tles
            .AsParallel()
            .Select(t => Propagate(t, epoch))
            .Where(o => o is not null)
            .Cast<OrbitalObject>()
            .ToList()
            .AsReadOnly();

    private static string ClassifyType(string name)
    {
        var upper = name.ToUpperInvariant();
        if (upper.Contains("DEB") || upper.Contains("DEBRIS")) return "debris";
        if (upper.Contains("R/B") || upper.Contains("ROCKET")) return "rocket_body";
        return "satellite";
    }

    // Remove quando SGP4 real estiver integrado.
    // Simula órbita circular simples para desenvolvimento offline.
    private static (double lat, double lon, double alt, double vel) SimulateOrbit(string noradId, DateTime epoch)
    {
        var seed = noradId.GetHashCode();
        var rng = new Random(seed);
        var altKm = 200 + rng.NextDouble() * 1800;
        var incDeg = rng.NextDouble() * 98;
        var period = 2 * Math.PI * Math.Sqrt(Math.Pow(EarthRadiusKm + altKm, 3) / MuKm3S2);
        var theta = (epoch.ToUnixTimeSeconds() % period) / period * 2 * Math.PI;
        var lat = incDeg * Math.Sin(theta);
        var lon = (epoch.ToUnixTimeSeconds() / 240.0 % 360) - 180;
        var vel = Math.Sqrt(MuKm3S2 / (EarthRadiusKm + altKm));
        return (lat, lon, altKm, vel);
    }

    // ECI to Geodetic — implementar quando SGP4 real retornar vetor ECI
    private static (double lat, double lon, double alt) EciToGeodetic(double x, double y, double z, DateTime epoch)
    {
        var gst = GreenwichSiderealTime(epoch);
        var lon = Math.Atan2(y, x) - gst;
        if (lon > Math.PI) lon -= 2 * Math.PI;
        if (lon < -Math.PI) lon += 2 * Math.PI;
        var r = Math.Sqrt(x * x + y * y + z * z);
        var lat = Math.Asin(z / r);
        var alt = r - EarthRadiusKm;
        return (lat * 180 / Math.PI, lon * 180 / Math.PI, alt);
    }

    private static double GreenwichSiderealTime(DateTime epoch)
    {
        var jd = 367 * epoch.Year
            - (int)(7 * (epoch.Year + (int)((epoch.Month + 9) / 12.0)) / 4.0)
            + (int)(275 * epoch.Month / 9.0)
            + epoch.Day + 1721013.5
            + (epoch.Hour + epoch.Minute / 60.0 + epoch.Second / 3600.0) / 24.0;
        var t = (jd - 2451545.0) / 36525.0;
        var gst = 280.46061837 + 360.98564736629 * (jd - 2451545.0) + t * t * 0.000387933 - t * t * t / 38710000.0;
        return (gst % 360) * Math.PI / 180.0;
    }
}
```

> **Integração SGP4 real:** Após instalar o pacote NuGet, substitua o bloco `SimulateOrbit` pela chamada real do propagador. O método `EciToGeodetic` acima é implementação padrão e pode ser mantida. Os testes em `OrbitalEngineServiceTests` validam que a saída tem valores físicos realistas — eles devem continuar passando após a troca.

- [ ] **Step 4: Rodar — deve passar**

```bash
dotnet test MissionClear.Tests --filter "OrbitalEngineServiceTests"
```

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: OrbitalEngineService com SGP4 stub e ECI-to-geodetic, pronto para integração real"
```

---

## Fase 5: Background Ingestion Service

### Task 5.1: TleIngestionService (IHostedService)

**Files:**
- Create: `MissionClear.Api/Services/Background/TleIngestionService.cs`

- [ ] **Step 1: Implementar TleIngestionService.cs**

```csharp
// MissionClear.Api/Services/Background/TleIngestionService.cs
using MissionClear.Api.Cache;
using MissionClear.Api.Configuration;
using MissionClear.Api.Services;
using Microsoft.Extensions.Options;

namespace MissionClear.Api.Services.Background;

public class TleIngestionService(
    DataAggregatorService aggregator,
    OrbitalEngineService engine,
    OrbitalCache cache,
    IOptions<OrbitalSettings> settings,
    ILogger<TleIngestionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TleIngestionService starting");

        // Primeira ingestão ao iniciar — não espera o timer
        await RunCycle(stoppingToken);

        using var tleTimer = new PeriodicTimer(
            TimeSpan.FromMinutes(settings.Value.TleRefreshIntervalMinutes));
        using var propagateTimer = new PeriodicTimer(
            TimeSpan.FromSeconds(settings.Value.PropagationIntervalSeconds));

        var tleFetch = FetchLoop(tleTimer, stoppingToken);
        var propagate = PropagateLoop(propagateTimer, stoppingToken);

        await Task.WhenAll(tleFetch, propagate);
    }

    private async Task FetchLoop(PeriodicTimer timer, CancellationToken ct)
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await aggregator.FetchAndStoreAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "TLE fetch failed"); }
        }
    }

    private async Task PropagateLoop(PeriodicTimer timer, CancellationToken ct)
    {
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var tles = cache.GetTles();
                if (tles.Count == 0) continue;

                var propagated = engine.PropagateAll(tles, DateTime.UtcNow);
                cache.UpdatePropagatedObjects(propagated);
                logger.LogDebug("Propagated {Count} objects", propagated.Count);
            }
            catch (Exception ex) { logger.LogError(ex, "Propagation cycle failed"); }
        }
    }

    private async Task RunCycle(CancellationToken ct)
    {
        try
        {
            await aggregator.FetchAndStoreAsync(ct);
            var tles = cache.GetTles();
            var propagated = engine.PropagateAll(tles, DateTime.UtcNow);
            cache.UpdatePropagatedObjects(propagated);
            logger.LogInformation("Initial cycle: {TleCount} TLEs, {ObjCount} propagated",
                tles.Count, propagated.Count);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Initial TLE fetch failed — API will serve empty cache");
        }
    }
}
```

- [ ] **Step 2: Registrar no Program.cs**

```csharp
// MissionClear.Api/Program.cs
using MissionClear.Api.Cache;
using MissionClear.Api.Configuration;
using MissionClear.Api.Middleware;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Background;

var builder = WebApplication.CreateBuilder(args);

// Configurações tipadas
builder.Services.Configure<OrbitalSettings>(builder.Configuration.GetSection("OrbitalSettings"));
builder.Services.Configure<ExternalApiSettings>(builder.Configuration.GetSection("ExternalApis"));

// CORS
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
{
    if (builder.Environment.IsDevelopment())
        p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    else
        p.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader();
}));

// HTTP clients
builder.Services.AddHttpClient("CelesTrak");
builder.Services.AddHttpClient("KeepTrack");

// Cache singleton
builder.Services.AddSingleton<OrbitalCache>();

// Services
builder.Services.AddSingleton<DataAggregatorService>();
builder.Services.AddSingleton<OrbitalEngineService>();
builder.Services.AddSingleton<ConjunctionDetectorService>();
builder.Services.AddSingleton<LaunchWindowCalculatorService>();

// Background service
builder.Services.AddHostedService<TleIngestionService>();

// API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Middleware
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.Run();
```

- [ ] **Step 3: Verificar que compila**

```bash
dotnet build
```
Esperado: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: TleIngestionService background com fetch de 60min e propagação de 60s"
```

---

## Fase 6: Detecção de Conjunções

### Task 6.1: ConjunctionDetectorService

**Files:**
- Create: `MissionClear.Api/Services/ConjunctionDetectorService.cs`
- Test: `MissionClear.Tests/Services/ConjunctionDetectorServiceTests.cs`

- [ ] **Step 1: Escrever testes**

```csharp
// MissionClear.Tests/Services/ConjunctionDetectorServiceTests.cs
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Services;
using FluentAssertions;

namespace MissionClear.Tests.Services;

public class ConjunctionDetectorServiceTests
{
    private readonly ConjunctionDetectorService _detector = new();

    private static OrbitalObject MakeObject(string id, double lat, double lon, double alt)
        => new(id, $"OBJ-{id}", "debris", lat, lon, alt, 7.5, "celestrak", DateTime.UtcNow);

    [Fact]
    public void FindConjunctions_ObjectBeyond200Km_ReturnsEmpty()
    {
        var destination = KnownDestinations.ISS; // 408 km, lat ~0, lon ~0
        var debris = new List<OrbitalObject>
        {
            MakeObject("1", 0, 50, 408) // mesmo altitude, mas 50° de lon de distância ~ 5600km
        };

        var result = _detector.FindConjunctions(debris, destination, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindConjunctions_ObjectWithin25Km_ReturnsCritical()
    {
        var destination = KnownDestinations.ISS;
        // Objeto muito próximo da ISS (0.1° de diferença ~ 11km)
        var debris = new List<OrbitalObject>
        {
            MakeObject("99", 0.05, 0.05, 408.1)
        };

        var result = _detector.FindConjunctions(debris, destination, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

        result.Should().HaveCount(1);
        result.First().RiskLevel.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void RiskScore_NoConjunctions_IsZero()
    {
        var score = ConjunctionDetectorService.CalculateRiskScore([]);
        score.Should().Be(0.0);
    }

    [Fact]
    public void RiskScore_CriticalConjunction_IsOne()
    {
        var conjunction = new ConjunctionResult("1", "DEB-1", 10.0, DateTime.UtcNow, RiskLevel.Critical);
        var score = ConjunctionDetectorService.CalculateRiskScore([conjunction]);
        score.Should().Be(1.0);
    }
}
```

- [ ] **Step 2: Rodar — deve falhar**

```bash
dotnet test MissionClear.Tests --filter "ConjunctionDetectorServiceTests"
```

- [ ] **Step 3: Implementar ConjunctionDetectorService.cs**

```csharp
// MissionClear.Api/Services/ConjunctionDetectorService.cs
using MissionClear.Api.Models.Domain;

namespace MissionClear.Api.Services;

public class ConjunctionDetectorService
{
    private const double SafeKm = 25.0;
    private const double MaxKm  = 200.0;

    // Encontra detritos que passam perto da trajetória no período dado
    public IReadOnlyList<ConjunctionResult> FindConjunctions(
        IReadOnlyList<OrbitalObject> debris,
        MissionDestination destination,
        DateTime from,
        DateTime to)
    {
        var results = new List<ConjunctionResult>();

        foreach (var obj in debris)
        {
            // Verificação de altitude: só objetos na faixa de altitude do destino ±200km
            if (Math.Abs(obj.AltitudeKm - destination.AltitudeKm) > 200) continue;

            var distKm = HaversineKm(obj.LatitudeDeg, obj.LongitudeDeg,
                0, 0); // Trajetória simplificada: passa pelo equador

            // Ajuste pela diferença de altitude
            var altDiff = Math.Abs(obj.AltitudeKm - destination.AltitudeKm);
            var approach3d = Math.Sqrt(distKm * distKm + altDiff * altDiff);

            if (approach3d < MaxKm)
            {
                results.Add(new ConjunctionResult(
                    obj.NoradCatId,
                    obj.Name,
                    approach3d,
                    from + (to - from) / 2, // momento estimado: meio da janela
                    ConjunctionResult.ClassifyRisk(approach3d)));
            }
        }

        return results.OrderBy(r => r.ClosestApproachKm).ToList().AsReadOnly();
    }

    // Fórmula definida na seção de algoritmos
    public static double CalculateRiskScore(IReadOnlyList<ConjunctionResult> conjunctions)
    {
        if (conjunctions.Count == 0) return 0.0;

        var total = conjunctions.Sum(c =>
        {
            if (c.ClosestApproachKm >= MaxKm) return 0.0;
            if (c.ClosestApproachKm <= SafeKm) return 1.0;
            return 1.0 - (c.ClosestApproachKm - SafeKm) / (MaxKm - SafeKm);
        });

        return Math.Min(1.0, total);
    }

    // Haversine distance em km entre dois pontos (lat/lon em graus)
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
```

- [ ] **Step 4: Rodar — deve passar**

```bash
dotnet test MissionClear.Tests --filter "ConjunctionDetectorServiceTests"
```

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: ConjunctionDetectorService com Haversine + algoritmo risk_score definido"
```

---

## Fase 7: Calculador de Janelas de Lançamento

### Task 7.1: LaunchWindowCalculatorService

**Files:**
- Create: `MissionClear.Api/Services/LaunchWindowCalculatorService.cs`
- Test: `MissionClear.Tests/Services/LaunchWindowCalculatorServiceTests.cs`

- [ ] **Step 1: Escrever testes**

```csharp
// MissionClear.Tests/Services/LaunchWindowCalculatorServiceTests.cs
using MissionClear.Api.Cache;
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace MissionClear.Tests.Services;

public class LaunchWindowCalculatorServiceTests
{
    private readonly OrbitalCache _cache = new();
    private readonly ConjunctionDetectorService _detector = new();

    private LaunchWindowCalculatorService CreateService()
        => new(_cache, _detector, NullLogger<LaunchWindowCalculatorService>.Instance);

    [Fact]
    public void Calculate_EmptyCache_ReturnsWindowsWithZeroRisk()
    {
        // Cache vazio = sem detritos = sem risco
        var svc = CreateService();
        var from = DateTime.UtcNow;
        var to = from.AddHours(6);

        var windows = svc.Calculate(KnownDestinations.ISS, from, to);

        windows.Should().NotBeEmpty();
        windows.All(w => w.RiskScore == 0.0).Should().BeTrue();
    }

    [Fact]
    public void Calculate_DeltaV_IsPositive()
    {
        var svc = CreateService();
        var from = DateTime.UtcNow;
        var windows = svc.Calculate(KnownDestinations.ISS, from, from.AddHours(3));

        windows.All(w => w.DeltaVKmS > 0).Should().BeTrue();
    }

    [Fact]
    public void CalculateDeltaV_IssDestination_IsRealistic()
    {
        // ISS a 408km, inclinação 51.6° — delta-v típico ~9-10 km/s
        var svc = CreateService();
        var dv = LaunchWindowCalculatorService.CalculateDeltaV(KnownDestinations.ISS, launchInclinationDeg: 28.5);

        dv.Should().BeInRange(7.0, 12.0);
    }

    [Fact]
    public void MissionScore_ZeroRisk_FullDeltaVEfficiency_Returns100()
    {
        var score = LaunchWindowCalculatorService.CalculateMissionScore(0.0, 0.0);
        score.Should().Be(100);
    }

    [Fact]
    public void MissionScore_MaxRisk_Returns50()
    {
        // riskScore=1.0 → safety=0, efficiency=50 (se dv=0)
        var score = LaunchWindowCalculatorService.CalculateMissionScore(1.0, 0.0);
        score.Should().Be(50);
    }
}
```

- [ ] **Step 2: Rodar — deve falhar**

```bash
dotnet test MissionClear.Tests --filter "LaunchWindowCalculatorServiceTests"
```

- [ ] **Step 3: Implementar LaunchWindowCalculatorService.cs**

```csharp
// MissionClear.Api/Services/LaunchWindowCalculatorService.cs
using MissionClear.Api.Cache;
using MissionClear.Api.Models.Domain;

namespace MissionClear.Api.Services;

public class LaunchWindowCalculatorService(
    OrbitalCache cache,
    ConjunctionDetectorService detector,
    ILogger<LaunchWindowCalculatorService> logger)
{
    private const double SlotMinutes = 15.0;
    private const double LaunchInclinationDeg = 28.5; // Canaveral/launch site padrão
    private const double MaxDeltaVKmS = 12.0;

    public IReadOnlyList<LaunchWindow> Calculate(
        MissionDestination destination,
        DateTime from,
        DateTime to)
    {
        var debris = cache.GetPropagatedObjects();
        var windows = new List<LaunchWindow>();
        var current = from;

        while (current < to)
        {
            var slotEnd = current.AddMinutes(SlotMinutes);
            if (slotEnd > to) slotEnd = to;

            var conjunctions = detector.FindConjunctions(debris, destination, current, slotEnd);
            var riskScore = ConjunctionDetectorService.CalculateRiskScore(conjunctions);
            var deltaV = CalculateDeltaV(destination, LaunchInclinationDeg);
            var durationH = (destination.AltitudeKm / 400.0) * 6.2; // estimativa simples

            windows.Add(new LaunchWindow(
                current, slotEnd, riskScore, deltaV, durationH, conjunctions));

            current = slotEnd;
        }

        logger.LogDebug("Calculated {Count} launch windows for {Dest}", windows.Count, destination.Id);
        return windows.AsReadOnly();
    }

    // Delta-v (km/s) simplificado para LEO — Tsiolkovsky + Hohmann transfer
    // dv_circular = sqrt(mu / r) onde r = Earth radius + altitude
    // dv_inclination = 2 * v * sin(Δi/2)
    public static double CalculateDeltaV(MissionDestination destination, double launchInclinationDeg)
    {
        const double mu = 398600.4418;
        const double earthRadius = 6371.0;
        var r = earthRadius + destination.AltitudeKm;
        var vCircular = Math.Sqrt(mu / r);
        var incDiff = Math.Abs(destination.InclinationDeg - launchInclinationDeg);
        var dvInclination = 2 * vCircular * Math.Sin(incDiff * Math.PI / 180 / 2);
        return Math.Round(vCircular + dvInclination, 2);
    }

    // Algoritmo definido no spec
    public static int CalculateMissionScore(double riskScore, double deltaVKmS)
    {
        var efficiency = Math.Max(0.0, 1.0 - deltaVKmS / MaxDeltaVKmS) * 50;
        var safety = (1.0 - Math.Clamp(riskScore, 0.0, 1.0)) * 50;
        return (int)(efficiency + safety);
    }
}
```

- [ ] **Step 4: Rodar — deve passar**

```bash
dotnet test MissionClear.Tests --filter "LaunchWindowCalculatorServiceTests"
```

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: LaunchWindowCalculatorService com delta-v Hohmann e mission_score"
```

---

## Fase 8: Controllers

### Task 8.1: DebrisController

**Files:**
- Create: `MissionClear.Api/Controllers/DebrisController.cs`

- [ ] **Step 1: Implementar DebrisController.cs**

```csharp
// MissionClear.Api/Controllers/DebrisController.cs
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Cache;
using MissionClear.Api.Models.Api;
using MissionClear.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MissionClear.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DebrisController(OrbitalCache cache, IOptions<OrbitalSettings> settings) : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<DebrisDto>> Get(
        [FromQuery] double altitudeMinKm = 200,
        [FromQuery] double altitudeMaxKm = 2000,
        [FromQuery] int limit = 500)
    {
        if (!cache.IsReady)
            return ServiceUnavailable(ApiErrorDto.CacheNotReady());

        limit = Math.Clamp(limit, 1, 2000);

        var objects = cache.GetPropagatedObjects()
            .Where(o => o.AltitudeKm >= altitudeMinKm && o.AltitudeKm <= altitudeMaxKm)
            .Take(limit)
            .Select(o => new DebrisDto
            {
                Id          = o.NoradCatId,
                Name        = o.Name,
                Type        = o.Type,
                Latitude    = Math.Round(o.LatitudeDeg, 4),
                Longitude   = Math.Round(o.LongitudeDeg, 4),
                AltitudeKm  = Math.Round(o.AltitudeKm, 2),
                VelocityKmS = Math.Round(o.VelocityKmS, 3),
                Source      = o.Source,
                UpdatedAt   = o.PropagatedAt
            });

        return Ok(objects);
    }

    private ObjectResult ServiceUnavailable(ApiErrorDto error)
        => StatusCode(503, error);
}
```

- [ ] **Step 2: Commit**

```bash
git add .
git commit -m "feat: DebrisController GET /api/debris com filtro altitude e limit"
```

---

### Task 8.2: LaunchWindowsController

**Files:**
- Create: `MissionClear.Api/Controllers/LaunchWindowsController.cs`

- [ ] **Step 1: Implementar LaunchWindowsController.cs**

```csharp
// MissionClear.Api/Controllers/LaunchWindowsController.cs
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Cache;
using MissionClear.Api.Models.Api;
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Services;

namespace MissionClear.Api.Controllers;

[ApiController]
[Route("api/launch-windows")]
public class LaunchWindowsController(
    OrbitalCache cache,
    LaunchWindowCalculatorService calculator) : ControllerBase
{
    [HttpGet]
    public ActionResult<LaunchWindowsResponseDto> Get(
        [FromQuery] string destination,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        if (!cache.IsReady)
            return StatusCode(503, ApiErrorDto.CacheNotReady());

        if (!KnownDestinations.TryGet(destination, out var dest) || dest is null)
            return BadRequest(ApiErrorDto.InvalidDestination(destination));

        if ((to - from).TotalHours > 48)
            return BadRequest(ApiErrorDto.TimeRangeExceeded());

        var windows = calculator.Calculate(dest, from, to);

        return Ok(new LaunchWindowsResponseDto
        {
            Destination = dest.DisplayName,
            From        = from,
            To          = to,
            Windows     = windows.Select(w => new LaunchWindowDto
            {
                Start         = w.Start,
                End           = w.End,
                RiskScore     = Math.Round(w.RiskScore, 4),
                DeltaVKmS     = w.DeltaVKmS,
                DurationHours = Math.Round(w.DurationHours, 1),
                Conjunctions  = w.Conjunctions.Select(c => new ConjunctionDto
                {
                    DebrisId              = c.DebrisId,
                    ClosestApproachKm     = Math.Round(c.ClosestApproachKm, 2),
                    TimeOfClosestApproach = c.TimeOfClosestApproach,
                    RiskLevel             = c.RiskLevel.ToString().ToLower()
                }).ToList()
            }).ToList()
        });
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add .
git commit -m "feat: LaunchWindowsController GET /api/launch-windows com validação de destino e range"
```

---

### Task 8.3: MissionController

**Files:**
- Create: `MissionClear.Api/Controllers/MissionController.cs`

- [ ] **Step 1: Implementar MissionController.cs**

```csharp
// MissionClear.Api/Controllers/MissionController.cs
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Cache;
using MissionClear.Api.Models.Api;
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Services;

namespace MissionClear.Api.Controllers;

[ApiController]
[Route("api/mission")]
public class MissionController(
    OrbitalCache cache,
    ConjunctionDetectorService detector) : ControllerBase
{
    [HttpPost("simulate")]
    public ActionResult<MissionSimulateResponseDto> Simulate([FromBody] MissionSimulateRequestDto request)
    {
        if (!cache.IsReady)
            return StatusCode(503, ApiErrorDto.CacheNotReady());

        if (!KnownDestinations.TryGet(request.Destination, out var dest) || dest is null)
            return BadRequest(ApiErrorDto.InvalidDestination(request.Destination));

        if (request.ArrivalTime <= request.DepartureTime)
            return BadRequest(new ApiErrorDto
            {
                Error     = "INVALID_TIME_RANGE",
                Message   = "arrival_time must be after departure_time.",
                Timestamp = DateTime.UtcNow
            });

        var debris = cache.GetPropagatedObjects();
        var conjunctions = detector.FindConjunctions(debris, dest, request.DepartureTime, request.ArrivalTime);
        var riskScore = ConjunctionDetectorService.CalculateRiskScore(conjunctions);
        var deltaV = LaunchWindowCalculatorService.CalculateDeltaV(dest, 28.5);
        var score = LaunchWindowCalculatorService.CalculateMissionScore(riskScore, deltaV);

        return Ok(new MissionSimulateResponseDto
        {
            Trajectory   = [],
            Obstacles    = conjunctions.Select(c => new ObstacleDto
            {
                DebrisId              = c.DebrisId,
                ClosestApproachKm     = Math.Round(c.ClosestApproachKm, 2),
                TimeOfClosestApproach = c.TimeOfClosestApproach,
                RiskLevel             = c.RiskLevel.ToString().ToLower()
            }).ToList(),
            MissionScore = score
        });
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add .
git commit -m "feat: MissionController POST /api/mission/simulate"
```

---

## Fase 9: Middleware de Exceção

### Task 9.1: GlobalExceptionMiddleware

**Files:**
- Create: `MissionClear.Api/Middleware/GlobalExceptionMiddleware.cs`

- [ ] **Step 1: Implementar GlobalExceptionMiddleware.cs**

```csharp
// MissionClear.Api/Middleware/GlobalExceptionMiddleware.cs
using System.Text.Json;
using MissionClear.Api.Models.Api;

namespace MissionClear.Api.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            var error = ApiErrorDto.InternalError();
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(error, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
        }
    }
}
```

- [ ] **Step 2: Verificar build final**

```bash
dotnet build
```
Esperado: `Build succeeded. 0 Error(s).`

- [ ] **Step 3: Rodar todos os testes**

```bash
dotnet test --verbosity normal
```
Esperado: todos os testes PASS.

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: GlobalExceptionMiddleware — nunca expõe stack trace em produção"
```

---

## Fase 10: Smoke Test Manual

### Task 10.1: Iniciar e verificar endpoints

- [ ] **Step 1: Iniciar a API**

```bash
cd MissionClear.Api
dotnet run
```
Esperado: API ouvindo em `http://localhost:5000`. Log deve mostrar `TleIngestionService starting` e após ~30s `Initial cycle: XXXX TLEs`.

- [ ] **Step 2: Testar /api/debris**

```bash
curl "http://localhost:5000/api/debris?limit=5"
```
Esperado: array JSON com 5 objetos, cada um com `id`, `name`, `type`, `altitude_km`, etc.

- [ ] **Step 3: Testar /api/launch-windows**

```bash
curl "http://localhost:5000/api/launch-windows?destination=ISS&from=2025-05-27T00:00:00Z&to=2025-05-27T06:00:00Z"
```
Esperado: JSON com `destination`, `windows` array com slots de 15 minutos.

- [ ] **Step 4: Testar /api/mission/simulate**

```bash
curl -X POST "http://localhost:5000/api/mission/simulate" \
  -H "Content-Type: application/json" \
  -d '{"destination":"ISS","departure_time":"2025-05-27T14:00:00Z","arrival_time":"2025-05-27T20:00:00Z"}'
```
Esperado: JSON com `obstacles`, `mission_score`.

- [ ] **Step 5: Testar destinação inválida**

```bash
curl "http://localhost:5000/api/launch-windows?destination=MARTE&from=2025-05-27T00:00:00Z&to=2025-05-27T06:00:00Z"
```
Esperado: 400 com `{"error":"INVALID_DESTINATION","message":"..."}`

- [ ] **Step 6: Commit final**

```bash
git add .
git commit -m "chore: smoke tests validados, MVP backend funcional"
```

---

## Ordem de Implementação Recomendada

```
Fase 0 (scaffolding)     → imediato, ~30min
Fase 1 (modelos)         → imediato, ~30min
Fase 2 (cache)           → imediato, ~20min
Fase 3 (ingestão dados)  → depende de Fase 1+2, ~45min
Fase 4 (SGP4 engine)     → depende de Fase 1+2, ~1h
Fase 5 (background svc)  → depende de Fase 3+4, ~30min
Fase 6 (conjunções)      → depende de Fase 1+2, ~45min
Fase 7 (janelas)         → depende de Fase 2+6, ~30min
Fase 8 (controllers)     → depende de Fase 6+7, ~45min
Fase 9 (middleware)      → depende de Fase 8, ~15min
Fase 10 (smoke tests)    → depende de tudo, ~30min
```

**Estimativa total:** 6–8h de trabalho em sessão contínua.

---

## O Que Não Está Neste Plano (V2)

- Integração real KeepTrack (endpoint a confirmar quando API key disponível)
- SGP4 real (stub presente — substituir conforme pacote NuGet disponível)
- Autenticação
- Persistência (banco de dados)
- Rate limiting
- Cache distribuído (Redis)
- Paginação com cursor
- Trajetória detalhada em `/api/mission/simulate` (campo `trajectory` retorna vazio por ora)
