# Implementation Plan 00: Project Scaffolding

## Overview

Creates the solution structure from scratch: two projects (`MissionClear.Api` and `MissionClear.Tests`), installs all NuGet packages, sets up configuration POCOs with startup validation, folder skeleton with `.gitkeep` placeholders, `.gitignore`, and 4 configuration tests.

**Execution order:** First. Everything else depends on this.

## Requirements

- .NET 8 SDK installed
- `dotnet` CLI available in PATH
- No SGP4 NuGet package — `OrbitalEngineService` uses a deterministic stub (plan-03)
- JWT Secret must be at least 32 characters — startup throws if shorter

---

## Phase 1: Solution & Projects (commit 1)

### Step 1.1: Create solution and projects

```bash
# Run from the repo root: C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C

dotnet new sln -n MissionClear
dotnet new webapi -n MissionClear.Api --no-openapi -o MissionClear.Api
dotnet new xunit -n MissionClear.Tests -o MissionClear.Tests

dotnet sln add MissionClear.Api/MissionClear.Api.csproj
dotnet sln add MissionClear.Tests/MissionClear.Tests.csproj

# Test project references the API project
dotnet add MissionClear.Tests/MissionClear.Tests.csproj reference MissionClear.Api/MissionClear.Api.csproj
```

### Step 1.2: Install NuGet packages

```bash
# MissionClear.Api
dotnet add MissionClear.Api package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.10
dotnet add MissionClear.Api package Microsoft.EntityFrameworkCore.Design --version 8.0.10
dotnet add MissionClear.Api package Microsoft.EntityFrameworkCore.Tools --version 8.0.10
dotnet add MissionClear.Api package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.10
dotnet add MissionClear.Api package BCrypt.Net-Next --version 4.0.3

# MissionClear.Tests
dotnet add MissionClear.Tests package FluentAssertions --version 6.12.1
dotnet add MissionClear.Tests package Microsoft.EntityFrameworkCore.InMemory --version 8.0.10
dotnet add MissionClear.Tests package coverlet.collector --version 6.0.2
```

**No SGP4 package.** The orbital engine uses a deterministic FNV-hash stub (plan-03). Using a NuGet SGP4 library would require implementation-specific knowledge that doesn't exist as a canonical .NET package. The stub is sufficient for MVP.

### Step 1.3: Create .gitignore

```gitignore
obj/
bin/
*.user
.vs/
.idea/
*.suo
*.lock.json
appsettings.Production.json
*.db
*.db-journal
```

- [ ] Commit: `chore: initialize solution with Api + Tests projects`

---

## Phase 2: Folder Structure (commit 2)

### Separação de responsabilidades — regra geral

```
Cada camada tem UMA responsabilidade. Nunca misturar:

┌─────────────────────────────────────────────────────────────┐
│  HTTP / Presentation layer                                  │
│  Controllers/  →  só routing + deserializar + chamar serviço│
├─────────────────────────────────────────────────────────────┤
│  Application / Business layer                               │
│  Services/     →  regras de negócio, orquestração           │
│  Interfaces/   →  contratos (IFooService, IFooRepository)   │
├─────────────────────────────────────────────────────────────┤
│  Calculation layer (funções puras)                          │
│  Helpers/      →  static classes sem estado e sem DI        │
│                   OrbitalMath, RiskScoring, MissionScoring  │
├─────────────────────────────────────────────────────────────┤
│  Data Access layer                                          │
│  Repositories/ →  queries EF Core via interface             │
│  Data/         →  AppDbContext, Migrations                  │
├─────────────────────────────────────────────────────────────┤
│  Domain layer                                               │
│  Models/       →  domain records e value objects            │
│  Entities/     →  EF Core entities (mapeadas para tabelas)  │
│  Dtos/         →  request/response bodies da API            │
├─────────────────────────────────────────────────────────────┤
│  Cross-cutting                                              │
│  Exceptions/   →  DomainException e códigos de erro         │
│  Configuration/→  POCOs de config (JwtSettings etc.)        │
│  Middleware/   →  GlobalExceptionMiddleware                  │
│  Cache/        →  OrbitalCache (in-memory thread-safe)       │
└─────────────────────────────────────────────────────────────┘
```

### Onde cada arquivo vai — referência rápida

| Arquivo | Pasta | Motivo |
|---------|-------|--------|
| `AuthController.cs` | `Controllers/` | HTTP adapter puro |
| `IAuthService.cs` | `Interfaces/` | Contrato da camada de serviço |
| `AuthService.cs` | `Services/` | Regra de negócio (BCrypt, JWT) |
| `IUserRepository.cs` | `Interfaces/` | Contrato de acesso a dados |
| `UserRepository.cs` | `Repositories/` | Queries EF Core sobre `UserEntity` |
| `UserEntity.cs` | `Entities/` | Classe mapeada para tabela `users` |
| `UserResponse.cs` | `Dtos/` | Body de resposta da API |
| `OrbitalObject.cs` | `Models/` | Domain record (nunca vai pro banco direto) |
| `AppDbContext.cs` | `Data/` | EF Core DbContext |
| `DomainException.cs` | `Exceptions/` | Exceção de domínio com código de erro |
| `JwtSettings.cs` | `Configuration/` | POCO de config |
| `GlobalExceptionMiddleware.cs` | `Middleware/` | Middleware ASP.NET |
| `OrbitalCache.cs` | `Cache/` | Estado compartilhado thread-safe |
| `OrbitalMath.cs` | `Helpers/` | Haversine, ECI→Geodetic, GMST — funções puras |
| `RiskScoring.cs` | `Helpers/` | Classify(km), ComputeScore() — funções puras |
| `MissionScoring.cs` | `Helpers/` | Compute(deltaV, risk) → int — funções puras |

### Convenção de nomenclatura

```
Interface    → IFooService, IFooRepository   (prefixo I)
Serviço      → FooService                   (sufixo Service)
Repositório  → FooRepository                (sufixo Repository)
Controller   → FooController                (sufixo Controller)
Entidade EF  → FooEntity                    (sufixo Entity)
Helper       → FooMath, FooScoring          (sem sufixo fixo — nome descreve o domínio)
DTO request  → FooRequest, CreateFooRequest
DTO response → FooResponse, FooDto
```

### Quando usar cada camada — decisão rápida

```
Preciso fazer uma conta/fórmula sem banco e sem HTTP?  → Helpers/
Preciso orquestrar lógica de negócio?                 → Services/
Preciso ler/escrever no banco?                        → Repositories/
Preciso receber/retornar HTTP?                        → Controllers/
Preciso definir um contrato para DI?                  → Interfaces/
Preciso mapear uma tabela do banco?                   → Entities/
Preciso representar um conceito do domínio?           → Models/
Preciso definir o body de request ou response?        → Dtos/
```

### Step 2.1: Estrutura completa de pastas

```
MissionClear.Api/
│
├── Controllers/
│   ├── BaseApiController.cs          ← helpers: GetUserId(), DomainError()
│   ├── AuthController.cs             ← POST /api/auth/*
│   ├── UsersController.cs            ← GET|PUT /api/users/me
│   ├── StatusController.cs           ← GET /api/status
│   ├── DebrisController.cs           ← GET /api/debris*
│   ├── DestinationsController.cs     ← GET /api/destinations
│   ├── LaunchWindowsController.cs    ← GET /api/launch-windows*
│   ├── MissionController.cs          ← POST /api/mission/* + SSE
│   ├── MissionsController.cs         ← GET|DELETE /api/missions/* (histórico)
│   └── DashboardController.cs        ← GET /api/dashboard/*
│
├── Interfaces/                        ← TODOS os contratos aqui
│   ├── IAuthService.cs
│   ├── IUserService.cs
│   ├── IUserRepository.cs
│   ├── IRefreshTokenRepository.cs
│   ├── IMissionRepository.cs
│   ├── IMissionHistoryService.cs
│   ├── IDashboardService.cs
│   ├── IDataAggregatorService.cs
│   ├── IOrbitalEngineService.cs
│   ├── IConjunctionDetector.cs
│   └── ILaunchWindowCalculator.cs
│
├── Services/
│   ├── AuthService.cs
│   ├── UserService.cs
│   ├── MissionHistoryService.cs
│   ├── DashboardService.cs
│   ├── DataAggregatorService.cs
│   ├── OrbitalEngineService.cs
│   ├── ConjunctionDetector.cs
│   ├── LaunchWindowCalculator.cs
│   ├── MissionSimulationService.cs
│   ├── SessionStore.cs               ← singleton, in-memory
│   ├── MissionSseService.cs
│   └── Background/
│       └── TleIngestionService.cs    ← BackgroundService
│
├── Helpers/                           ← cálculos puros (static, sem DI)
│   ├── OrbitalMath.cs                 ← Haversine, ECI→Geodetic, GMST, ToRad
│   ├── RiskScoring.cs                 ← Classify(km), ComputeScore(conjunctions)
│   └── MissionScoring.cs             ← Compute(deltaV, risk) → mission_score [0-100]
│
├── Repositories/
│   ├── UserRepository.cs
│   ├── RefreshTokenRepository.cs
│   └── MissionRepository.cs
│
├── Data/
│   ├── AppDbContext.cs
│   └── Migrations/                   ← gerado por dotnet ef
│
├── Entities/                          ← classes mapeadas para tabelas EF Core
│   ├── UserEntity.cs
│   ├── RefreshTokenEntity.cs
│   └── MissionEntity.cs
│
├── Models/                            ← domain records (não vão pro banco diretamente)
│   ├── OrbitalObject.cs
│   ├── TleRecord.cs
│   ├── ConjunctionResult.cs
│   ├── LaunchWindow.cs
│   ├── MissionSession.cs
│   ├── MissionDestination.cs
│   └── RiskLevel.cs
│
├── Dtos/                              ← request/response bodies da API
│   ├── ApiErrorDto.cs
│   ├── DebrisDto.cs
│   ├── DestinationDto.cs
│   ├── ConjunctionDto.cs
│   ├── LaunchWindowDto.cs
│   ├── MissionSimulateRequest.cs
│   ├── MissionSimulateResponse.cs
│   ├── SessionRequest.cs
│   ├── SessionCompleteRequest.cs
│   ├── AuthRegisterRequest.cs
│   ├── AuthLoginRequest.cs
│   ├── AuthRefreshRequest.cs
│   ├── AuthResponse.cs
│   ├── UserResponse.cs
│   ├── UpdateUserRequest.cs
│   ├── MissionHistoryDto.cs
│   ├── MissionDetailDto.cs
│   ├── MissionStatsDto.cs
│   ├── DashboardSummaryResponse.cs
│   └── DashboardAlertsResponse.cs
│
├── Exceptions/
│   └── DomainException.cs
│
├── Cache/
│   └── OrbitalCache.cs
│
├── Configuration/
│   ├── JwtSettings.cs
│   ├── OrbitalSettings.cs
│   ├── ExternalApiSettings.cs
│   ├── CorsSettings.cs
│   └── DashboardConstants.cs
│
├── Middleware/
│   └── GlobalExceptionMiddleware.cs
│
├── Program.cs
├── appsettings.json
└── appsettings.Development.json

MissionClear.Tests/
├── Configuration/
│   └── AppSettingsTests.cs
├── Helpers/
│   ├── OrbitalMathTests.cs
│   ├── RiskScoringTests.cs
│   └── MissionScoringTests.cs
├── Services/
│   ├── ConjunctionDetectorTests.cs
│   ├── LaunchWindowCalculatorTests.cs
│   ├── MissionSimulationServiceTests.cs
│   ├── SessionStoreTests.cs
│   ├── MissionSseServiceTests.cs
│   ├── AuthServiceTests.cs
│   ├── MissionHistoryServiceTests.cs
│   └── DashboardServiceTests.cs
├── Repositories/
│   ├── UserRepositoryTests.cs
│   └── MissionRepositoryTests.cs
└── Controllers/
    ├── TestWebApplicationFactory.cs
    ├── AuthControllerTests.cs
    ├── DebrisControllerTests.cs
    └── MissionsControllerTests.cs
```

### Fluxo de chamada — exemplo: POST /api/auth/register

```
HTTP Request
    ↓
AuthController.Register()        ← valida shape do body, chama serviço
    ↓
IAuthService.RegisterAsync()     ← regra: senha forte? email já existe?
    ↓
IUserRepository.ExistsByEmailAsync()  ← query no banco
IUserRepository.CreateAsync()         ← persiste UserEntity
IRefreshTokenRepository.CreateAsync() ← persiste RefreshTokenEntity
    ↓
JwtService.GenerateAccessToken()  ← gera JWT
    ↓
AuthResponse (DTO)                ← retorna para o controller
    ↓
HTTP 201 Created
```

### Regras de dependência (nunca violar)

```
Controller  →  pode depender de: IFooService
Service     →  pode depender de: IFooRepository, outros IFooService, JwtService, Helpers.*
Repository  →  pode depender de: AppDbContext
Helper      →  ZERO dependências — só System.Math e tipos do domínio
Controller  →  NUNCA acessa AppDbContext diretamente
Controller  →  NUNCA chama Helpers diretamente (vai via Service)
Service     →  NUNCA acessa HttpContext
Repository  →  NUNCA contém regra de negócio
Entity      →  NUNCA exposta direto como resposta HTTP (usar DTO)
```

### Quem usa cada Helper

```
OrbitalMath    ← OrbitalEngineService (EciToGeodetic, Gmst)
               ← ConjunctionDetector  (HaversineKm)

RiskScoring    ← ConjunctionDetector      (Classify, ComputeScore)
               ← LaunchWindowCalculator   (ComputeScore)
               ← MissionSimulationService (ComputeScore)
               ← DashboardService         (threshold ConjunctionThresholdKm)

MissionScoring ← MissionSimulationService (Compute)
               ← MissionHistoryService    (Compute ao salvar no banco)
```

```powershell
# Run from repo root in PowerShell
$dirs = @(
    # Presentation layer
    "MissionClear.Api/Controllers",
    # Application layer
    "MissionClear.Api/Interfaces",
    "MissionClear.Api/Services",
    "MissionClear.Api/Services/Background",
    # Calculation layer (pure functions, no DI)
    "MissionClear.Api/Helpers",
    # Data access layer
    "MissionClear.Api/Repositories",
    "MissionClear.Api/Data",
    "MissionClear.Api/Data/Migrations",
    # Domain layer
    "MissionClear.Api/Entities",
    "MissionClear.Api/Models",
    "MissionClear.Api/Dtos",
    # Cross-cutting
    "MissionClear.Api/Exceptions",
    "MissionClear.Api/Cache",
    "MissionClear.Api/Configuration",
    "MissionClear.Api/Middleware",
    # Tests
    "MissionClear.Tests/Configuration",
    "MissionClear.Tests/Helpers",
    "MissionClear.Tests/Services",
    "MissionClear.Tests/Repositories",
    "MissionClear.Tests/Controllers"
)
foreach ($d in $dirs) {
    New-Item -ItemType Directory -Force $d | Out-Null
    New-Item -ItemType File -Force "$d/.gitkeep" | Out-Null
}
Write-Host "Folders created."
```

- [ ] Commit: `chore: add folder structure with .gitkeep`

---

## Phase 3: Helpers — cálculos puros (commit 3)

Criados aqui pois todos os Services (planos 03-06) dependem deles. Nenhuma dependência externa — só `System.Math` e tipos primitivos.

### OrbitalMath

**File:** `MissionClear.Api/Helpers/OrbitalMath.cs`

```csharp
namespace MissionClear.Api.Helpers;

public static class OrbitalMath
{
    public const double EarthRadiusKm = 6371.0;
    public const double EarthFlattening = 1.0 / 298.257223563; // WGS-84
    public const double EarthEccentricitySq = 2 * EarthFlattening - EarthFlattening * EarthFlattening;

    public static double ToRad(double deg) => deg * Math.PI / 180.0;
    public static double ToDeg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>Haversine great-circle distance on a sphere of given radius.</summary>
    public static double HaversineKm(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg, double radiusKm)
    {
        var dLat = ToRad(lat2Deg - lat1Deg);
        var dLon = ToRad(lon2Deg - lon1Deg);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1Deg)) * Math.Cos(ToRad(lat2Deg))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Greenwich Mean Sidereal Time in radians.</summary>
    public static double Gmst(DateTime utc)
    {
        var jd = 367.0 * utc.Year
            - (int)(7.0 * (utc.Year + (int)((utc.Month + 9.0) / 12.0)) / 4.0)
            + (int)(275.0 * utc.Month / 9.0)
            + utc.Day + 1721013.5
            + utc.TimeOfDay.TotalHours / 24.0;
        var t = (jd - 2451545.0) / 36525.0;
        var gmstDeg = 280.46061837 + 360.98564736629 * (jd - 2451545.0)
                    + 0.000387933 * t * t - t * t * t / 38710000.0;
        return ToRad(gmstDeg % 360.0);
    }

    /// <summary>Convert ECI (km) to geodetic lat/lon/alt. Bowring iterative method.</summary>
    public static (double LatDeg, double LonDeg, double AltKm) EciToGeodetic(
        double xKm, double yKm, double zKm, double gmstRad)
    {
        var lonRad = Math.Atan2(yKm, xKm) - gmstRad;
        var rKm = Math.Sqrt(xKm * xKm + yKm * yKm);
        var latRad = Math.Atan2(zKm, rKm * (1 - EarthEccentricitySq));

        // Bowring iteration (3 passes — sufficient for LEO)
        for (var i = 0; i < 3; i++)
        {
            var sinLat = Math.Sin(latRad);
            var n = EarthRadiusKm / Math.Sqrt(1 - EarthEccentricitySq * sinLat * sinLat);
            latRad = Math.Atan2(zKm + EarthEccentricitySq * n * sinLat, rKm);
        }

        var sinLat2 = Math.Sin(latRad);
        var nFinal = EarthRadiusKm / Math.Sqrt(1 - EarthEccentricitySq * sinLat2 * sinLat2);
        var altKm = rKm / Math.Cos(latRad) - nFinal;

        // Normalize longitude to [-180, 180]
        var lonDeg = ToDeg(lonRad) % 360.0;
        if (lonDeg > 180) lonDeg -= 360;
        if (lonDeg < -180) lonDeg += 360;

        return (ToDeg(latRad), lonDeg, altKm);
    }
}
```

### RiskScoring

**File:** `MissionClear.Api/Helpers/RiskScoring.cs`

```csharp
using MissionClear.Api.Models;

namespace MissionClear.Api.Helpers;

public static class RiskScoring
{
    // Thresholds
    public const double CriticalKm  = 1.0;
    public const double HighKm      = 5.0;
    public const double MediumKm    = 10.0;
    public const double SafeKm      = 25.0;
    public const double MaxRadiusKm = 200.0;

    public static RiskLevel Classify(double km) => km switch
    {
        < CriticalKm => RiskLevel.Critical,
        < HighKm     => RiskLevel.High,
        < MediumKm   => RiskLevel.Medium,
        _            => RiskLevel.Low
    };

    /// <summary>
    /// Aggregated risk score [0, 1] from a set of closest-approach distances.
    /// Formula: sum of contributions, clamped to 1.
    /// Contribution per object = max(0, 1 - (d - SAFE_KM) / (MAX_KM - SAFE_KM))
    /// </summary>
    public static double ComputeScore(IEnumerable<double> closestApproachesKm)
    {
        double total = 0.0;
        foreach (var d in closestApproachesKm)
        {
            if (d >= MaxRadiusKm) continue;
            total += Math.Max(0.0, 1.0 - (d - SafeKm) / (MaxRadiusKm - SafeKm));
        }
        return Math.Min(1.0, total);
    }
}
```

### MissionScoring

**File:** `MissionClear.Api/Helpers/MissionScoring.cs`

```csharp
namespace MissionClear.Api.Helpers;

public static class MissionScoring
{
    public const double MaxDeltaVKmS     = 12.0;
    public const double EfficiencyWeight = 50.0;
    public const double SafetyWeight     = 50.0;

    /// <summary>
    /// Mission score [0, 100].
    /// efficiency = max(0, 1 - deltaV/12) * 50
    /// safety     = (1 - riskScore) * 50
    /// total      = clamp(round(efficiency + safety), 0, 100)
    /// </summary>
    public static (double Efficiency, double Safety, int Total) Compute(double deltaVKmS, double riskScore)
    {
        var efficiency = Math.Max(0.0, 1.0 - deltaVKmS / MaxDeltaVKmS) * EfficiencyWeight;
        var safety     = (1.0 - Math.Clamp(riskScore, 0.0, 1.0)) * SafetyWeight;
        var total      = (int)Math.Clamp(Math.Round(efficiency + safety), 0, 100);
        return (efficiency, safety, total);
    }
}
```

### Testes dos Helpers (RED → GREEN)

**File:** `MissionClear.Tests/Helpers/OrbitalMathTests.cs`

```csharp
using FluentAssertions;
using MissionClear.Api.Helpers;
using Xunit;

namespace MissionClear.Tests.Helpers;

public class OrbitalMathTests
{
    [Fact]
    public void Haversine_SamePoint_ReturnsZero()
    {
        OrbitalMath.HaversineKm(0, 0, 0, 0, OrbitalMath.EarthRadiusKm).Should().BeApproximately(0, 0.001);
    }

    [Fact]
    public void Haversine_OneDegreeLatAtEquator_IsAbout111km()
    {
        var km = OrbitalMath.HaversineKm(0, 0, 1, 0, OrbitalMath.EarthRadiusKm);
        km.Should().BeInRange(110, 112);
    }

    [Fact]
    public void EciToGeodetic_EquatorialPoint_LatNearZero()
    {
        // Point on equator, no inclination
        var gmst = OrbitalMath.Gmst(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var r = OrbitalMath.EarthRadiusKm + 400; // 400 km LEO
        var (lat, lon, alt) = OrbitalMath.EciToGeodetic(r, 0, 0, gmst);
        lat.Should().BeApproximately(0, 0.1);
        alt.Should().BeApproximately(400, 1.0);
    }

    [Fact]
    public void ToRad_And_ToDeg_AreInverse()
    {
        OrbitalMath.ToDeg(OrbitalMath.ToRad(45.0)).Should().BeApproximately(45.0, 0.0001);
    }
}
```

**File:** `MissionClear.Tests/Helpers/RiskScoringTests.cs`

```csharp
using FluentAssertions;
using MissionClear.Api.Helpers;
using MissionClear.Api.Models;
using Xunit;

namespace MissionClear.Tests.Helpers;

public class RiskScoringTests
{
    [Theory]
    [InlineData(0.5, RiskLevel.Critical)]
    [InlineData(3.0, RiskLevel.High)]
    [InlineData(7.0, RiskLevel.Medium)]
    [InlineData(50.0, RiskLevel.Low)]
    public void Classify_ReturnsCorrectLevel(double km, RiskLevel expected)
    {
        RiskScoring.Classify(km).Should().Be(expected);
    }

    [Fact]
    public void ComputeScore_NoDebris_ReturnsZero()
    {
        RiskScoring.ComputeScore(Array.Empty<double>()).Should().Be(0.0);
    }

    [Fact]
    public void ComputeScore_FarDebris_ReturnsZero()
    {
        RiskScoring.ComputeScore(new[] { 300.0, 500.0 }).Should().Be(0.0);
    }

    [Fact]
    public void ComputeScore_ManyCloseDebris_ClampsToOne()
    {
        var many = Enumerable.Repeat(0.1, 100).ToList();
        RiskScoring.ComputeScore(many).Should().Be(1.0);
    }

    [Fact]
    public void ComputeScore_AtBoundary_IsNonZero()
    {
        // d = SAFE_KM → contribution = 1.0 - 0 = 1.0 for a single object
        RiskScoring.ComputeScore(new[] { RiskScoring.SafeKm }).Should().BeApproximately(1.0, 0.001);
    }
}
```

**File:** `MissionClear.Tests/Helpers/MissionScoringTests.cs`

```csharp
using FluentAssertions;
using MissionClear.Api.Helpers;
using Xunit;

namespace MissionClear.Tests.Helpers;

public class MissionScoringTests
{
    [Fact]
    public void Compute_ZeroDeltaV_ZeroRisk_Returns100()
    {
        var (_, _, total) = MissionScoring.Compute(0, 0);
        total.Should().Be(100);
    }

    [Fact]
    public void Compute_MaxDeltaV_ZeroRisk_Returns50()
    {
        var (_, _, total) = MissionScoring.Compute(MissionScoring.MaxDeltaVKmS, 0);
        total.Should().Be(50);
    }

    [Fact]
    public void Compute_ZeroDeltaV_MaxRisk_Returns50()
    {
        var (_, _, total) = MissionScoring.Compute(0, 1.0);
        total.Should().Be(50);
    }

    [Fact]
    public void Compute_MaxDeltaV_MaxRisk_Returns0()
    {
        var (_, _, total) = MissionScoring.Compute(MissionScoring.MaxDeltaVKmS, 1.0);
        total.Should().Be(0);
    }

    [Fact]
    public void Compute_IssCruise_TypicalValues()
    {
        // deltaV=9.4 → efficiency=(1-9.4/12)*50=10.83; risk=0.1 → safety=45; total=56
        var (eff, saf, total) = MissionScoring.Compute(9.4, 0.1);
        eff.Should().BeApproximately(10.83, 0.01);
        saf.Should().BeApproximately(45.0, 0.01);
        total.Should().Be(56);
    }
}
```

- [ ] `dotnet test --filter "Helpers"` — todos 13 testes devem passar (GREEN)
- [ ] Commit: `feat(helpers): add OrbitalMath, RiskScoring, MissionScoring pure calculation classes`

---

## Phase 4: Configuration POCOs (commit 4)

Each POCO has a `SectionName` constant so binding is never hardcoded at the call site.

> **Nota:** `MissionScoring` e `DashboardConstants` foram movidos para `Helpers/` (Phase 3). Aqui ficam apenas POCOs de `IConfiguration`.

### JwtSettings

**File:** `MissionClear.Api/Configuration/JwtSettings.cs`

```csharp
namespace MissionClear.Api.Configuration;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 60;
    public int RefreshTokenDays { get; init; } = 7;
}
```

### OrbitalSettings

**File:** `MissionClear.Api/Configuration/OrbitalSettings.cs`

```csharp
namespace MissionClear.Api.Configuration;

public sealed class OrbitalSettings
{
    public const string SectionName = "Orbital";

    public int TleFetchIntervalMinutes { get; init; } = 60;
    public int PropagationIntervalSeconds { get; init; } = 60;
    public int MaxDebrisCount { get; init; } = 30000;
    public int TleMaxAgeDays { get; init; } = 7;
}
```

### ExternalApiSettings

**File:** `MissionClear.Api/Configuration/ExternalApiSettings.cs`

```csharp
namespace MissionClear.Api.Configuration;

public sealed class ExternalApiSettings
{
    public const string SectionName = "ExternalApi";

    public string CelesTrakBaseUrl { get; init; } =
        "https://celestrak.org/NORAD/elements/gp.php?GROUP=debris&FORMAT=json";
    public string KeepTrackBaseUrl { get; init; } = "https://keeptrack.space/api";
    public string KeepTrackApiKey { get; init; } = string.Empty;
    public int KeepTrackTimeoutSeconds { get; init; } = 5;
}
```

### CorsSettings

**File:** `MissionClear.Api/Configuration/CorsSettings.cs`

```csharp
namespace MissionClear.Api.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; init; } = Array.Empty<string>();
}
```

- [ ] Commit: `feat(config): add JwtSettings, OrbitalSettings, ExternalApiSettings, CorsSettings`

---

## Phase 4: appsettings (commit 4)

### appsettings.json

**File:** `MissionClear.Api/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Jwt": {
    "Secret": "",
    "Issuer": "mission-clear-api",
    "Audience": "mission-clear-mobile",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 7
  },
  "Orbital": {
    "TleFetchIntervalMinutes": 60,
    "PropagationIntervalSeconds": 60,
    "MaxDebrisCount": 30000,
    "TleMaxAgeDays": 7
  },
  "ExternalApi": {
    "CelesTrakBaseUrl": "https://celestrak.org/NORAD/elements/gp.php?GROUP=debris&FORMAT=json",
    "KeepTrackBaseUrl": "https://keeptrack.space/api",
    "KeepTrackApiKey": "",
    "KeepTrackTimeoutSeconds": 5
  },
  "Cors": {
    "AllowedOrigins": []
  },
  "Sessions": {
    "TtlMinutes": 30
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=missionclear.db"
  }
}
```

### appsettings.Development.json

**File:** `MissionClear.Api/appsettings.Development.json`

```json
{
  "Jwt": {
    "Secret": "dev-only-secret-change-me-please-32chars-minimum-xxxxx"
  },
  "Cors": {
    "AllowedOrigins": ["*"]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

**Security note:** `appsettings.Production.json` is in `.gitignore`. Production `Jwt__Secret` must be provided via environment variable.

- [ ] Commit: `chore: add appsettings with dev defaults`

---

## Phase 5: Program.cs (commit 5)

**File:** `MissionClear.Api/Program.cs`

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MissionClear.Api.Configuration;

var builder = WebApplication.CreateBuilder(args);

// ── Startup guard ───────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret must be at least 32 characters. Set it via environment variable Jwt__Secret.");

// ── Configuration binding ───────────────────────────────────────────────────
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<OrbitalSettings>(builder.Configuration.GetSection(OrbitalSettings.SectionName));
builder.Services.Configure<ExternalApiSettings>(builder.Configuration.GetSection(ExternalApiSettings.SectionName));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection(CorsSettings.SectionName));

// ── CORS ────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(opt => opt.AddPolicy("MobileApp", policy =>
{
    if (allowedOrigins.Contains("*"))
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<MissionClear.Api.Data.AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── HTTP clients ─────────────────────────────────────────────────────────────
builder.Services.AddHttpClient();

// ── Authentication ──────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            // Disable default claim type mapping so "sub" stays as "sub"
            NameClaimType = "sub"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

var app = builder.Build();

app.UseMiddleware<MissionClear.Api.Middleware.GlobalExceptionMiddleware>();
app.UseCors("MobileApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
```

- [ ] `dotnet build` — must succeed
- [ ] Commit: `feat: Program.cs with startup guard, JWT, CORS, DI skeleton`

---

## Phase 6: Configuration Tests (commit 6)

**File:** `MissionClear.Tests/Configuration/AppSettingsTests.cs`

```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MissionClear.Api.Configuration;
using Xunit;

namespace MissionClear.Tests.Configuration;

public class AppSettingsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    [Fact]
    public void JwtSettings_BindsCorrectly()
    {
        var config = BuildConfig(new()
        {
            ["Jwt:Secret"] = "exactly-32-characters-long-secret!",
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:AccessTokenMinutes"] = "30",
            ["Jwt:RefreshTokenDays"] = "14"
        });

        var settings = config.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;

        settings.Secret.Should().Be("exactly-32-characters-long-secret!");
        settings.Issuer.Should().Be("test-issuer");
        settings.AccessTokenMinutes.Should().Be(30);
        settings.RefreshTokenDays.Should().Be(14);
    }

    [Fact]
    public void OrbitalSettings_BindsWithDefaults()
    {
        var config = BuildConfig(new());
        var settings = config.GetSection(OrbitalSettings.SectionName).Get<OrbitalSettings>()
            ?? new OrbitalSettings();

        settings.TleFetchIntervalMinutes.Should().Be(60);
        settings.PropagationIntervalSeconds.Should().Be(60);
        settings.MaxDebrisCount.Should().Be(30000);
        settings.TleMaxAgeDays.Should().Be(7);
    }

    [Fact]
    public void ExternalApiSettings_BindsCelesTrakUrl()
    {
        var config = BuildConfig(new()
        {
            ["ExternalApi:CelesTrakBaseUrl"] = "https://celestrak.org/test",
            ["ExternalApi:KeepTrackApiKey"] = ""
        });

        var settings = config.GetSection(ExternalApiSettings.SectionName).Get<ExternalApiSettings>()!;

        settings.CelesTrakBaseUrl.Should().Be("https://celestrak.org/test");
        settings.KeepTrackApiKey.Should().BeEmpty();
    }

    [Fact]
    public void JwtSecret_ShorterThan32_ShouldFailValidation()
    {
        var secret = "short";
        var isValid = !string.IsNullOrWhiteSpace(secret) && secret.Length >= 32;
        isValid.Should().BeFalse("secrets shorter than 32 chars must be rejected at startup");
    }
}
```

- [ ] `dotnet test --filter AppSettingsTests` — all 4 pass (GREEN)
- [ ] Commit: `test(config): add AppSettingsTests for configuration binding`

---

## Testing Strategy

- 4 unit tests covering configuration binding correctness
- No integration tests at this phase (no HTTP layer yet)
- All subsequent plans assume this scaffold is green

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Jwt:Secret empty in dev → startup crash | `appsettings.Development.json` has a 50-char dev secret |
| Production secret committed to git | `appsettings.Production.json` in `.gitignore`; use `Jwt__Secret` env var |
| SQLite not available in CI | EF Core InMemory used in tests (plan-01+); SQLite only for runtime |
| Namespace drift between plans | All plans use `MissionClear.Api.*` namespace prefix |

---

## Success Criteria

- [ ] `dotnet build` passes with 0 warnings, 0 errors
- [ ] `dotnet test` passes with 0 failures (4 tests)
- [ ] `MissionClear.Api` project has all required NuGet packages installed
- [ ] All 4 configuration POCOs present with `SectionName` constant
- [ ] `Program.cs` throws `InvalidOperationException` when `Jwt:Secret` < 32 chars
- [ ] Folder skeleton created (`Controllers/`, `Services/`, `Data/`, `Domain/`, `Entities/`, `Dtos/`, `Models/`, `Exceptions/`, `Cache/`, `Configuration/`, `Middleware/`)
- [ ] `appsettings.Development.json` has dev secret (50+ chars)
- [ ] 6 atomic commits with conventional-commit messages

---

## Relevant Files

- `MissionClear.sln`
- `MissionClear.Api/MissionClear.Api.csproj`
- `MissionClear.Tests/MissionClear.Tests.csproj`
- `MissionClear.Api/Program.cs`
- `MissionClear.Api/Configuration/JwtSettings.cs`
- `MissionClear.Api/Configuration/OrbitalSettings.cs`
- `MissionClear.Api/Configuration/ExternalApiSettings.cs`
- `MissionClear.Api/Configuration/CorsSettings.cs`
- `MissionClear.Api/appsettings.json`
- `MissionClear.Api/appsettings.Development.json`
- `MissionClear.Tests/Configuration/AppSettingsTests.cs`
- `.gitignore`
