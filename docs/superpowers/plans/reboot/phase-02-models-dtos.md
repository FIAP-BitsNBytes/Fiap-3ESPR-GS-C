# Phase 02 — Models + DTOs + DomainException

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Criar todos os domain models, DTOs e DomainException. Esta fase não tem lógica de negócio — apenas tipos.

**Nota:** Esta fase é idêntica ao plan-02-models.md original, com estas adições:
1. `role` em `RegisterRequest` (opcional, default "Researcher")
2. `role` em `UserDto` e `AuthResponseDto`

---

### Task 1: DomainException

**Files:**
- Create: `MissionClear.Api/Exceptions/DomainException.cs`

- [ ] **Step 1: Criar diretório**

```powershell
mkdir MissionClear.Api/Exceptions
```

- [ ] **Step 2: Escrever DomainException.cs**

```csharp
namespace MissionClear.Api.Exceptions;

public sealed class DomainException(string errorCode, string message, int httpStatus = 400)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int HttpStatus { get; } = httpStatus;
}
```

Uso:
```csharp
throw new DomainException("EMAIL_ALREADY_EXISTS", "Email já cadastrado.", 409);
throw new DomainException("INVALID_CREDENTIALS", "Email ou senha incorretos.", 401);
throw new DomainException("MISSION_NOT_FOUND", "Missão não encontrada.", 404);
throw new DomainException("FORBIDDEN", "Acesso negado.", 403);
throw new DomainException("CACHE_NOT_READY", "Cache orbital inicializando.", 503);
```

---

### Task 2: Domain Models

**Files:**
- Create: `MissionClear.Api/Models/OrbitalObject.cs`
- Create: `MissionClear.Api/Models/MissionDestination.cs`
- Create: `MissionClear.Api/Models/ConjunctionResult.cs`
- Create: `MissionClear.Api/Models/LaunchWindow.cs`
- Create: `MissionClear.Api/Models/MissionSession.cs`

(Arquivo `MissionClear.Api/Models/RiskLevel.cs` já existe — manter.)

- [ ] **Step 1: Criar OrbitalObject.cs**

```csharp
namespace MissionClear.Api.Models;

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

- [ ] **Step 2: Criar MissionDestination.cs**

```csharp
namespace MissionClear.Api.Models;

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
    public static readonly MissionDestination ISS = new(
        "ISS", "Estação Espacial Internacional", 408, 51.6,
        "Órbita da ISS — destino mais popular para missões LEO", 9.40, 6.2, "iss");

    public static readonly MissionDestination LeoGeneric = new(
        "LEO_GENERIC", "Órbita LEO Genérica", 400, 28.5,
        "Órbita baixa padrão para satélites de observação", 9.20, 5.8, "leo");

    public static readonly MissionDestination Sso = new(
        "SSO", "Sun-Synchronous Orbit", 500, 97.4,
        "Órbita heliosíncrona — usada por satélites de imageamento", 10.10, 7.0, "sso");

    public static readonly IReadOnlyList<MissionDestination> All = [ISS, LeoGeneric, Sso];

    public static MissionDestination? FindById(string id) =>
        All.FirstOrDefault(d => d.Id == id);
}
```

- [ ] **Step 3: Criar ConjunctionResult.cs**

```csharp
using MissionClear.Api.Helpers;

namespace MissionClear.Api.Models;

public sealed record ConjunctionResult(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    DateTime TimeOfClosestApproach,
    RiskLevel RiskLevel);
```

- [ ] **Step 4: Criar LaunchWindow.cs**

```csharp
namespace MissionClear.Api.Models;

public sealed record LaunchWindow(
    DateTime Start,
    DateTime End,
    double RiskScore,
    double DeltaVKmS,
    double DurationHours,
    bool IsRecommended,
    IReadOnlyList<ConjunctionResult> Conjunctions);
```

- [ ] **Step 5: Criar MissionSession.cs**

```csharp
namespace MissionClear.Api.Models;

public enum SessionStatus { Active, Completed, Expired }

public sealed class MissionSession
{
    public string SessionId { get; init; } = $"sess_{Guid.NewGuid():N}";
    public required string Destination { get; init; }
    public required DateTime DepartureTime { get; init; }
    public required DateTime ArrivalTime { get; init; }
    public DateTime ExpiresAt { get; init; } = DateTime.UtcNow.AddMinutes(30);
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public double RiskScore { get; set; }
    public double DeltaVKmS { get; set; }
    public int MissionScore { get; set; }
    public List<ConjunctionResult> Conjunctions { get; } = [];
}
```

---

### Task 3: DTOs — Auth

**Files:**
- Create: `MissionClear.Api/Dtos/Auth/RegisterRequest.cs`
- Create: `MissionClear.Api/Dtos/Auth/LoginRequest.cs`
- Create: `MissionClear.Api/Dtos/Auth/RefreshRequest.cs`
- Create: `MissionClear.Api/Dtos/Auth/LogoutRequest.cs`
- Create: `MissionClear.Api/Dtos/Auth/AuthResponse.cs`

- [ ] **Step 1: Criar diretório**

```powershell
mkdir MissionClear.Api/Dtos
mkdir MissionClear.Api/Dtos/Auth
```

- [ ] **Step 2: Escrever todos os DTOs de auth**

`RegisterRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record RegisterRequest(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    [Required][StringLength(50, MinimumLength = 2)] string DisplayName,
    string Role = "Researcher");
```

`LoginRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record LoginRequest(
    [Required][EmailAddress] string Email,
    [Required] string Password);
```

`RefreshRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record RefreshRequest([Required] string RefreshToken);
```

`LogoutRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Auth;

public sealed record LogoutRequest([Required] string RefreshToken);
```

`AuthResponse.cs`:
```csharp
namespace MissionClear.Api.Dtos.Auth;

public sealed record UserInAuthResponse(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    DateTime CreatedAt,
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

---

### Task 4: DTOs — User, Orbital, Mission, Common

**Files:**
- Create: `MissionClear.Api/Dtos/User/UserProfileResponse.cs`
- Create: `MissionClear.Api/Dtos/User/UpdateUserRequest.cs`
- Create: `MissionClear.Api/Dtos/Orbital/DebrisDto.cs`
- Create: `MissionClear.Api/Dtos/Orbital/DebrisDetailDto.cs`
- Create: `MissionClear.Api/Dtos/Orbital/DebrisStatsDto.cs`
- Create: `MissionClear.Api/Dtos/Mission/SimulateRequest.cs`
- Create: `MissionClear.Api/Dtos/Mission/SimulateResponse.cs`
- Create: `MissionClear.Api/Dtos/Mission/SessionRequest.cs`
- Create: `MissionClear.Api/Dtos/Mission/SessionResponse.cs`
- Create: `MissionClear.Api/Dtos/Mission/CompleteSessionRequest.cs`
- Create: `MissionClear.Api/Dtos/Mission/CompleteSessionResponse.cs`
- Create: `MissionClear.Api/Dtos/History/MissionListResponse.cs`
- Create: `MissionClear.Api/Dtos/History/MissionDetailResponse.cs`
- Create: `MissionClear.Api/Dtos/History/MissionStatsResponse.cs`
- Create: `MissionClear.Api/Dtos/Dashboard/DashboardSummaryResponse.cs`
- Create: `MissionClear.Api/Dtos/Dashboard/AlertsResponse.cs`
- Create: `MissionClear.Api/Dtos/Common/ApiErrorDto.cs`
- Create: `MissionClear.Api/Dtos/Common/PaginationDto.cs`
- Create: `MissionClear.Api/Dtos/Common/ConjunctionDto.cs`
- Create: `MissionClear.Api/Dtos/Common/LaunchWindowDto.cs`
- Create: `MissionClear.Api/Dtos/Common/BestWindowDto.cs`
- Create: `MissionClear.Api/Dtos/Status/StatusResponse.cs`
- Create: `MissionClear.Api/Dtos/Destination/DestinationDto.cs`

- [ ] **Step 1: Criar diretórios**

```powershell
mkdir MissionClear.Api/Dtos/User
mkdir MissionClear.Api/Dtos/Orbital
mkdir MissionClear.Api/Dtos/Mission
mkdir MissionClear.Api/Dtos/History
mkdir MissionClear.Api/Dtos/Dashboard
mkdir MissionClear.Api/Dtos/Common
mkdir MissionClear.Api/Dtos/Status
mkdir MissionClear.Api/Dtos/Destination
```

- [ ] **Step 2: Escrever DTOs comuns**

`Common/ApiErrorDto.cs`:
```csharp
namespace MissionClear.Api.Dtos.Common;

public sealed record ApiErrorDto(string Error, string Message, string Timestamp);
```

`Common/PaginationDto.cs`:
```csharp
namespace MissionClear.Api.Dtos.Common;

public sealed record PaginationDto(int Page, int Limit, int Total, int TotalPages);

public sealed record PagedResponse<T>(IReadOnlyList<T> Data, PaginationDto Pagination);
```

`Common/ConjunctionDto.cs`:
```csharp
namespace MissionClear.Api.Dtos.Common;

public sealed record ConjunctionDto(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    string TimeOfClosestApproach,
    string RiskLevel);
```

`Common/LaunchWindowDto.cs`:
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
```

`Common/BestWindowDto.cs`:
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
```

- [ ] **Step 3: Escrever DTOs de User**

`User/UserProfileResponse.cs`:
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

`User/UpdateUserRequest.cs`:
```csharp
namespace MissionClear.Api.Dtos.User;

public sealed record UpdateUserRequest(
    string? DisplayName,
    string? Password,
    string? CurrentPassword);
```

- [ ] **Step 4: Escrever DTOs orbitais**

`Orbital/DebrisDto.cs`:
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

`Orbital/DebrisDetailDto.cs`:
```csharp
namespace MissionClear.Api.Dtos.Orbital;

public sealed record TleDto(string Epoch, string Line1, string Line2);

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

`Orbital/DebrisStatsDto.cs`:
```csharp
namespace MissionClear.Api.Dtos.Orbital;

public sealed record ByTypeDto(int Debris, int Satellite, int RocketBody);
public sealed record ByAltitudeBandDto(int Low200500km, int Mid5001000km, int High10002000km);
public sealed record SourcesDto(int Celestrak, int Keeptrack);

public sealed record DebrisStatsDto(
    int TotalTracked,
    ByTypeDto ByType,
    ByAltitudeBandDto ByAltitudeBand,
    SourcesDto Sources,
    string LastUpdated);
```

- [ ] **Step 5: Escrever DTOs de missão**

`Mission/SimulateRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Mission;

public sealed record SimulateRequest(
    [Required] string Destination,
    [Required] string DepartureTime,
    [Required] string ArrivalTime);
```

`Mission/SimulateResponse.cs`:
```csharp
using MissionClear.Api.Dtos.Common;

namespace MissionClear.Api.Dtos.Mission;

public sealed record ObstacleDto(
    string DebrisId,
    string DebrisName,
    double ClosestApproachKm,
    string TimeOfClosestApproach,
    string RiskLevel);

public sealed record SimulateResponse(
    string Destination,
    string DepartureTime,
    string ArrivalTime,
    IReadOnlyList<object> Trajectory,
    IReadOnlyList<ObstacleDto> Obstacles,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS);
```

`Mission/SessionRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Mission;

public sealed record SessionRequest(
    [Required] string Destination,
    [Required] string DepartureTime,
    [Required] string ArrivalTime);
```

`Mission/SessionResponse.cs`:
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

`Mission/CompleteSessionRequest.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Dtos.Mission;

public sealed record CompleteSessionRequest(
    [Required] string Status,
    bool SaveToHistory = false);
```

`Mission/CompleteSessionResponse.cs`:
```csharp
namespace MissionClear.Api.Dtos.Mission;

public sealed record CompleteSessionResponse(
    string SessionId,
    string Status,
    int MissionScore,
    double RiskScore,
    double DeltaVKmS,
    int ObstaclesEncountered,
    long DurationSeconds,
    bool SavedToHistory,
    string? MissionId);
```

- [ ] **Step 6: Escrever DTOs de histórico e dashboard**

`History/MissionListResponse.cs`:
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

`History/MissionDetailResponse.cs`:
```csharp
using MissionClear.Api.Dtos.Mission;

namespace MissionClear.Api.Dtos.History;

public sealed record ScoreBreakdownDto(int EfficiencyScore, int SafetyScore, int Total);

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

`History/MissionStatsResponse.cs`:
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

`Dashboard/DashboardSummaryResponse.cs`:
```csharp
namespace MissionClear.Api.Dtos.Dashboard;

public sealed record OrbitalSummaryDto(
    int TotalTrackedObjects,
    ByTypeDto ByType,
    ByAltitudeBandDto ByAltitudeBand,
    int ActiveConjunctionAlerts,
    string LastUpdated);

public sealed record UserDashboardDto(
    string DisplayName,
    int TotalMissions,
    int BestScore,
    LastMissionDto? LastMission);

public sealed record LastMissionDto(
    string Destination,
    string Status,
    int Score,
    string CreatedAt);

public sealed record DashboardSummaryResponse(
    OrbitalSummaryDto Orbital,
    UserDashboardDto? User);

// References ByTypeDto and ByAltitudeBandDto from Orbital DTOs
// Declare here to avoid cross-namespace issues:
public sealed record ByTypeDto(int Debris, int Satellite, int RocketBody);
public sealed record ByAltitudeBandDto(int Low200500km, int Mid5001000km, int High10002000km);
```

`Dashboard/AlertsResponse.cs`:
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

`Status/StatusResponse.cs`:
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

`Destination/DestinationDto.cs`:
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

- [ ] **Step 7: Build para verificar tipos**

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj
```

Resultado esperado: sem erros de compilação.

- [ ] **Step 8: Commit**

```powershell
git add MissionClear.Api/Exceptions/ MissionClear.Api/Models/ MissionClear.Api/Dtos/
git commit -m "feat(models): domain models, DTOs, DomainException"
```
