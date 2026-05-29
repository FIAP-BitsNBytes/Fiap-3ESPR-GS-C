# Plan 02 — Domain Models + DTOs + DomainException

**Execution order:** After plan-00 / phase-01. Parallel with nothing — other phases depend on this.
**Estimated time:** 30–40 minutes.
**Goal:** Criar `DomainException`, todos os domain records e todos os DTOs JSON snake_case do contrato de API — base imutável usada por todos os outros planos.
**Dependencies:** `MissionClear.Api` compilando limpo (plan-00 / phase-00 completo). `MissionClear.Api/Models/RiskLevel.cs` já existe — manter.
**Unlocks:** phase-03-orbital, phase-04-auth, phase-05-simulation, phase-06-history-dashboard, phase-07-api-controllers, phase-08-mvc-web — todos consomem estes tipos.

---

## Diretrizes globais

- `<Nullable>enable</Nullable>` no `.csproj` (já feito). Toda string default = `string.Empty`, nunca `null`.
- Todos os DTOs são `record` (imutáveis), com parâmetros posicionais ou `init`-only properties.
- Serialização: `JsonSerializerOptions` com `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` configurado no `Program.cs` — **não duplicar** `[JsonPropertyName]` em cada propriedade.
- Records de domínio (não-DTO) usam PascalCase puro, sem atributos JSON.
- Nenhum arquivo passa de 200 linhas. Um tipo por arquivo (exceto enums pequenos e records auxiliares estreitamente relacionados).
- Após cada fase: `dotnet build MissionClear.Api` deve compilar limpo.
- IDs prefixados: `usr_{Guid:N}`, `msn_{Guid:N}`, `sess_{Guid:N}`.
- Timestamps: `DateTime.UtcNow`, serializados no formato `O` (ISO 8601 UTC).

---

## Phase 1 — DomainException

### Task 1.1 — Criar diretório e `DomainException.cs`

**File:** `MissionClear.Api/Exceptions/DomainException.cs`

```powershell
New-Item -ItemType Directory -Force MissionClear.Api/Exceptions
```

```csharp
namespace MissionClear.Api.Exceptions;

/// <summary>
/// Exceção de domínio com código de erro e HTTP status.
/// Capturada pelo GlobalExceptionMiddleware e serializada como ApiErrorDto.
/// </summary>
public sealed class DomainException(string errorCode, string message, int httpStatus = 400)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int HttpStatus { get; } = httpStatus;
}
```

**Códigos de erro canônicos (section 14 da API_CONTRACT.md):**

| ErrorCode | HttpStatus |
|---|---|
| `INVALID_DESTINATION` | 400 |
| `TIME_RANGE_EXCEEDED` | 400 |
| `INVALID_TIME_RANGE` | 400 |
| `MISSING_PARAMETER` | 400 |
| `INVALID_DATE_FORMAT` | 400 |
| `INVALID_PASSWORD_FORMAT` | 400 |
| `INVALID_CURRENT_PASSWORD` | 401 |
| `INVALID_CREDENTIALS` | 401 |
| `TOKEN_EXPIRED` | 401 |
| `INVALID_REFRESH_TOKEN` | 401 |
| `UNAUTHORIZED` | 401 |
| `FORBIDDEN` | 403 |
| `DEBRIS_NOT_FOUND` | 404 |
| `MISSION_NOT_FOUND` | 404 |
| `SESSION_NOT_FOUND` | 404 |
| `EMAIL_ALREADY_EXISTS` | 409 |
| `SESSION_ALREADY_COMPLETED` | 409 |
| `CACHE_NOT_READY` | 503 |
| `INTERNAL_ERROR` | 500 |

**Uso nos Services:**
```csharp
throw new DomainException("EMAIL_ALREADY_EXISTS", "Email já cadastrado.", 409);
throw new DomainException("INVALID_CREDENTIALS", "Email ou senha incorretos.", 401);
throw new DomainException("MISSION_NOT_FOUND", "Missão não encontrada.", 404);
throw new DomainException("FORBIDDEN", "Acesso negado.", 403);
throw new DomainException("CACHE_NOT_READY", "Cache orbital inicializando.", 503);
throw new DomainException("INVALID_DESTINATION", "Destino inválido. Use ISS, LEO_GENERIC ou SSO.", 400);
throw new DomainException("SESSION_NOT_FOUND", "Sessão expirada ou não encontrada.", 404);
throw new DomainException("SESSION_ALREADY_COMPLETED", "Sessão já foi finalizada.", 409);
```

### Task 1.2 — Build check

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj
```

---

## Phase 2 — Domain Models

### Task 2.1 — `Models/OrbitalObject.cs`

**File:** `MissionClear.Api/Models/OrbitalObject.cs`

> Manter `MissionClear.Api/Models/RiskLevel.cs` existente — não alterar.

```csharp
namespace MissionClear.Api.Models;

/// <summary>
/// Objeto orbital propagado (debris, satélite ou rocket body) em um instante.
/// Imutável. Produzido pelo OrbitalEngine, consumido pelo ConjunctionDetector e pelos controllers.
/// Campos TLE/orbit opcionais — presentes apenas quando fetched via /api/debris/{id}.
/// </summary>
public sealed record OrbitalObject(
    string Id,
    string Name,
    string Type,
    double Latitude,
    double Longitude,
    double AltitudeKm,
    double VelocityKmS,
    string Source,
    DateTime UpdatedAt,
    string? TleLine1 = null,
    string? TleLine2 = null,
    string? TleEpoch = null,
    double? InclinationDeg = null,
    double? Eccentricity = null,
    double? PeriodMinutes = null,
    double? ApogeeKm = null,
    double? PerigeeKm = null);
```

### Task 2.2 — `Models/MissionDestination.cs` + `KnownDestinations`

**File:** `MissionClear.Api/Models/MissionDestination.cs`

> Valores fixos conforme `API_CONTRACT.md` section 4 e `GET /api/destinations`.

```csharp
namespace MissionClear.Api.Models;

/// <summary>
/// Destino orbital pré-definido. AltitudeKm e InclinationDeg alimentam o LaunchWindowCalculator.
/// LatitudeDeg/LongitudeDeg padrão 0.0 — órbitas tratadas como equatoriais na simulação MVP.
/// </summary>
public sealed record MissionDestination(
    string Id,
    string DisplayName,
    double AltitudeKm,
    double InclinationDeg,
    string Description,
    double DeltaVKmS,
    double MissionDurationHours,
    string Icon,
    double LatitudeDeg = 0.0,
    double LongitudeDeg = 0.0);

public static class KnownDestinations
{
    public static readonly MissionDestination ISS = new(
        "ISS",
        "Estação Espacial Internacional",
        408,
        51.6,
        "Órbita da ISS — destino mais popular para missões LEO",
        9.40,
        6.2,
        "iss");

    public static readonly MissionDestination LeoGeneric = new(
        "LEO_GENERIC",
        "Órbita LEO Genérica",
        400,
        28.5,
        "Órbita baixa padrão para satélites de observação",
        9.20,
        5.8,
        "leo");

    public static readonly MissionDestination Sso = new(
        "SSO",
        "Sun-Synchronous Orbit",
        500,
        97.4,
        "Órbita heliosíncrona — usada por satélites de imageamento",
        10.10,
        7.0,
        "sso");

    public static readonly IReadOnlyList<MissionDestination> All = [ISS, LeoGeneric, Sso];

    public static MissionDestination? FindById(string id) =>
        All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    public static MissionDestination? Get(string id) => FindById(id);
}
```

### Task 2.3 — `Models/ConjunctionResult.cs`

**File:** `MissionClear.Api/Models/ConjunctionResult.cs`

```csharp
using MissionClear.Api.Helpers;

namespace MissionClear.Api.Models;

/// <summary>
/// Aproximação detectada entre um objeto orbital e uma trajetória de missão.
/// RiskLevel calculado pelo RiskScoring helper.
/// </summary>
public sealed record ConjunctionResult(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    DateTime TimeOfClosestApproach,
    RiskLevel RiskLevel);
```

### Task 2.4 — `Models/LaunchWindow.cs`

**File:** `MissionClear.Api/Models/LaunchWindow.cs`

```csharp
namespace MissionClear.Api.Models;

/// <summary>
/// Janela temporal de lançamento avaliada. RiskScore in [0,1]; menor é melhor.
/// IsRecommended = true quando RiskScore &lt; 0.1.
/// </summary>
public sealed record LaunchWindow(
    DateTime Start,
    DateTime End,
    double RiskScore,
    double DeltaVKmS,
    double DurationHours,
    bool IsRecommended,
    IReadOnlyList<ConjunctionResult> Conjunctions);
```

### Task 2.5 — `Models/MissionSession.cs`

**File:** `MissionClear.Api/Models/MissionSession.cs`

```csharp
namespace MissionClear.Api.Models;

public enum SessionStatus { Active, Completed, Expired }

/// <summary>
/// Sessão de simulação ao vivo (SSE). Mutável: Status muda quando a simulação termina.
/// Mantida em memória pelo SessionStore. Não persiste a menos que o usuário peça save_to_history.
/// TTL padrão: 30 minutos a partir da criação.
/// </summary>
public sealed class MissionSession
{
    public string SessionId { get; init; } = $"sess_{Guid.NewGuid():N}";
    public required string Destination { get; init; }
    public required DateTime DepartureTime { get; init; }
    public required DateTime ArrivalTime { get; init; }
    public DateTime ExpiresAt { get; init; } = DateTime.UtcNow.AddMinutes(30);
    public required Guid UserId { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public double RiskScore { get; set; }
    public double DeltaVKmS { get; set; }
    public int MissionScore { get; set; }
    public int ObstaclesEncountered { get; set; }
    public List<ConjunctionResult> Conjunctions { get; } = [];

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}
```

### Task 2.6 — Build check + commit

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj
git add MissionClear.Api/Exceptions/ MissionClear.Api/Models/OrbitalObject.cs MissionClear.Api/Models/MissionDestination.cs MissionClear.Api/Models/ConjunctionResult.cs MissionClear.Api/Models/LaunchWindow.cs MissionClear.Api/Models/MissionSession.cs
git commit -m "feat(models): DomainException + domain records (OrbitalObject, MissionDestination, ConjunctionResult, LaunchWindow, MissionSession)"
```

---

## Phase 3 — DTOs de Auth

**Directory:** `MissionClear.Api/Dtos/Auth/`

```powershell
New-Item -ItemType Directory -Force MissionClear.Api/Dtos/Auth
```

### Task 3.1 — `Dtos/Auth/RegisterRequest.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record RegisterRequest(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    [Required][StringLength(50, MinimumLength = 2)] string DisplayName,
    string Role = "Researcher");
```

### Task 3.2 — `Dtos/Auth/LoginRequest.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record LoginRequest(
    [Required][EmailAddress] string Email,
    [Required] string Password);
```

### Task 3.3 — `Dtos/Auth/RefreshRequest.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record RefreshRequest([Required] string RefreshToken);
```

### Task 3.4 — `Dtos/Auth/LogoutRequest.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record LogoutRequest([Required] string RefreshToken);
```

### Task 3.5 — `Dtos/Auth/AuthResponse.cs`

> `UserInAuthResponse` inclui `Role` — obrigatório para autorização no Mobile e no MVC.
> `user.id` usa prefixo `usr_` (gerado pelo UserRepository).
> `register` retorna sem `total_missions`/`best_score` (usuário novo); `login` retorna com eles.

```csharp
namespace MissionClear.Api.Dtos.Auth;

/// <summary>
/// Objeto user incluído nas respostas de login e register.
/// total_missions e best_score são opcionais: null em register (conta nova), populados em login.
/// </summary>
public sealed record UserInAuthResponse(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    string CreatedAt,
    int? TotalMissions = null,
    int? BestScore = null);

public sealed record AuthResponse(
    UserInAuthResponse User,
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

public sealed record RefreshTokenResponse(
    string AccessToken,
    int ExpiresIn);
```

### Task 3.6 — Build check + commit

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj
git add MissionClear.Api/Dtos/Auth/
git commit -m "feat(dtos): auth DTOs (RegisterRequest, LoginRequest, RefreshRequest, LogoutRequest, AuthResponse)"
```

---

## Phase 4 — DTOs de User

**Directory:** `MissionClear.Api/Dtos/User/`

```powershell
New-Item -ItemType Directory -Force MissionClear.Api/Dtos/User
```

### Task 4.1 — `Dtos/User/UserProfileResponse.cs`

> Campos de `stats` conforme `GET /api/users/me` (section 6).
> `average_score` é `int` no contrato (`"average_score": 81`).

```csharp
namespace MissionClear.Api.Dtos.User;

public sealed record UserStatsDto(
    int TotalMissions,
    int SuccessfulMissions,
    int FailedMissions,
    int AbortedMissions,
    double SuccessRate,
    int BestScore,
    int AverageScore,
    string? FavoriteDestination,
    double TotalDeltaVKmS);

public sealed record UserProfileResponse(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    string CreatedAt,
    UserStatsDto Stats);
```

### Task 4.2 — `Dtos/User/UpdateUserRequest.cs`

> `current_password` é obrigatório somente quando `password` está presente — validado no Service.

```csharp
namespace MissionClear.Api.Dtos.User;

public sealed record UpdateUserRequest(
    string? DisplayName,
    string? Password,
    string? CurrentPassword);
```

---

## Phase 5 — DTOs Orbitais

**Directories:**
```powershell
New-Item -ItemType Directory -Force MissionClear.Api/Dtos/Orbital
New-Item -ItemType Directory -Force MissionClear.Api/Dtos/Common
New-Item -ItemType Directory -Force MissionClear.Api/Dtos/Status
New-Item -ItemType Directory -Force MissionClear.Api/Dtos/Destination
```

### Task 5.1 — `Dtos/Orbital/DebrisDto.cs`

> Campos conforme `GET /api/debris` (section 7). Serialização snake_case via `JsonNamingPolicy`.

```csharp
namespace MissionClear.Api.Dtos.Orbital;

public sealed record DebrisDto(
    string Id,
    string Name,
    string Type,
    double Latitude,
    double Longitude,
    double AltitudeKm,
    double VelocityKmS,
    string Source,
    string UpdatedAt);
```

### Task 5.2 — `Dtos/Orbital/DebrisDetailDto.cs`

> Campos conforme `GET /api/debris/{id}` (section 7).

```csharp
namespace MissionClear.Api.Dtos.Orbital;

public sealed record TleDto(
    string Epoch,
    string Line1,
    string Line2);

public sealed record OrbitParamsDto(
    double InclinationDeg,
    double Eccentricity,
    double PeriodMinutes,
    double ApogeeKm,
    double PerigeeKm);

public sealed record DebrisDetailDto(
    string Id,
    string Name,
    string Type,
    double Latitude,
    double Longitude,
    double AltitudeKm,
    double VelocityKmS,
    string Source,
    string UpdatedAt,
    TleDto? Tle,
    OrbitParamsDto? Orbit);
```

### Task 5.3 — `Dtos/Orbital/DebrisStatsDto.cs`

> Campos conforme `GET /api/debris/stats` (section 7).
> `by_altitude_band` usa chaves exatas do contrato: `low_200_500km`, `mid_500_1000km`, `high_1000_2000km`.

```csharp
namespace MissionClear.Api.Dtos.Orbital;

public sealed record ByTypeDto(
    int Debris,
    int Satellite,
    int RocketBody);

/// <summary>
/// Snake_case via JsonNamingPolicy produz: low_200_500_km — incorreto.
/// Usar [JsonPropertyName] explícito apenas neste record para forçar as chaves exatas do contrato.
/// </summary>
public sealed record ByAltitudeBandDto(
    [property: System.Text.Json.Serialization.JsonPropertyName("low_200_500km")] int Low200500km,
    [property: System.Text.Json.Serialization.JsonPropertyName("mid_500_1000km")] int Mid5001000km,
    [property: System.Text.Json.Serialization.JsonPropertyName("high_1000_2000km")] int High10002000km);

public sealed record SourcesDto(
    int Celestrak,
    int Keeptrack);

public sealed record DebrisStatsDto(
    int TotalTracked,
    ByTypeDto ByType,
    ByAltitudeBandDto ByAltitudeBand,
    SourcesDto Sources,
    string LastUpdated);
```

### Task 5.4 — `Dtos/Common/ApiErrorDto.cs`

> Shape conforme `API_CONTRACT.md` section 3. Todos os error codes de section 14 representados.
> Capturado e serializado pelo `GlobalExceptionMiddleware` quando `DomainException` é lançada.

```csharp
namespace MissionClear.Api.Dtos.Common;

public sealed record ApiErrorDto(string Error, string Message, string Timestamp)
{
    public static ApiErrorDto From(string code, string message) =>
        new(code, message, DateTime.UtcNow.ToString("O"));

    // Auth
    public static ApiErrorDto EmailAlreadyExists() =>
        From("EMAIL_ALREADY_EXISTS", "Este email já está cadastrado.");
    public static ApiErrorDto InvalidCredentials() =>
        From("INVALID_CREDENTIALS", "Email ou senha incorretos.");
    public static ApiErrorDto TokenExpired() =>
        From("TOKEN_EXPIRED", "Token de acesso expirado. Use o refresh token.");
    public static ApiErrorDto InvalidRefreshToken() =>
        From("INVALID_REFRESH_TOKEN", "Refresh token inválido ou revogado.");
    public static ApiErrorDto Unauthorized() =>
        From("UNAUTHORIZED", "Rota requer autenticação.");
    public static ApiErrorDto InvalidPasswordFormat() =>
        From("INVALID_PASSWORD_FORMAT", "Senha deve ter no mínimo 8 caracteres, 1 maiúscula e 1 número.");
    public static ApiErrorDto InvalidCurrentPassword() =>
        From("INVALID_CURRENT_PASSWORD", "Senha atual incorreta.");

    // Acesso
    public static ApiErrorDto Forbidden() =>
        From("FORBIDDEN", "Você não tem permissão para acessar este recurso.");

    // Not found
    public static ApiErrorDto DebrisNotFound(string id) =>
        From("DEBRIS_NOT_FOUND", $"Debris '{id}' não encontrado no cache.");
    public static ApiErrorDto MissionNotFound(string id) =>
        From("MISSION_NOT_FOUND", $"Missão '{id}' não encontrada.");
    public static ApiErrorDto SessionNotFound(string id) =>
        From("SESSION_NOT_FOUND", $"Sessão '{id}' expirada ou não encontrada.");

    // Conflito
    public static ApiErrorDto SessionAlreadyCompleted() =>
        From("SESSION_ALREADY_COMPLETED", "Esta sessão já foi finalizada.");

    // Validação orbital
    public static ApiErrorDto InvalidDestination(string id) =>
        From("INVALID_DESTINATION", $"Destino '{id}' não é suportado. Use ISS, LEO_GENERIC ou SSO.");
    public static ApiErrorDto TimeRangeExceeded() =>
        From("TIME_RANGE_EXCEEDED", "Período solicitado excede o limite de 48 horas.");
    public static ApiErrorDto InvalidTimeRange() =>
        From("INVALID_TIME_RANGE", "arrival_time deve ser posterior a departure_time.");
    public static ApiErrorDto MissingParameter(string param) =>
        From("MISSING_PARAMETER", $"Parâmetro obrigatório ausente: '{param}'.");
    public static ApiErrorDto InvalidDateFormat(string param) =>
        From("INVALID_DATE_FORMAT", $"Parâmetro '{param}' não está em formato ISO 8601 UTC.");

    // Sistema
    public static ApiErrorDto CacheNotReady() =>
        From("CACHE_NOT_READY", "Cache orbital ainda está inicializando. Tente novamente em alguns segundos.");
    public static ApiErrorDto InternalError() =>
        From("INTERNAL_ERROR", "Erro interno do servidor. Tente novamente.");
}
```

### Task 5.5 — `Dtos/Common/PaginationDto.cs`

```csharp
namespace MissionClear.Api.Dtos.Common;

public sealed record PaginationDto(int Page, int Limit, int Total, int TotalPages)
{
    public static PaginationDto From(int page, int limit, int total)
    {
        var safeLimit = Math.Max(1, limit);
        return new(page, safeLimit, total, (int)Math.Ceiling(total / (double)safeLimit));
    }
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Data, PaginationDto Pagination);
```

### Task 5.6 — `Dtos/Common/ConjunctionDto.cs`

> Campos exatos de `ConjunctionDto` / `ObstacleDto` — mesmos no contrato (section 15).

```csharp
namespace MissionClear.Api.Dtos.Common;

public sealed record ConjunctionDto(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    string TimeOfClosestApproach,
    string RiskLevel);
```

### Task 5.7 — `Dtos/Common/LaunchWindowDto.cs`

> Campos conforme `GET /api/launch-windows` (section 8).

```csharp
namespace MissionClear.Api.Dtos.Common;

public sealed record LaunchWindowDto(
    string Start,
    string End,
    double RiskScore,
    double DeltaVKmS,
    double DurationHours,
    bool IsRecommended,
    IReadOnlyList<ConjunctionDto> Conjunctions);

/// <summary>
/// Shape de GET /api/launch-windows — inclui total_windows e safe_windows.
/// </summary>
public sealed record LaunchWindowsResponse(
    string Destination,
    string From,
    string To,
    int TotalWindows,
    int SafeWindows,
    IReadOnlyList<LaunchWindowDto> Windows);
```

### Task 5.8 — `Dtos/Common/BestWindowDto.cs`

> Campos conforme `GET /api/launch-windows/best` (section 8).

```csharp
namespace MissionClear.Api.Dtos.Common;

public sealed record BestWindowDto(
    int Rank,
    string Start,
    string End,
    double RiskScore,
    double DeltaVKmS,
    double DurationHours,
    IReadOnlyList<ConjunctionDto> Conjunctions);

public sealed record BestWindowsResponse(
    string Destination,
    string From,
    string To,
    IReadOnlyList<BestWindowDto> BestWindows);
```

### Task 5.9 — `Dtos/Status/StatusResponse.cs`

> Campos conforme `GET /api/status` (section 12).

```csharp
namespace MissionClear.Api.Dtos.Status;

public sealed record SourceStatusDto(string Celestrak, string Keeptrack);

public sealed record StatusResponse(
    string Status,
    int TleCount,
    int PropagatedCount,
    string? LastTleFetch,
    string? LastPropagation,
    long UptimeSeconds,
    SourceStatusDto Sources);
```

### Task 5.10 — `Dtos/Destination/DestinationDto.cs`

> Campos conforme `GET /api/destinations` (section 7).

```csharp
namespace MissionClear.Api.Dtos.Destination;

public sealed record DestinationDto(
    string Id,
    string DisplayName,
    double AltitudeKm,
    double InclinationDeg,
    string Description,
    double DeltaVKmS,
    double MissionDurationHours,
    string Icon);

public sealed record DestinationsResponse(IReadOnlyList<DestinationDto> Destinations);
```

### Task 5.11 — Build check + commit

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj
git add MissionClear.Api/Dtos/Orbital/ MissionClear.Api/Dtos/Common/ MissionClear.Api/Dtos/Status/ MissionClear.Api/Dtos/Destination/ MissionClear.Api/Dtos/User/
git commit -m "feat(dtos): orbital, common envelopes, status, destination, user DTOs"
```

---

## Phase 6 — DTOs de Missão

**Directories:**
```powershell
New-Item -ItemType Directory -Force MissionClear.Api/Dtos/Mission
```

### Task 6.1 — `Dtos/Mission/SimulateRequest.cs`

> `POST /api/mission/simulate` (section 9).

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Mission;

public sealed record SimulateRequest(
    [Required] string Destination,
    DateTime DepartureTime,
    DateTime ArrivalTime);
```

### Task 6.2 — `Dtos/Mission/SimulateResponse.cs`

> Shape de `POST /api/mission/simulate` response (section 9).
> `trajectory` é array vazio no MVP. `obstacles` usa `ObstacleDto` (idêntico a `ConjunctionDto`).

```csharp
using MissionClear.Api.Dtos.Common;

namespace MissionClear.Api.Dtos.Mission;

/// <summary>
/// Obstáculo na trajetória — mesmo shape que ConjunctionDto (seção 15 do contrato).
/// Alias local para clareza semântica no contexto de simulação.
/// </summary>
public sealed record ObstacleDto(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    string TimeOfClosestApproach,
    string RiskLevel);

public sealed record SimulateResponse(
    string Destination,
    DateTime DepartureTime,
    DateTime ArrivalTime,
    IReadOnlyList<object> Trajectory,
    IReadOnlyList<ObstacleDto> Obstacles,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS);
```

### Task 6.3 — `Dtos/Mission/SessionRequest.cs`

> `POST /api/mission/session` request (section 9).

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Mission;

public sealed record SessionRequest(
    [Required] string Destination,
    [Required] string DepartureTime,
    [Required] string ArrivalTime);
```

### Task 6.4 — `Dtos/Mission/SessionResponse.cs`

> `POST /api/mission/session` response 201 (section 9).

```csharp
namespace MissionClear.Api.Dtos.Mission;

public sealed record SessionResponse(
    string SessionId,
    string Destination,
    string DepartureTime,
    string ArrivalTime,
    string StreamUrl,
    string ExpiresAt);
```

### Task 6.5 — `Dtos/Mission/CompleteSessionRequest.cs`

> `POST /api/mission/session/{sessionId}/complete` request (section 9).

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Mission;

public sealed record CompleteSessionRequest(
    [Required] string Status,
    bool SaveToHistory = false);
```

### Task 6.6 — `Dtos/Mission/CompleteSessionResponse.cs`

> `POST /api/mission/session/{sessionId}/complete` response 200 (section 9).
> `duration_seconds` é `double` (valor no contrato: `"duration_seconds": 22380`).
> `mission_id` usa prefixo `msn_` — null quando `save_to_history = false`.

```csharp
namespace MissionClear.Api.Dtos.Mission;

public sealed record CompleteSessionResponse(
    string SessionId,
    string Status,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS,
    int ObstaclesEncountered,
    double DurationSeconds,
    bool SavedToHistory,
    string? MissionId);
```

### Task 6.7 — Build check + commit

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj
git add MissionClear.Api/Dtos/Mission/
git commit -m "feat(dtos): mission DTOs (SimulateRequest/Response, Session lifecycle, CompleteSession)"
```

---

## Phase 7 — DTOs de Histórico e Dashboard

**Directories:**
```powershell
New-Item -ItemType Directory -Force MissionClear.Api/Dtos/History
New-Item -ItemType Directory -Force MissionClear.Api/Dtos/Dashboard
```

### Task 7.1 — `Dtos/History/MissionSummaryDto.cs`

> Shape de cada item em `GET /api/missions` data array (section 10).

```csharp
namespace MissionClear.Api.Dtos.History;

public sealed record MissionSummaryDto(
    string Id,
    string Destination,
    string DestinationDisplay,
    string Status,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS,
    int ObstaclesEncountered,
    string DepartureTime,
    string ArrivalTime,
    string CreatedAt);
```

### Task 7.2 — `Dtos/History/MissionDetailResponse.cs`

> Shape de `GET /api/missions/{id}` (section 10).

```csharp
using MissionClear.Api.Dtos.Mission;

namespace MissionClear.Api.Dtos.History;

public sealed record ScoreBreakdownDto(
    int EfficiencyScore,
    int SafetyScore,
    int Total);

public sealed record MissionDetailResponse(
    string Id,
    string Destination,
    string DestinationDisplay,
    string Status,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS,
    string DepartureTime,
    string ArrivalTime,
    string CreatedAt,
    IReadOnlyList<ObstacleDto> Obstacles,
    ScoreBreakdownDto ScoreBreakdown);
```

### Task 7.3 — `Dtos/History/MissionStatsResponse.cs`

> Shape de `GET /api/missions/stats` (section 10).
> `average_score` é `int` no contrato (`"average_score": 81`).
> `missions_by_destination` é objeto com chaves ISS/LEO_GENERIC/SSO — usar `Dictionary<string, int>`.

```csharp
namespace MissionClear.Api.Dtos.History;

public sealed record MissionStatsResponse(
    int TotalMissions,
    int SuccessfulMissions,
    int FailedMissions,
    int AbortedMissions,
    double SuccessRate,
    int BestScore,
    int WorstScore,
    int AverageScore,
    double TotalDeltaVKmS,
    int TotalObstaclesEncountered,
    string? FavoriteDestination,
    Dictionary<string, int> MissionsByDestination);
```

### Task 7.4 — `Dtos/Dashboard/DashboardSummaryResponse.cs`

> Shape de `GET /api/dashboard/summary` (section 11).
> `user` é null quando não autenticado.
> `ByAltitudeBandDto` aqui reutiliza a definição de `Dtos/Orbital/DebrisStatsDto.cs`
> **mas** precisa dos mesmos `[JsonPropertyName]` explícitos para as chaves com números.

```csharp
using MissionClear.Api.Dtos.Orbital;

namespace MissionClear.Api.Dtos.Dashboard;

public sealed record OrbitalSummaryDto(
    int TotalTrackedObjects,
    ByTypeDto ByType,
    ByAltitudeBandDto ByAltitudeBand,
    int ActiveConjunctionAlerts,
    string LastUpdated);

public sealed record LastMissionDto(
    string Destination,
    string Status,
    int Score,
    string CreatedAt);

public sealed record UserDashboardDto(
    string DisplayName,
    int TotalMissions,
    int BestScore,
    LastMissionDto? LastMission);

public sealed record DashboardSummaryResponse(
    OrbitalSummaryDto Orbital,
    UserDashboardDto? User);
```

### Task 7.5 — `Dtos/Dashboard/AlertsResponse.cs`

> Shape de `GET /api/dashboard/alerts` (section 11).
> `minutes_until_conjunction` é `int` no contrato (`"minutes_until_conjunction": 238`).

```csharp
namespace MissionClear.Api.Dtos.Dashboard;

public sealed record AlertDto(
    string Id,
    string DebrisId,
    string DebrisName,
    string AffectedDestination,
    double ClosestApproachKm,
    string TimeOfClosestApproach,
    string RiskLevel,
    int MinutesUntilConjunction,
    string DetectedAt);

public sealed record AlertsResponse(
    IReadOnlyList<AlertDto> Alerts,
    int WindowHours,
    string GeneratedAt);
```

### Task 7.6 — Build check + commit

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj
git add MissionClear.Api/Dtos/History/ MissionClear.Api/Dtos/Dashboard/
git commit -m "feat(dtos): history (MissionSummary, MissionDetail, MissionStats) and dashboard (Summary, Alerts)"
```

---

## Phase 8 — JSON Naming Policy em Program.cs

> Garantir que `JsonNamingPolicy.SnakeCaseLower` está configurado para todos os controllers.
> Localizar a seção de `AddControllers` ou `AddControllers().AddJsonOptions(...)` em `Program.cs`.

```csharp
// Em Program.cs — dentro do builder.Services block:
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.WriteIndented = false;
    });
```

> Adicionar using: `using System.Text.Json; using System.Text.Json.Serialization;`

> **Atenção:** `ByAltitudeBandDto` tem `[JsonPropertyName]` explícito — esse atributo tem precedência sobre a policy global. Verificar se `JsonNamingPolicy` + `[JsonPropertyName]` coexistem corretamente (coexistem — o atributo ganha).

### Task 8.1 — Build + test final

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj
dotnet build MissionClear.Tests/MissionClear.Tests.csproj
dotnet test MissionClear.Tests/MissionClear.Tests.csproj
git add MissionClear.Api/Program.cs
git commit -m "feat(config): configure SnakeCaseLower JSON naming policy for all controllers"
```

---

## Phase 9 — Compile-check Tests

**File:** `MissionClear.Tests/Models/DtoCompileTests.cs`

```csharp
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Dtos.Dashboard;
using MissionClear.Api.Dtos.Destination;
using MissionClear.Api.Dtos.History;
using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Dtos.Status;
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models;
using FluentAssertions;
using Xunit;

namespace MissionClear.Tests.Models;

public class DtoCompileTests
{
    [Fact]
    public void DomainException_stores_error_code_and_http_status()
    {
        var ex = new DomainException("EMAIL_ALREADY_EXISTS", "Email já cadastrado.", 409);
        ex.ErrorCode.Should().Be("EMAIL_ALREADY_EXISTS");
        ex.HttpStatus.Should().Be(409);
        ex.Message.Should().Be("Email já cadastrado.");
    }

    [Fact]
    public void KnownDestinations_values_match_api_contract_section4()
    {
        KnownDestinations.ISS.Id.Should().Be("ISS");
        KnownDestinations.ISS.AltitudeKm.Should().Be(408);
        KnownDestinations.ISS.InclinationDeg.Should().Be(51.6);
        KnownDestinations.ISS.DeltaVKmS.Should().Be(9.40);
        KnownDestinations.ISS.MissionDurationHours.Should().Be(6.2);

        KnownDestinations.LeoGeneric.Id.Should().Be("LEO_GENERIC");
        KnownDestinations.LeoGeneric.AltitudeKm.Should().Be(400);
        KnownDestinations.LeoGeneric.InclinationDeg.Should().Be(28.5);
        KnownDestinations.LeoGeneric.DeltaVKmS.Should().Be(9.20);
        KnownDestinations.LeoGeneric.MissionDurationHours.Should().Be(5.8);

        KnownDestinations.Sso.Id.Should().Be("SSO");
        KnownDestinations.Sso.AltitudeKm.Should().Be(500);
        KnownDestinations.Sso.InclinationDeg.Should().Be(97.4);
        KnownDestinations.Sso.DeltaVKmS.Should().Be(10.10);
        KnownDestinations.Sso.MissionDurationHours.Should().Be(7.0);

        KnownDestinations.All.Should().HaveCount(3);
    }

    [Fact]
    public void KnownDestinations_FindById_is_case_insensitive()
    {
        KnownDestinations.FindById("iss").Should().NotBeNull();
        KnownDestinations.FindById("ISS").Should().NotBeNull();
        KnownDestinations.FindById("leo_generic").Should().NotBeNull();
        KnownDestinations.FindById("MARS").Should().BeNull();
    }

    [Fact]
    public void MissionSession_has_sess_prefix_and_30min_ttl()
    {
        var session = new MissionSession
        {
            Destination = "ISS",
            DepartureTime = DateTime.UtcNow,
            ArrivalTime = DateTime.UtcNow.AddHours(6),
            UserId = Guid.NewGuid()
        };
        session.SessionId.Should().StartWith("sess_");
        session.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(5));
        session.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void ApiErrorDto_factory_methods_produce_correct_codes()
    {
        ApiErrorDto.EmailAlreadyExists().Error.Should().Be("EMAIL_ALREADY_EXISTS");
        ApiErrorDto.InvalidCredentials().Error.Should().Be("INVALID_CREDENTIALS");
        ApiErrorDto.TokenExpired().Error.Should().Be("TOKEN_EXPIRED");
        ApiErrorDto.InvalidRefreshToken().Error.Should().Be("INVALID_REFRESH_TOKEN");
        ApiErrorDto.Unauthorized().Error.Should().Be("UNAUTHORIZED");
        ApiErrorDto.Forbidden().Error.Should().Be("FORBIDDEN");
        ApiErrorDto.DebrisNotFound("X").Error.Should().Be("DEBRIS_NOT_FOUND");
        ApiErrorDto.MissionNotFound("X").Error.Should().Be("MISSION_NOT_FOUND");
        ApiErrorDto.SessionNotFound("X").Error.Should().Be("SESSION_NOT_FOUND");
        ApiErrorDto.SessionAlreadyCompleted().Error.Should().Be("SESSION_ALREADY_COMPLETED");
        ApiErrorDto.InvalidDestination("X").Error.Should().Be("INVALID_DESTINATION");
        ApiErrorDto.TimeRangeExceeded().Error.Should().Be("TIME_RANGE_EXCEEDED");
        ApiErrorDto.InvalidTimeRange().Error.Should().Be("INVALID_TIME_RANGE");
        ApiErrorDto.MissingParameter("p").Error.Should().Be("MISSING_PARAMETER");
        ApiErrorDto.InvalidDateFormat("p").Error.Should().Be("INVALID_DATE_FORMAT");
        ApiErrorDto.InvalidPasswordFormat().Error.Should().Be("INVALID_PASSWORD_FORMAT");
        ApiErrorDto.InvalidCurrentPassword().Error.Should().Be("INVALID_CURRENT_PASSWORD");
        ApiErrorDto.CacheNotReady().Error.Should().Be("CACHE_NOT_READY");
        ApiErrorDto.InternalError().Error.Should().Be("INTERNAL_ERROR");
    }

    [Fact]
    public void PaginationDto_computes_total_pages()
    {
        var p = PaginationDto.From(1, 20, 45);
        p.TotalPages.Should().Be(3);
        p.Total.Should().Be(45);
    }

    [Fact]
    public void All_dto_types_instantiate_without_error()
    {
        // Auth
        _ = new RegisterRequest("a@b.com", "Pass1234!", "Name");
        _ = new LoginRequest("a@b.com", "pass");
        _ = new RefreshRequest("tok");
        _ = new LogoutRequest("tok");
        _ = new UserInAuthResponse("usr_1", "e", "n", "Researcher", DateTime.UtcNow.ToString("O"));
        _ = new AuthResponse(new UserInAuthResponse("usr_1", "e", "n", "Researcher", "2025-01-01T00:00:00Z"), "at", "rt", 3600);
        _ = new RefreshTokenResponse("at", 3600);

        // User
        _ = new UserStatsDto(0, 0, 0, 0, 0, 0, 0, null, 0);
        _ = new UserProfileResponse("usr_1", "e", "n", "Researcher", "2025-01-01T00:00:00Z", new UserStatsDto(0,0,0,0,0,0,0,null,0));
        _ = new UpdateUserRequest(null, null, null);

        // Orbital
        _ = new DebrisDto("1","n","debris",0,0,400,7.5,"celestrak","2025-01-01T00:00:00Z");
        _ = new TleDto("ep", "l1", "l2");
        _ = new OrbitParamsDto(74, 0.004, 97, 800, 750);
        _ = new DebrisDetailDto("1","n","debris",0,0,400,7.5,"celestrak","2025-01-01T00:00:00Z", null, null);
        _ = new ByTypeDto(100, 50, 20);
        _ = new ByAltitudeBandDto(80, 60, 40);
        _ = new SourcesDto(200, 0);
        _ = new DebrisStatsDto(200, new ByTypeDto(0,0,0), new ByAltitudeBandDto(0,0,0), new SourcesDto(0,0), "2025-01-01T00:00:00Z");

        // Common
        _ = new ConjunctionDto("1","n",5.0,"2025-01-01T00:00:00Z","high");
        _ = new LaunchWindowDto("s","e",0.01,9.4,6.2,true,[]);
        _ = new LaunchWindowsResponse("ISS","s","e",48,41,[]);
        _ = new BestWindowDto(1,"s","e",0.01,9.4,6.2,[]);
        _ = new BestWindowsResponse("ISS","s","e",[]);

        // Status
        _ = new SourceStatusDto("ok","unavailable");
        _ = new StatusResponse("ready",100,90,null,null,0,new SourceStatusDto("ok","unavailable"));

        // Destination
        _ = new DestinationDto("ISS","ISS",408,51.6,"desc",9.4,6.2,"iss");
        _ = new DestinationsResponse([]);

        // Mission
        _ = new SimulateRequest("ISS", DateTime.UtcNow, DateTime.UtcNow.AddHours(6));
        _ = new ObstacleDto("1","n",5.0,"2025-01-01T00:00:00Z","high");
        _ = new SimulateResponse("ISS",DateTime.UtcNow,DateTime.UtcNow.AddHours(6),[],[],87,0.12,9.4);
        _ = new SessionRequest("ISS","s","e");
        _ = new SessionResponse("sess_1","ISS","s","e","/stream","exp");
        _ = new CompleteSessionRequest("success", false);
        _ = new CompleteSessionResponse("sess_1","success",87,0.12,9.4,2,3600.0,false,null);

        // History
        _ = new MissionSummaryDto("msn_1","ISS","ISS","success",87,0.12,9.4,2,"s","e","c");
        _ = new ScoreBreakdownDto(42,45,87);
        _ = new MissionDetailResponse("msn_1","ISS","ISS","success",87,0.12,9.4,"s","e","c",[],new ScoreBreakdownDto(42,45,87));
        _ = new MissionStatsResponse(12,9,2,1,0.75,97,23,81,112.8,18,"ISS",new Dictionary<string,int>{{"ISS",8}});

        // Dashboard
        _ = new LastMissionDto("ISS","success",87,"2025-01-01T00:00:00Z");
        _ = new UserDashboardDto("Name",12,97,null);
        _ = new OrbitalSummaryDto(1000,new ByTypeDto(0,0,0),new ByAltitudeBandDto(0,0,0),3,"2025-01-01T00:00:00Z");
        _ = new DashboardSummaryResponse(new OrbitalSummaryDto(0,new ByTypeDto(0,0,0),new ByAltitudeBandDto(0,0,0),0,"2025-01-01T00:00:00Z"),null);
        _ = new AlertDto("alrt_1","1","n","ISS",8.2,"2025-01-01T00:00:00Z","critical",238,"2025-01-01T00:00:00Z");
        _ = new AlertsResponse([],6,"2025-01-01T00:00:00Z");
    }

    [Fact]
    public void RegisterRequest_default_role_is_Researcher()
    {
        var req = new RegisterRequest("a@b.com", "Pass1234!", "Name");
        req.Role.Should().Be("Researcher");
    }

    [Fact]
    public void OrbitalObject_optional_tle_fields_are_null_by_default()
    {
        var obj = new OrbitalObject("1","ISS","satellite",0,0,408,7.66,"celestrak",DateTime.UtcNow);
        obj.TleLine1.Should().BeNull();
        obj.InclinationDeg.Should().BeNull();
    }
}
```

### Task 9.1 — Run + commit

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj
dotnet test MissionClear.Tests/MissionClear.Tests.csproj
git add MissionClear.Tests/Models/DtoCompileTests.cs
git commit -m "test(models): compile-check all DTOs, DomainException, KnownDestinations invariants"
```

---

## Definition of Done

- [ ] `MissionClear.Api/Exceptions/DomainException.cs` existe com `ErrorCode` (string) e `HttpStatus` (int)
- [ ] `DomainException` cobre todos os 19 error codes de `API_CONTRACT.md` section 14
- [ ] `Models/OrbitalObject.cs` — record com campos TLE/orbit opcionais
- [ ] `Models/MissionDestination.cs` + `KnownDestinations` — ISS (408km, 51.6°, Δv 9.40, 6.2h), LEO_GENERIC (400km, 28.5°, Δv 9.20, 5.8h), SSO (500km, 97.4°, Δv 10.10, 7.0h)
- [ ] `Models/ConjunctionResult.cs`, `Models/LaunchWindow.cs`, `Models/MissionSession.cs`
- [ ] `MissionSession.IsExpired` property e TTL padrão de 30 minutos
- [ ] `Dtos/Auth/`: RegisterRequest (Role default "Researcher"), LoginRequest, RefreshRequest, LogoutRequest, AuthResponse, UserInAuthResponse (com `Role`), RefreshTokenResponse
- [ ] `Dtos/User/`: UserProfileResponse (com `Role`), UserStatsDto, UpdateUserRequest
- [ ] `Dtos/Orbital/`: DebrisDto, DebrisDetailDto (TleDto + OrbitParamsDto), DebrisStatsDto (ByTypeDto, ByAltitudeBandDto com [JsonPropertyName] explícito, SourcesDto)
- [ ] `Dtos/Mission/`: SimulateRequest, SimulateResponse (ObstacleDto), SessionRequest, SessionResponse, CompleteSessionRequest, CompleteSessionResponse
- [ ] `Dtos/History/`: MissionSummaryDto, MissionDetailResponse (ScoreBreakdownDto), MissionStatsResponse
- [ ] `Dtos/Dashboard/`: DashboardSummaryResponse (OrbitalSummaryDto, UserDashboardDto, LastMissionDto), AlertsResponse (AlertDto)
- [ ] `Dtos/Common/`: ApiErrorDto (19 factory methods), PaginationDto, PagedResponse\<T\>, ConjunctionDto, LaunchWindowDto, LaunchWindowsResponse (total_windows + safe_windows), BestWindowDto, BestWindowsResponse
- [ ] `Dtos/Status/`: StatusResponse, SourceStatusDto
- [ ] `Dtos/Destination/`: DestinationDto, DestinationsResponse
- [ ] `Program.cs` configura `JsonNamingPolicy.SnakeCaseLower` + `WhenWritingNull`
- [ ] `dotnet build` limpo em `MissionClear.Api` e `MissionClear.Tests`
- [ ] `dotnet test` passa `DtoCompileTests` (todos os facts verdes)

## Handoff para os próximos planos

| Plano | Tipos consumidos |
|---|---|
| **phase-03-orbital** | `OrbitalObject`, `MissionDestination`, `KnownDestinations`, `LaunchWindow`, `ConjunctionResult`, `DomainException` |
| **phase-04-auth** | `RegisterRequest`, `LoginRequest`, `AuthResponse`, `UserInAuthResponse`, `DomainException`, `ApiErrorDto.*` |
| **phase-05-simulation** | `MissionSession`, `SessionRequest`, `SessionResponse`, `CompleteSessionRequest`, `CompleteSessionResponse`, `SimulateRequest`, `SimulateResponse` |
| **phase-06-history-dashboard** | `MissionSummaryDto`, `MissionDetailResponse`, `MissionStatsResponse`, `DashboardSummaryResponse`, `AlertsResponse` |
| **phase-07-api-controllers** | Todos os DTOs + `ApiErrorDto` factory methods + `DomainException` (via middleware) |
| **phase-08-mvc-web** | `AuthResponse`, `UserInAuthResponse`, `UserProfileResponse` |
