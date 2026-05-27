# Plan 02 — Domain Models & API DTOs

**Execution order:** After plan-00. Parallel with plan-01.
**Estimated time:** 30 minutes.
**Goal:** Definir todos os records de domínio (LEO objects, conjunções, janelas, sessões, destinos) e todos os DTOs JSON snake_case do contrato de API — base imutável usada por todos os outros planos.
**Dependencies:** plan-00-scaffolding.md complete (projeto `MissionClear.Api` criado, `MissionClear.Tests` referenciando o Api).
**Unlocks:** plan-03-orbital, plan-04-auth, plan-05-mission, plan-06-history-dashboard, plan-07-controllers — todos consomem estes tipos.

---

## Diretrizes globais (válidas para todos os arquivos deste plano)

- `<Nullable>enable</Nullable>` no `.csproj` (já feito em plan-00). Toda string default = `string.Empty`, nunca `null`.
- Todos os DTOs são `record` (imutáveis), com `init`-only properties.
- Toda propriedade JSON tem `[JsonPropertyName("snake_case")]` — namespace `System.Text.Json.Serialization`.
- Records de domínio (não-DTO) podem usar PascalCase puro, sem atributos JSON.
- Nenhum arquivo passa de 200 linhas. Um tipo por arquivo (exceto enums pequenos colocados junto do record que os usa).
- Após cada Phase: `dotnet build` deve compilar limpo. Ao final do plano: `dotnet test` roda os testes de compile-check.

---

## Phase 1 — Domain Models (records puros, sem JSON)

### Task 1.1 — `Models/Domain/OrbitalObject.cs`

Criar arquivo `MissionClear.Api/Models/Domain/OrbitalObject.cs`:

```csharp
namespace MissionClear.Api.Models.Domain;

/// <summary>
/// Objeto orbital propagado (debris, satélite ou rocket body) em um instante.
/// Imutável. Produzido pelo OrbitalEngine, consumido pelo ConjunctionDetector e pelos controllers.
/// </summary>
public sealed record OrbitalObject(
    string NoradCatId,
    string Name,
    string Type,
    double LatitudeDeg,
    double LongitudeDeg,
    double AltitudeKm,
    double VelocityKmS,
    string Source,
    DateTime PropagatedAt);
```

### Task 1.2 — `Models/Domain/MissionDestination.cs` + `KnownDestinations`

Criar `MissionClear.Api/Models/Domain/MissionDestination.cs`:

```csharp
namespace MissionClear.Api.Models.Domain;

/// <summary>
/// Destino orbital pré-definido. AltitudeKm e InclinationDeg alimentam o LaunchWindowCalculator.
/// </summary>
public sealed record MissionDestination(
    string Id,
    string DisplayName,
    double AltitudeKm,
    double InclinationDeg,
    string Description,
    double DeltaVKmS,
    double MissionDurationHours,
    string Icon);

public static class KnownDestinations
{
    public static readonly MissionDestination Iss = new(
        Id: "ISS",
        DisplayName: "International Space Station",
        AltitudeKm: 408,
        InclinationDeg: 51.6,
        Description: "Estação Espacial Internacional — órbita inclinada de 51.6 graus.",
        DeltaVKmS: 9.4,
        MissionDurationHours: 6.2,
        Icon: "iss");

    public static readonly MissionDestination LeoGeneric = new(
        Id: "LEO_GENERIC",
        DisplayName: "Generic Low Earth Orbit",
        AltitudeKm: 400,
        InclinationDeg: 28.5,
        Description: "Órbita baixa equatorial genérica para satélites de uso geral.",
        DeltaVKmS: 9.1,
        MissionDurationHours: 5.5,
        Icon: "leo");

    public static readonly MissionDestination Sso = new(
        Id: "SSO",
        DisplayName: "Sun-Synchronous Orbit",
        AltitudeKm: 500,
        InclinationDeg: 97.4,
        Description: "Órbita sol-síncrona para sensoriamento remoto e meteorologia.",
        DeltaVKmS: 9.8,
        MissionDurationHours: 7.1,
        Icon: "sso");

    public static IReadOnlyList<MissionDestination> AllDestinations { get; } =
        new[] { Iss, LeoGeneric, Sso };

    public static IReadOnlyList<string> ValidIds { get; } =
        AllDestinations.Select(d => d.Id).ToArray();

    public static bool TryGet(string? id, out MissionDestination destination)
    {
        destination = Iss;
        if (string.IsNullOrWhiteSpace(id)) return false;

        foreach (var d in AllDestinations)
        {
            if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                destination = d;
                return true;
            }
        }
        return false;
    }
}
```

### Task 1.3 — `Models/Domain/ConjunctionResult.cs` + `RiskLevel`

Criar `MissionClear.Api/Models/Domain/ConjunctionResult.cs`:

```csharp
namespace MissionClear.Api.Models.Domain;

/// <summary>
/// Aproximação detectada entre um objeto orbital e uma trajetória de missão.
/// </summary>
public sealed record ConjunctionResult(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    DateTime TimeOfClosestApproachUtc,
    RiskLevel Risk);

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public static class RiskLevelClassifier
{
    /// <summary>
    /// Critical &lt; 1 km, High &lt; 5 km, Medium &lt; 10 km, Low caso contrário.
    /// </summary>
    public static RiskLevel Classify(double closestApproachKm)
    {
        if (closestApproachKm < 1.0) return RiskLevel.Critical;
        if (closestApproachKm < 5.0) return RiskLevel.High;
        if (closestApproachKm < 10.0) return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    public static string ToWireString(this RiskLevel level) => level switch
    {
        RiskLevel.Critical => "critical",
        RiskLevel.High => "high",
        RiskLevel.Medium => "medium",
        _ => "low"
    };
}
```

### Task 1.4 — `Models/Domain/LaunchWindow.cs`

Criar `MissionClear.Api/Models/Domain/LaunchWindow.cs`:

```csharp
namespace MissionClear.Api.Models.Domain;

/// <summary>
/// Janela temporal de lançamento avaliada. RiskScore in [0,1]; menor é melhor.
/// </summary>
public sealed record LaunchWindow(
    DateTime StartUtc,
    DateTime EndUtc,
    double RiskScore,
    double DeltaVKmS,
    double DurationHours,
    bool IsRecommended,
    IReadOnlyList<ConjunctionResult> Conjunctions);
```

### Task 1.5 — `Models/Domain/MissionSession.cs` + `SessionStatus`

Criar `MissionClear.Api/Models/Domain/MissionSession.cs`:

```csharp
namespace MissionClear.Api.Models.Domain;

public enum SessionStatus
{
    Active,
    Success,
    Failure,
    Aborted,
    Expired
}

/// <summary>
/// Sessão de simulação ao vivo (SSE). Mutável: Status muda quando a simulação termina.
/// Mantida em memória pelo SessionStore. Não persiste a menos que o usuário peça save_to_history.
/// </summary>
public sealed class MissionSession
{
    public required string Id { get; init; }
    public string? UserId { get; init; }
    public required string DestinationId { get; init; }
    public required DateTime DepartureTimeUtc { get; init; }
    public required DateTime ArrivalTimeUtc { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }

    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public double? FinalMissionScore { get; set; }
    public double? FinalRiskScore { get; set; }
    public double? FinalDeltaVKmS { get; set; }
    public int ObstaclesEncountered { get; set; }
    public double DurationSeconds { get; set; }

    public static string NewId() => $"sess_{Guid.NewGuid():N}";
}

public static class SessionStatusExtensions
{
    public static string ToWireString(this SessionStatus status) => status switch
    {
        SessionStatus.Active => "active",
        SessionStatus.Success => "success",
        SessionStatus.Failure => "failure",
        SessionStatus.Aborted => "aborted",
        SessionStatus.Expired => "expired",
        _ => "active"
    };

    public static bool TryParse(string? raw, out SessionStatus status)
    {
        switch (raw?.ToLowerInvariant())
        {
            case "success": status = SessionStatus.Success; return true;
            case "failure": status = SessionStatus.Failure; return true;
            case "aborted": status = SessionStatus.Aborted; return true;
            default: status = SessionStatus.Active; return false;
        }
    }
}
```

### Task 1.6 — `Models/Tle/TleRecord.cs`

Criar `MissionClear.Api/Models/Tle/TleRecord.cs`:

```csharp
namespace MissionClear.Api.Models.Tle;

/// <summary>
/// Raw TLE record as fetched from CelesTrak or KeepTrack. Immutable.
/// </summary>
public sealed record TleRecord(
    string NoradCatId,
    string Name,
    string Line1,
    string Line2,
    string Source,
    DateTime FetchedAt);

/// <summary>
/// Raw JSON shape returned by CelesTrak's GP endpoint.
/// </summary>
public sealed class CelesTrakGpRecord
{
    public int NORAD_CAT_ID { get; set; }
    public string? OBJECT_NAME { get; set; }
    public string? OBJECT_TYPE { get; set; }
    public string? TLE_LINE1 { get; set; }
    public string? TLE_LINE2 { get; set; }
    public string? EPOCH { get; set; }
    public double INCLINATION { get; set; }
    public double ECCENTRICITY { get; set; }
    public double APOAPSIS { get; set; }
    public double PERIAPSIS { get; set; }
    public double PERIOD { get; set; }
}
```

### Task 1.7 — Build check + commit

```bash
dotnet build MissionClear.Api/MissionClear.Api.csproj
git add MissionClear.Api/Models/Domain MissionClear.Api/Models/Tle
git commit -m "feat(models): domain records (OrbitalObject, MissionDestination, ConjunctionResult, LaunchWindow, MissionSession) + TleRecord"
```

---

## Phase 2 — API DTOs: Common, Orbital, Mission

### Task 2.1 — `Models/Dtos/Common/ApiErrorDto.cs`

Criar `MissionClear.Api/Models/Dtos/Common/ApiErrorDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Dtos.Common;

/// <summary>
/// Envelope padronizado de erro. Toda resposta de erro do contrato usa este shape.
/// </summary>
public sealed record ApiErrorDto
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiErrorDto Make(string code, string message) =>
        new() { Error = code, Message = message, Timestamp = DateTime.UtcNow };

    public static ApiErrorDto InvalidDestination(string id) =>
        Make("invalid_destination", $"Destino '{id}' não é suportado. Use ISS, LEO_GENERIC ou SSO.");

    public static ApiErrorDto CacheNotReady() =>
        Make("cache_not_ready", "Cache orbital ainda está aquecendo. Tente novamente em alguns segundos.");

    public static ApiErrorDto InvalidCredentials() =>
        Make("invalid_credentials", "Email ou senha incorretos.");

    public static ApiErrorDto EmailExists() =>
        Make("email_exists", "Este email já está cadastrado.");

    public static ApiErrorDto TokenExpired() =>
        Make("token_expired", "Token de acesso expirado. Use o refresh token.");

    public static ApiErrorDto InvalidRefreshToken() =>
        Make("invalid_refresh_token", "Refresh token inválido ou revogado.");

    public static ApiErrorDto Forbidden() =>
        Make("forbidden", "Você não tem permissão para acessar este recurso.");

    public static ApiErrorDto NotFound(string what) =>
        Make("not_found", $"{what} não encontrado.");

    public static ApiErrorDto InternalError() =>
        Make("internal_error", "Erro interno do servidor. Tente novamente.");

    public static ApiErrorDto SessionCompleted() =>
        Make("session_completed", "Esta sessão já foi finalizada.");

    public static ApiErrorDto InvalidPasswordFormat() =>
        Make("invalid_password_format", "Senha deve ter no mínimo 8 caracteres, incluindo letras e números.");

    public static ApiErrorDto InvalidCurrentPassword() =>
        Make("invalid_current_password", "Senha atual incorreta.");

    public static ApiErrorDto TimeRangeExceeded() =>
        Make("time_range_exceeded", "Intervalo de tempo solicitado excede o limite máximo permitido.");

    public static ApiErrorDto InvalidTimeRange() =>
        Make("invalid_time_range", "Intervalo de tempo inválido: 'from' deve ser anterior a 'to'.");
}
```

### Task 2.2 — `Models/Dtos/Common/PaginationDto.cs`

Criar `MissionClear.Api/Models/Dtos/Common/PaginationDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Dtos.Common;

public sealed record PaginationDto
{
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("limit")] public int Limit { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; init; }

    public static PaginationDto From(int page, int limit, int total)
    {
        var safeLimit = Math.Max(1, limit);
        var pages = (int)Math.Ceiling(total / (double)safeLimit);
        return new PaginationDto { Page = page, Limit = safeLimit, Total = total, TotalPages = pages };
    }
}

public sealed record PagedResponse<T>
{
    [JsonPropertyName("data")] public IReadOnlyList<T> Data { get; init; } = Array.Empty<T>();
    [JsonPropertyName("pagination")] public PaginationDto Pagination { get; init; } = PaginationDto.From(1, 20, 0);
}
```

### Task 2.3 — `Models/Dtos/Common/StatusResponse.cs`

```csharp
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Dtos.Common;

public sealed record SourceStatusDto
{
    [JsonPropertyName("celestrak")] public string Celestrak { get; init; } = "unknown";
    [JsonPropertyName("keeptrack")] public string Keeptrack { get; init; } = "unknown";
}

public sealed record StatusResponse
{
    [JsonPropertyName("status")] public string Status { get; init; } = "loading";
    [JsonPropertyName("tle_count")] public int TleCount { get; init; }
    [JsonPropertyName("propagated_count")] public int PropagatedCount { get; init; }
    [JsonPropertyName("last_tle_fetch")] public DateTime? LastTleFetch { get; init; }
    [JsonPropertyName("last_propagation")] public DateTime? LastPropagation { get; init; }
    [JsonPropertyName("uptime_seconds")] public long UptimeSeconds { get; init; }
    [JsonPropertyName("sources")] public SourceStatusDto Sources { get; init; } = new();
}
```

### Task 2.4 — `Models/Dtos/Orbital/DebrisDto.cs`

```csharp
using System.Text.Json.Serialization;
using MissionClear.Api.Models.Domain;

namespace MissionClear.Api.Models.Dtos.Orbital;

public sealed record DebrisDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; init; } = "debris";
    [JsonPropertyName("latitude")] public double Latitude { get; init; }
    [JsonPropertyName("longitude")] public double Longitude { get; init; }
    [JsonPropertyName("altitude_km")] public double AltitudeKm { get; init; }
    [JsonPropertyName("velocity_km_s")] public double VelocityKmS { get; init; }
    [JsonPropertyName("source")] public string Source { get; init; } = "celestrak";
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; init; }

    public static DebrisDto From(OrbitalObject obj) => new()
    {
        Id = obj.NoradCatId,
        Name = obj.Name,
        Type = obj.Type,
        Latitude = Math.Round(obj.LatitudeDeg, 4),
        Longitude = Math.Round(obj.LongitudeDeg, 4),
        AltitudeKm = Math.Round(obj.AltitudeKm, 2),
        VelocityKmS = obj.VelocityKmS,
        Source = obj.Source,
        UpdatedAt = obj.PropagatedAt
    };
}
```

### Task 2.5 — `Models/Dtos/Orbital/DestinationDto.cs`

```csharp
using System.Text.Json.Serialization;
using MissionClear.Api.Models.Domain;

namespace MissionClear.Api.Models.Dtos.Orbital;

public sealed record DestinationDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("altitude_km")] public double AltitudeKm { get; init; }
    [JsonPropertyName("inclination_deg")] public double InclinationDeg { get; init; }
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("delta_v_km_s")] public double DeltaVKmS { get; init; }
    [JsonPropertyName("mission_duration_hours")] public double MissionDurationHours { get; init; }
    [JsonPropertyName("icon")] public string Icon { get; init; } = string.Empty;

    public static DestinationDto From(MissionDestination d) => new()
    {
        Id = d.Id,
        DisplayName = d.DisplayName,
        AltitudeKm = d.AltitudeKm,
        InclinationDeg = d.InclinationDeg,
        Description = d.Description,
        DeltaVKmS = d.DeltaVKmS,
        MissionDurationHours = d.MissionDurationHours,
        Icon = d.Icon
    };
}
```

### Task 2.6 — `Models/Dtos/Orbital/ConjunctionDto.cs`

```csharp
using System.Text.Json.Serialization;
using MissionClear.Api.Models.Domain;

namespace MissionClear.Api.Models.Dtos.Orbital;

public sealed record ConjunctionDto
{
    [JsonPropertyName("debris_id")] public string DebrisId { get; init; } = string.Empty;
    [JsonPropertyName("debris_name")] public string DebrisName { get; init; } = string.Empty;
    [JsonPropertyName("closest_approach_km")] public double ClosestApproachKm { get; init; }
    [JsonPropertyName("time_of_closest_approach")] public DateTime TimeOfClosestApproach { get; init; }
    [JsonPropertyName("risk_level")] public string RiskLevel { get; init; } = "low";

    public static ConjunctionDto From(ConjunctionResult c) => new()
    {
        DebrisId = c.DebrisId,
        DebrisName = c.DebrisName,
        ClosestApproachKm = c.ClosestApproachKm,
        TimeOfClosestApproach = c.TimeOfClosestApproachUtc,
        RiskLevel = c.Risk.ToWireString()
    };
}
```

### Task 2.7 — `Models/Dtos/Orbital/LaunchWindowDto.cs`

```csharp
using System.Text.Json.Serialization;
using MissionClear.Api.Models.Domain;

namespace MissionClear.Api.Models.Dtos.Orbital;

public sealed record LaunchWindowDto
{
    [JsonPropertyName("start")] public DateTime Start { get; init; }
    [JsonPropertyName("end")] public DateTime End { get; init; }
    [JsonPropertyName("risk_score")] public double RiskScore { get; init; }
    [JsonPropertyName("delta_v_km_s")] public double DeltaVKmS { get; init; }
    [JsonPropertyName("duration_hours")] public double DurationHours { get; init; }
    [JsonPropertyName("is_recommended")] public bool IsRecommended { get; init; }
    [JsonPropertyName("conjunctions")] public IReadOnlyList<ConjunctionDto> Conjunctions { get; init; } = Array.Empty<ConjunctionDto>();

    public static LaunchWindowDto From(LaunchWindow w) => new()
    {
        Start = w.StartUtc,
        End = w.EndUtc,
        RiskScore = w.RiskScore,
        DeltaVKmS = w.DeltaVKmS,
        DurationHours = w.DurationHours,
        IsRecommended = w.IsRecommended,
        Conjunctions = w.Conjunctions.Select(ConjunctionDto.From).ToArray()
    };
}

public sealed record LaunchWindowsResponse
{
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    [JsonPropertyName("from")] public DateTime From { get; init; }
    [JsonPropertyName("to")] public DateTime To { get; init; }
    [JsonPropertyName("windows")] public IReadOnlyList<LaunchWindowDto> Windows { get; init; } = Array.Empty<LaunchWindowDto>();
}
```

### Task 2.8 — `Models/Dtos/Mission/MissionSimulateDto.cs`

```csharp
using System.Text.Json.Serialization;
using MissionClear.Api.Models.Dtos.Orbital;

namespace MissionClear.Api.Models.Dtos.Mission;

public sealed record MissionSimulateRequest
{
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    [JsonPropertyName("departure_time")] public DateTime DepartureTime { get; init; }
    [JsonPropertyName("arrival_time")] public DateTime ArrivalTime { get; init; }
}

public sealed record TrajectoryPointDto
{
    [JsonPropertyName("t")] public DateTime TimestampUtc { get; init; }
    [JsonPropertyName("latitude")] public double Latitude { get; init; }
    [JsonPropertyName("longitude")] public double Longitude { get; init; }
    [JsonPropertyName("altitude_km")] public double AltitudeKm { get; init; }
}

public sealed record MissionSimulateResponse
{
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    [JsonPropertyName("departure_time")] public DateTime DepartureTime { get; init; }
    [JsonPropertyName("arrival_time")] public DateTime ArrivalTime { get; init; }
    [JsonPropertyName("trajectory")] public IReadOnlyList<TrajectoryPointDto> Trajectory { get; init; } = Array.Empty<TrajectoryPointDto>();
    [JsonPropertyName("obstacles")] public IReadOnlyList<ConjunctionDto> Obstacles { get; init; } = Array.Empty<ConjunctionDto>();
    [JsonPropertyName("mission_score")] public int MissionScore { get; init; }
    [JsonPropertyName("risk_score")] public double RiskScore { get; init; }
    [JsonPropertyName("delta_v_km_s")] public double DeltaVKmS { get; init; }
}
```

### Task 2.9 — Build + commit

```bash
dotnet build MissionClear.Api/MissionClear.Api.csproj
git add MissionClear.Api/Models/Dtos/Common MissionClear.Api/Models/Dtos/Orbital MissionClear.Api/Models/Dtos/Mission
git commit -m "feat(dtos): common envelopes, orbital DTOs, MissionSimulate request/response"
```

---

## Phase 3 — Session, Auth, User DTOs

### Task 3.1 — `Models/Dtos/Mission/SessionDtos.cs`

```csharp
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Dtos.Mission;

public sealed record SessionRequest
{
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    [JsonPropertyName("departure_time")] public DateTime DepartureTime { get; init; }
    [JsonPropertyName("arrival_time")] public DateTime ArrivalTime { get; init; }
}

public sealed record SessionResponse
{
    [JsonPropertyName("session_id")] public string SessionId { get; init; } = string.Empty;
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    [JsonPropertyName("departure_time")] public DateTime DepartureTime { get; init; }
    [JsonPropertyName("arrival_time")] public DateTime ArrivalTime { get; init; }
    [JsonPropertyName("stream_url")] public string StreamUrl { get; init; } = string.Empty;
    [JsonPropertyName("expires_at")] public DateTime ExpiresAt { get; init; }
}

public sealed record SessionCompleteRequest
{
    [JsonPropertyName("status")] public string Status { get; init; } = "success";
    [JsonPropertyName("save_to_history")] public bool SaveToHistory { get; init; } = true;
}

public sealed record SessionCompleteResponse
{
    [JsonPropertyName("session_id")] public string SessionId { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = "success";
    [JsonPropertyName("mission_score")] public int MissionScore { get; init; }
    [JsonPropertyName("risk_score")] public double RiskScore { get; init; }
    [JsonPropertyName("delta_v_km_s")] public double DeltaVKmS { get; init; }
    [JsonPropertyName("obstacles_encountered")] public int ObstaclesEncountered { get; init; }
    [JsonPropertyName("duration_seconds")] public double DurationSeconds { get; init; }
    [JsonPropertyName("saved_to_history")] public bool SavedToHistory { get; init; }
    [JsonPropertyName("mission_id")] public string? MissionId { get; init; }
}
```

### Task 3.2 — `Models/Dtos/Auth/AuthDtos.cs`

```csharp
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Dtos.Auth;

public sealed record AuthRegisterRequest
{
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; init; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = string.Empty;
}

public sealed record AuthLoginRequest
{
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("password")] public string Password { get; init; } = string.Empty;
}

public sealed record AuthRefreshRequest
{
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; init; } = string.Empty;
}

public sealed record AuthUserSummaryDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("total_missions")] public int TotalMissions { get; init; }
    [JsonPropertyName("best_score")] public int BestScore { get; init; }
}

public sealed record AuthResponse
{
    [JsonPropertyName("user")] public AuthUserSummaryDto User { get; init; } = new();
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; init; } = string.Empty;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
}

public sealed record ChangePasswordRequest
{
    [JsonPropertyName("current_password")] public string CurrentPassword { get; init; } = string.Empty;
    [JsonPropertyName("new_password")] public string NewPassword { get; init; } = string.Empty;
}
```

### Task 3.3 — `Models/Dtos/User/UserDtos.cs`

```csharp
using System.Text.Json.Serialization;

namespace MissionClear.Api.Models.Dtos.User;

public sealed record UserStatsDto
{
    [JsonPropertyName("total_missions")] public int TotalMissions { get; init; }
    [JsonPropertyName("successful_missions")] public int SuccessfulMissions { get; init; }
    [JsonPropertyName("failed_missions")] public int FailedMissions { get; init; }
    [JsonPropertyName("aborted_missions")] public int AbortedMissions { get; init; }
    [JsonPropertyName("success_rate")] public double SuccessRate { get; init; }
    [JsonPropertyName("best_score")] public int BestScore { get; init; }
    [JsonPropertyName("average_score")] public double AverageScore { get; init; }
    [JsonPropertyName("favorite_destination")] public string? FavoriteDestination { get; init; }
    [JsonPropertyName("total_delta_v_km_s")] public double TotalDeltaVKmS { get; init; }
}

public sealed record UserResponse
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("stats")] public UserStatsDto Stats { get; init; } = new();
}

public sealed record UpdateUserRequest
{
    [JsonPropertyName("display_name")] public string DisplayName { get; init; } = string.Empty;
}
```

### Task 3.4 — Build + commit

```bash
dotnet build MissionClear.Api/MissionClear.Api.csproj
git add MissionClear.Api/Models/Dtos/Mission MissionClear.Api/Models/Dtos/Auth MissionClear.Api/Models/Dtos/User
git commit -m "feat(dtos): session lifecycle, auth (register/login/refresh/change-password), user response + stats"
```

---

## Phase 4 — History + Dashboard DTOs

### Task 4.1 — `Models/Dtos/History/MissionHistoryDtos.cs`

```csharp
using System.Text.Json.Serialization;
using MissionClear.Api.Models.Dtos.Orbital;

namespace MissionClear.Api.Models.Dtos.History;

public sealed record MissionHistoryDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    [JsonPropertyName("destination_display")] public string DestinationDisplay { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = "success";
    [JsonPropertyName("mission_score")] public int MissionScore { get; init; }
    [JsonPropertyName("risk_score")] public double RiskScore { get; init; }
    [JsonPropertyName("delta_v_km_s")] public double DeltaVKmS { get; init; }
    [JsonPropertyName("obstacles_encountered")] public int ObstaclesEncountered { get; init; }
    [JsonPropertyName("departure_time")] public DateTime DepartureTime { get; init; }
    [JsonPropertyName("arrival_time")] public DateTime ArrivalTime { get; init; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
}

public sealed record ScoreBreakdownDto
{
    [JsonPropertyName("efficiency_score")] public int EfficiencyScore { get; init; }
    [JsonPropertyName("safety_score")] public int SafetyScore { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
}

public sealed record MissionDetailDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("destination")] public string Destination { get; init; } = string.Empty;
    [JsonPropertyName("destination_display")] public string DestinationDisplay { get; init; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; init; } = "success";
    [JsonPropertyName("mission_score")] public int MissionScore { get; init; }
    [JsonPropertyName("risk_score")] public double RiskScore { get; init; }
    [JsonPropertyName("delta_v_km_s")] public double DeltaVKmS { get; init; }
    [JsonPropertyName("obstacles_encountered")] public int ObstaclesEncountered { get; init; }
    [JsonPropertyName("departure_time")] public DateTime DepartureTime { get; init; }
    [JsonPropertyName("arrival_time")] public DateTime ArrivalTime { get; init; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; init; }
    [JsonPropertyName("obstacles")] public IReadOnlyList<ConjunctionDto> Obstacles { get; init; } = Array.Empty<ConjunctionDto>();
    [JsonPropertyName("score_breakdown")] public ScoreBreakdownDto ScoreBreakdown { get; init; } = new();
}

public sealed record MissionsByDestinationDto
{
    [JsonPropertyName("ISS")] public int Iss { get; init; }
    [JsonPropertyName("LEO_GENERIC")] public int LeoGeneric { get; init; }
    [JsonPropertyName("SSO")] public int Sso { get; init; }
}

public sealed record MissionStatsDto
{
    [JsonPropertyName("total_missions")] public int TotalMissions { get; init; }
    [JsonPropertyName("successful_missions")] public int SuccessfulMissions { get; init; }
    [JsonPropertyName("failed_missions")] public int FailedMissions { get; init; }
    [JsonPropertyName("aborted_missions")] public int AbortedMissions { get; init; }
    [JsonPropertyName("success_rate")] public double SuccessRate { get; init; }
    [JsonPropertyName("best_score")] public int BestScore { get; init; }
    [JsonPropertyName("worst_score")] public int WorstScore { get; init; }
    [JsonPropertyName("average_score")] public double AverageScore { get; init; }
    [JsonPropertyName("total_delta_v_km_s")] public double TotalDeltaVKmS { get; init; }
    [JsonPropertyName("total_obstacles_encountered")] public int TotalObstaclesEncountered { get; init; }
    [JsonPropertyName("favorite_destination")] public string? FavoriteDestination { get; init; }
    [JsonPropertyName("missions_by_destination")] public MissionsByDestinationDto MissionsByDestination { get; init; } = new();
}
```

### Task 4.2 — `Models/Dtos/Dashboard/DashboardDtos.cs`

```csharp
using System.Text.Json.Serialization;
using MissionClear.Api.Models.Dtos.User;

namespace MissionClear.Api.Models.Dtos.Dashboard;

public sealed record ByTypeDto
{
    [JsonPropertyName("debris")] public int Debris { get; init; }
    [JsonPropertyName("satellite")] public int Satellite { get; init; }
    [JsonPropertyName("rocket_body")] public int RocketBody { get; init; }
}

public sealed record ByAltitudeBandDto
{
    [JsonPropertyName("low_200_500km")] public int Low200To500Km { get; init; }
    [JsonPropertyName("mid_500_1000km")] public int Mid500To1000Km { get; init; }
    [JsonPropertyName("high_1000_2000km")] public int High1000To2000Km { get; init; }
}

public sealed record OrbitalSummaryDto
{
    [JsonPropertyName("total_tracked_objects")] public int TotalTrackedObjects { get; init; }
    [JsonPropertyName("by_type")] public ByTypeDto ByType { get; init; } = new();
    [JsonPropertyName("by_altitude_band")] public ByAltitudeBandDto ByAltitudeBand { get; init; } = new();
    [JsonPropertyName("active_conjunction_alerts")] public int ActiveConjunctionAlerts { get; init; }
    [JsonPropertyName("last_updated")] public DateTime LastUpdated { get; init; }
}

public sealed record DashboardSummaryResponse
{
    [JsonPropertyName("orbital")] public OrbitalSummaryDto Orbital { get; init; } = new();
    [JsonPropertyName("user")] public UserResponse? User { get; init; }
}

public sealed record DashboardAlertDto
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("debris_id")] public string DebrisId { get; init; } = string.Empty;
    [JsonPropertyName("debris_name")] public string DebrisName { get; init; } = string.Empty;
    [JsonPropertyName("affected_destination")] public string AffectedDestination { get; init; } = string.Empty;
    [JsonPropertyName("closest_approach_km")] public double ClosestApproachKm { get; init; }
    [JsonPropertyName("time_of_closest_approach")] public DateTime TimeOfClosestApproach { get; init; }
    [JsonPropertyName("risk_level")] public string RiskLevel { get; init; } = "low";
    [JsonPropertyName("minutes_until_conjunction")] public double MinutesUntilConjunction { get; init; }
    [JsonPropertyName("detected_at")] public DateTime DetectedAt { get; init; }
}

public sealed record DashboardAlertsResponse
{
    [JsonPropertyName("alerts")] public IReadOnlyList<DashboardAlertDto> Alerts { get; init; } = Array.Empty<DashboardAlertDto>();
    [JsonPropertyName("window_hours")] public int WindowHours { get; init; }
    [JsonPropertyName("generated_at")] public DateTime GeneratedAt { get; init; }
}
```

### Task 4.3 — Build + commit

```bash
git add MissionClear.Api/Models/Dtos/History MissionClear.Api/Models/Dtos/Dashboard
git commit -m "feat(dtos): mission history (list/detail/stats) and dashboard (summary/alerts)"
```

---

## Phase 5 — Compile-check tests

### Task 5.1 — `MissionClear.Tests/Models/DtoSerializationTests.cs`

```csharp
using System.Text.Json;
using FluentAssertions;
using MissionClear.Api.Models.Domain;
using MissionClear.Api.Models.Dtos.Auth;
using MissionClear.Api.Models.Dtos.Common;
using MissionClear.Api.Models.Dtos.Dashboard;
using MissionClear.Api.Models.Dtos.History;
using MissionClear.Api.Models.Dtos.Mission;
using MissionClear.Api.Models.Dtos.Orbital;
using MissionClear.Api.Models.Dtos.User;
using Xunit;

namespace MissionClear.Tests.Models;

public class DtoSerializationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DebrisDto_serializes_with_snake_case_fields()
    {
        var dto = new DebrisDto
        {
            Id = "25544", Name = "ISS (ZARYA)", Type = "satellite",
            Latitude = -23.5, Longitude = -46.6, AltitudeKm = 408.5,
            VelocityKmS = 7.66, Source = "celestrak", UpdatedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(dto, Json);

        json.Should().Contain("\"altitude_km\"");
        json.Should().Contain("\"velocity_km_s\"");
        json.Should().Contain("\"updated_at\"");
    }

    [Fact]
    public void DebrisDto_From_OrbitalObject_maps_all_fields()
    {
        var obj = new OrbitalObject("1", "Test", "rocket_body", 10, 20, 500, 7.5, "celestrak", DateTime.UtcNow);
        var dto = DebrisDto.From(obj);

        dto.Id.Should().Be("1");
        dto.Type.Should().Be("rocket_body");
        dto.AltitudeKm.Should().Be(500);
    }

    [Fact]
    public void RiskLevelClassifier_classifies_distance_thresholds()
    {
        RiskLevelClassifier.Classify(0.5).Should().Be(RiskLevel.Critical);
        RiskLevelClassifier.Classify(3).Should().Be(RiskLevel.High);
        RiskLevelClassifier.Classify(7).Should().Be(RiskLevel.Medium);
        RiskLevelClassifier.Classify(50).Should().Be(RiskLevel.Low);
    }

    [Fact]
    public void KnownDestinations_TryGet_is_case_insensitive()
    {
        KnownDestinations.TryGet("iss", out var d).Should().BeTrue();
        d.AltitudeKm.Should().Be(408);
        d.InclinationDeg.Should().Be(51.6);

        KnownDestinations.TryGet("sso", out var sso).Should().BeTrue();
        sso.InclinationDeg.Should().Be(97.4);

        KnownDestinations.TryGet("MARS", out _).Should().BeFalse();
        KnownDestinations.ValidIds.Should().BeEquivalentTo(new[] { "ISS", "LEO_GENERIC", "SSO" });
    }

    [Fact]
    public void ApiError_factory_methods_produce_correct_codes()
    {
        ApiErrorDto.InvalidDestination("X").Error.Should().Be("invalid_destination");
        ApiErrorDto.CacheNotReady().Error.Should().Be("cache_not_ready");
        ApiErrorDto.InvalidCredentials().Error.Should().Be("invalid_credentials");
        ApiErrorDto.EmailExists().Error.Should().Be("email_exists");
        ApiErrorDto.TokenExpired().Error.Should().Be("token_expired");
        ApiErrorDto.InvalidRefreshToken().Error.Should().Be("invalid_refresh_token");
        ApiErrorDto.Forbidden().Error.Should().Be("forbidden");
        ApiErrorDto.NotFound("Mission").Error.Should().Be("not_found");
        ApiErrorDto.InternalError().Error.Should().Be("internal_error");
        ApiErrorDto.SessionCompleted().Error.Should().Be("session_completed");
        ApiErrorDto.InvalidPasswordFormat().Error.Should().Be("invalid_password_format");
        ApiErrorDto.InvalidCurrentPassword().Error.Should().Be("invalid_current_password");
        ApiErrorDto.TimeRangeExceeded().Error.Should().Be("time_range_exceeded");
        ApiErrorDto.InvalidTimeRange().Error.Should().Be("invalid_time_range");
    }

    [Fact]
    public void Pagination_From_computes_total_pages()
    {
        var p = PaginationDto.From(page: 1, limit: 20, total: 45);
        p.TotalPages.Should().Be(3);
    }

    [Fact]
    public void MissionSession_NewId_has_sess_prefix()
    {
        MissionSession.NewId().Should().StartWith("sess_");
    }

    [Fact]
    public void All_response_dtos_instantiate_with_defaults()
    {
        _ = new AuthResponse();
        _ = new UserResponse();
        _ = new MissionStatsDto();
        _ = new MissionHistoryDto();
        _ = new MissionDetailDto();
        _ = new DashboardSummaryResponse();
        _ = new DashboardAlertsResponse();
        _ = new LaunchWindowsResponse();
        _ = new MissionSimulateResponse();
        _ = new SessionResponse();
        _ = new SessionCompleteResponse();
        _ = new StatusResponse();
    }

    [Fact]
    public void SessionCompleteRequest_status_parses_known_values()
    {
        SessionStatusExtensions.TryParse("success", out var s).Should().BeTrue();
        s.Should().Be(SessionStatus.Success);
        SessionStatusExtensions.TryParse("failure", out s).Should().BeTrue();
        s.Should().Be(SessionStatus.Failure);
        SessionStatusExtensions.TryParse("aborted", out s).Should().BeTrue();
        s.Should().Be(SessionStatus.Aborted);
        SessionStatusExtensions.TryParse("nonsense", out _).Should().BeFalse();
    }
}
```

### Task 5.2 — Run tests + commit

```bash
dotnet test MissionClear.Tests
git add MissionClear.Tests/Models/DtoSerializationTests.cs
git commit -m "test(models): compile-check + snake_case + factory invariants for domain and DTOs"
```

---

## Definition of Done

- [ ] `dotnet build` no `MissionClear.Api` compila sem warnings
- [ ] `dotnet test` passa todos os testes em `DtoSerializationTests`
- [ ] Todos os domain records criados em `Models/Domain/` + `Models/Tle/`
- [ ] Todos os DTOs criados em `Models/Dtos/{Common,Orbital,Mission,Auth,User,History,Dashboard}/`
- [ ] `KnownDestinations` expõe ISS (408 km, 51.6°), LEO_GENERIC (400 km, 28.5°), SSO (500 km, 97.4°)
- [ ] `RiskLevelClassifier.Classify` retorna Critical (<1), High (<5), Medium (<10), Low (resto)
- [ ] `ApiErrorDto` expõe todos os 14 factory methods
- [ ] Toda propriedade JSON usa `[JsonPropertyName("snake_case")]`
- [ ] Nenhum arquivo passa de 200 linhas

## Handoff para os próximos planos

- **plan-03-orbital** consome `OrbitalObject`, `TleRecord`, `CelesTrakGpRecord`, `LaunchWindow`, `ConjunctionResult`
- **plan-04-auth** consome `AuthRegisterRequest`, `AuthLoginRequest`, `AuthResponse`, `ApiErrorDto.*`
- **plan-05-mission** consome `MissionSession`, `SessionRequest/Response`, `SessionCompleteRequest/Response`
- **plan-06-history-dashboard** consome `MissionHistoryDto`, `MissionDetailDto`, `MissionStatsDto`, `DashboardSummaryResponse`, `DashboardAlertsResponse`
- **plan-07-controllers** consome todos os DTOs + `ApiErrorDto` factory methods
