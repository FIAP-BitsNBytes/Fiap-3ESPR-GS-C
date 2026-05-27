# Mission Clear — Backend C# Implementation Plan v2.0

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement task-by-task.
>
> **AUTORIDADE ABSOLUTA:** `docs/API_CONTRACT.md` é a fonte da verdade de todos os contratos, campos e schemas. Em caso de conflito entre este plano e o contrato, o **contrato vence**.

**Goal:** Motor orbital completo — ingere TLEs reais, propaga via SGP4, detecta conjunções, calcula janelas de lançamento, autentica usuários com JWT, persiste histórico de missões em SQLite, expõe simulação dinâmica via SSE e 23 endpoints REST.

**Architecture:** Single-process ASP.NET Core 8. `IHostedService` faz ingestão/propagação em background. `OrbitalCache` serve posições em memória. SQLite persiste usuários, refresh tokens e histórico de missões. JWT autentica rotas protegidas. SSE entrega stream de simulação ao vivo.

**Tech Stack:** .NET 8, ASP.NET Core (Controllers), EF Core 8 + SQLite, JWT Bearer, BCrypt.Net-Next, SGP4 (NuGet), System.Text.Json, IHttpClientFactory, xUnit, FluentAssertions, Moq.

**Contrato de API:** `docs/API_CONTRACT.md` — 23 rotas, todos os schemas, todos os campos.

---

## Decisões Arquiteturais

| Decisão | Escolha | Motivo |
|---|---|---|
| Banco de dados | SQLite via EF Core | Local, zero setup, suficiente para MVP |
| Auth | JWT Bearer (access 1h + refresh 7d) | Padrão mobile, stateless |
| Senha | BCrypt.Net-Next | Hash seguro, nunca plaintext |
| IDs | Prefixo + Guid (`usr_`, `msn_`, `sess_`) | Legível, não-colidente |
| Cache orbital | `ConcurrentDictionary` em memória | TLEs mudam lentamente, propagação a cada 60s |
| Simulação dinâmica | SSE (Server-Sent Events) | Unidirecional, simples, funciona bem em RN |
| KeepTrack | Opcional/fallback, timeout 5s | Instável — nunca derruba o sistema |
| SGP4 | Biblioteca NuGet | Nunca reimplementar |
| Deduplicação | NORAD_CAT_ID, CelesTrak vence | Definido no contrato |

---

## Estrutura Completa de Arquivos

```
MissionClear.Api/
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
│
├── Configuration/
│   └── AppSettings.cs               # OrbitalSettings, ExternalApiSettings, JwtSettings, CorsSettings
│
├── Data/
│   ├── AppDbContext.cs              # EF Core DbContext
│   └── Migrations/                  # Auto-geradas pelo EF
│
├── Entities/                        # Entidades do banco
│   ├── UserEntity.cs
│   ├── RefreshTokenEntity.cs
│   └── MissionEntity.cs
│
├── Models/
│   ├── Tle/
│   │   ├── TleRecord.cs
│   │   └── CelesTrakGpRecord.cs
│   ├── Domain/
│   │   ├── OrbitalObject.cs
│   │   ├── MissionDestination.cs
│   │   ├── ConjunctionResult.cs
│   │   ├── LaunchWindow.cs
│   │   └── MissionSession.cs        # Estado em memória da sessão SSE
│   └── Api/                         # DTOs — tudo derivado do API_CONTRACT.md
│       ├── Auth/
│       │   ├── RegisterRequestDto.cs
│       │   ├── LoginRequestDto.cs
│       │   ├── RefreshRequestDto.cs
│       │   ├── LogoutRequestDto.cs
│       │   └── AuthResponseDto.cs
│       ├── User/
│       │   ├── UserResponseDto.cs
│       │   └── UpdateUserRequestDto.cs
│       ├── Debris/
│       │   ├── DebrisDto.cs
│       │   ├── DebrisDetailDto.cs
│       │   └── DebrisStatsDto.cs
│       ├── Destination/
│       │   └── DestinationDto.cs
│       ├── LaunchWindow/
│       │   ├── LaunchWindowsResponseDto.cs
│       │   └── BestWindowsResponseDto.cs
│       ├── Mission/
│       │   ├── SimulateRequestDto.cs
│       │   ├── SimulateResponseDto.cs
│       │   ├── SessionRequestDto.cs
│       │   ├── SessionResponseDto.cs
│       │   └── SessionCompleteRequestDto.cs
│       ├── History/
│       │   ├── MissionHistoryDto.cs
│       │   ├── MissionDetailDto.cs
│       │   └── MissionStatsDto.cs
│       ├── Dashboard/
│       │   ├── DashboardSummaryDto.cs
│       │   └── DashboardAlertsDto.cs
│       ├── ApiErrorDto.cs
│       └── PaginationDto.cs
│
├── Cache/
│   └── OrbitalCache.cs
│
├── Services/
│   ├── Background/
│   │   └── TleIngestionService.cs
│   ├── DataAggregatorService.cs
│   ├── OrbitalEngineService.cs
│   ├── ConjunctionDetectorService.cs
│   ├── LaunchWindowCalculatorService.cs
│   ├── MissionSessionService.cs     # Gerencia sessões SSE em memória
│   ├── MissionHistoryService.cs     # Persiste/consulta histórico no SQLite
│   ├── DashboardService.cs
│   ├── AuthService.cs               # register, login, refresh, logout
│   ├── JwtService.cs                # generate/validate JWT
│   └── UserService.cs               # get/update perfil
│
├── Controllers/
│   ├── AuthController.cs
│   ├── UsersController.cs
│   ├── DebrisController.cs
│   ├── DestinationsController.cs
│   ├── LaunchWindowsController.cs
│   ├── MissionController.cs         # simulate + session
│   ├── MissionsController.cs        # histórico
│   ├── DashboardController.cs
│   └── StatusController.cs
│
└── Middleware/
    └── GlobalExceptionMiddleware.cs

MissionClear.Tests/
├── Services/
│   ├── OrbitalEngineServiceTests.cs
│   ├── ConjunctionDetectorServiceTests.cs
│   ├── LaunchWindowCalculatorServiceTests.cs
│   ├── DataAggregatorServiceTests.cs
│   ├── JwtServiceTests.cs
│   └── AuthServiceTests.cs
└── Controllers/
    ├── DebrisControllerTests.cs
    └── AuthControllerTests.cs
```

---

## Fase 0: Scaffolding

### Task 0.1: Criar solução e projetos

**Files:**
- Create: `MissionClear.sln`
- Create: `MissionClear.Api/MissionClear.Api.csproj`
- Create: `MissionClear.Tests/MissionClear.Tests.csproj`

- [ ] **Step 1: Criar solução**

```bash
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet new sln -n MissionClear
dotnet new webapi -n MissionClear.Api --framework net8.0 --use-controllers
dotnet new xunit -n MissionClear.Tests --framework net8.0
dotnet sln add MissionClear.Api/MissionClear.Api.csproj
dotnet sln add MissionClear.Tests/MissionClear.Tests.csproj
dotnet add MissionClear.Tests/MissionClear.Tests.csproj reference MissionClear.Api/MissionClear.Api.csproj
```

- [ ] **Step 2: Pacotes — MissionClear.Api**

```bash
cd MissionClear.Api
dotnet add package Microsoft.EntityFrameworkCore --version 8.*
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 8.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.*
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.*
dotnet add package BCrypt.Net-Next --version 4.*
dotnet add package SGP4
```

> **Nota SGP4:** Execute `dotnet package search SGP4` e escolha o pacote com mais downloads e commit recente que exponha propagação TLE. Se não encontrar `SGP4`, tente `Orbit.Sgp4`. O `OrbitalEngineService` tem stub que funciona sem o pacote — integre depois.

- [ ] **Step 3: Pacotes — MissionClear.Tests**

```bash
cd ../MissionClear.Tests
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 8.*
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.*
dotnet add package Moq --version 4.*
dotnet add package FluentAssertions --version 6.*
```

- [ ] **Step 4: Limpar arquivos gerados**

```bash
cd ../MissionClear.Api
rm -f Controllers/WeatherForecastController.cs WeatherForecast.cs
```

- [ ] **Step 5: Criar estrutura de diretórios**

```bash
mkdir -p Configuration Data Entities Cache Middleware
mkdir -p Models/Tle Models/Domain
mkdir -p Models/Api/Auth Models/Api/User Models/Api/Debris
mkdir -p Models/Api/Destination Models/Api/LaunchWindow
mkdir -p Models/Api/Mission Models/Api/History Models/Api/Dashboard
mkdir -p Services/Background
mkdir -p ../MissionClear.Tests/Services ../MissionClear.Tests/Controllers
```

- [ ] **Step 6: Verificar build**

```bash
cd ..
dotnet build
```
Esperado: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add .
git commit -m "chore: scaffold solução MissionClear.Api + Tests com todos os pacotes"
```

---

### Task 0.2: Configuração tipada

**Files:**
- Modify: `MissionClear.Api/appsettings.json`
- Create: `MissionClear.Api/appsettings.Development.json`
- Create: `MissionClear.Api/Configuration/AppSettings.cs`

- [ ] **Step 1: appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "Default": "Data Source=missionclear.db"
  },
  "Jwt": {
    "Secret": "",
    "Issuer": "mission-clear-api",
    "Audience": "mission-clear-mobile",
    "AccessTokenExpirationHours": 1,
    "RefreshTokenExpirationDays": 7
  },
  "OrbitalSettings": {
    "TleRefreshIntervalMinutes": 60,
    "PropagationIntervalSeconds": 60,
    "TleStaleDays": 7,
    "SseDebrisUpdateIntervalSeconds": 30,
    "SseHeartbeatIntervalSeconds": 15
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

- [ ] **Step 2: appsettings.Development.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },
  "Jwt": {
    "Secret": "dev-secret-min-32-chars-mission-clear-2026"
  }
}
```

> JWT Secret em produção: variável de ambiente `JWT__SECRET`. Nunca commitar segredo real.

- [ ] **Step 3: Configuration/AppSettings.cs**

```csharp
namespace MissionClear.Api.Configuration;

public class JwtSettings
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "mission-clear-api";
    public string Audience { get; init; } = "mission-clear-mobile";
    public int AccessTokenExpirationHours { get; init; } = 1;
    public int RefreshTokenExpirationDays { get; init; } = 7;
}

public class OrbitalSettings
{
    public int TleRefreshIntervalMinutes { get; init; } = 60;
    public int PropagationIntervalSeconds { get; init; } = 60;
    public int TleStaleDays { get; init; } = 7;
    public int SseDebrisUpdateIntervalSeconds { get; init; } = 30;
    public int SseHeartbeatIntervalSeconds { get; init; } = 15;
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
git commit -m "chore: configuração tipada JWT, Orbital, ExternalApis, CORS"
```

---

## Fase 1: Banco de Dados (EF Core + SQLite)

### Task 1.1: Entidades do banco

**Files:**
- Create: `MissionClear.Api/Entities/UserEntity.cs`
- Create: `MissionClear.Api/Entities/RefreshTokenEntity.cs`
- Create: `MissionClear.Api/Entities/MissionEntity.cs`

- [ ] **Step 1: UserEntity.cs**

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Entities;

public class UserEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;          // "usr_" + Guid

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; } = [];
    public ICollection<MissionEntity> Missions { get; set; } = [];
}
```

- [ ] **Step 2: RefreshTokenEntity.cs**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

public class RefreshTokenEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; } = false;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
```

- [ ] **Step 3: MissionEntity.cs**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

public class MissionEntity
{
    [Key]
    public string Id { get; set; } = string.Empty;          // "msn_" + Guid

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Destination { get; set; } = string.Empty;

    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = string.Empty;      // success | failure | aborted

    public int MissionScore { get; set; }
    public double RiskScore { get; set; }
    public double DeltaVKmS { get; set; }
    public int ObstaclesEncountered { get; set; }

    // JSON serializado — lista de ObstacleDto
    public string ObstaclesJson { get; set; } = "[]";

    // JSON serializado — { efficiency_score, safety_score, total }
    public string ScoreBreakdownJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
```

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: entidades EF Core User, RefreshToken, Mission"
```

---

### Task 1.2: DbContext e Migrations

**Files:**
- Create: `MissionClear.Api/Data/AppDbContext.cs`

- [ ] **Step 1: AppDbContext.cs**

```csharp
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<MissionEntity> Missions => Set<MissionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshTokenEntity>(e =>
        {
            e.HasIndex(r => r.Token).IsUnique();
            e.HasOne(r => r.User)
             .WithMany(u => u.RefreshTokens)
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MissionEntity>(e =>
        {
            e.HasOne(m => m.User)
             .WithMany(u => u.Missions)
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

- [ ] **Step 2: Criar migration inicial**

```bash
cd MissionClear.Api
dotnet ef migrations add InitialCreate --output-dir Data/Migrations
dotnet ef database update
```
Esperado: arquivo `missionclear.db` criado na raiz de `MissionClear.Api/`.

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: AppDbContext + migration InitialCreate (SQLite)"
```
