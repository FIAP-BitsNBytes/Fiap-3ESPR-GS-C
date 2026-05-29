# Mission Clear — Architecture Overview

> Gerado em 2026-05-28. Fonte de verdade: plan-00 a plan-08.

---

## Solution (5 projetos)

```
MissionClear.sln
├── MissionClear.AppHost/         net8.0 — Aspire orquestrador
│   └── Program.cs                MySQL container + api + web
│
├── MissionClear.ServiceDefaults/ net8.0 — biblioteca pura
│   └── Extensions.cs             AddServiceDefaults(), MapDefaultEndpoints()
│
├── MissionClear.Api/             net10.0 — motor orbital + REST API
├── MissionClear.Web/             net10.0 — MVC web (cookie auth)
└── MissionClear.Tests/           net10.0 — xUnit
```

### Grafo de referências

```
AppHost ──► Api
AppHost ──► Web
Api     ──► ServiceDefaults
Web     ──► ServiceDefaults
Tests   ──► Api
```

`ServiceDefaults` não referencia nenhum projeto da solution.

---

## MissionClear.Api — estrutura interna

```
MissionClear.Api/
├── Configuration/
│   ├── JwtSettings.cs
│   ├── OrbitalSettings.cs
│   ├── ExternalApiSettings.cs
│   └── CorsSettings.cs
│
├── Entities/                         EF Core. Nunca expostas na API.
│   ├── UserEntity.cs                 [Table("users")] — Id, Email, DisplayName, PasswordHash, Role, CreatedAt
│   ├── RefreshTokenEntity.cs         [Table("refresh_tokens")] — FK UserId, Token, ExpiresAt, IsRevoked
│   └── MissionEntity.cs             [Table("missions")] — FK UserId, Status, Score, ObstaclesJson
│
├── Data/
│   ├── AppDbContext.cs               MySQL provider, índices únicos, cascade deletes
│   ├── AppDbContextFactory.cs        design-time factory p/ migrations sem AppHost
│   ├── Migrations/                   auto-gerado — InitialCreate
│   └── Repositories/
│       ├── IUserRepository.cs
│       ├── IRefreshTokenRepository.cs
│       ├── IMissionRepository.cs     + MissionPageResult + MissionStatsProjection
│       ├── UserRepository.cs
│       ├── RefreshTokenRepository.cs
│       └── MissionRepository.cs      sort: created_at_desc | score_desc | risk_score_asc
│
├── Exceptions/
│   └── DomainException.cs            ErrorCode (string) + HttpStatus (int) — 19 códigos canônicos
│
├── Models/
│   ├── RiskLevel.cs                  (existente — não alterar)
│   ├── OrbitalObject.cs              record — Latitude/Longitude (sem "Deg")
│   ├── MissionDestination.cs         record + KnownDestinations (ISS, LEO_GENERIC, SSO)
│   ├── ConjunctionResult.cs          record — DebrisId, DebrisName, ClosestApproachKm, RiskLevel
│   ├── LaunchWindow.cs               record — Start, End, RiskScore, DeltaVKmS, Conjunctions
│   └── MissionSession.cs             class (init-only) — SessionId, UserId (Guid req), Status mutável
│
├── Dtos/
│   ├── Auth/
│   │   RegisterRequest, LoginRequest, RefreshRequest, LogoutRequest
│   │   AuthResponse, UserInAuthResponse (com Role), RefreshTokenResponse
│   ├── User/
│   │   UserProfileResponse, UserStatsDto, UpdateUserRequest
│   ├── Orbital/
│   │   DebrisDto, DebrisDetailDto, DebrisStatsDto (JsonPropertyName explícito em ByAltitudeBandDto)
│   ├── Mission/
│   │   SimulateRequest, SimulateResponse, ObstacleDto (5 campos)
│   │   SessionRequest, SessionResponse (6 campos)
│   │   CompleteSessionRequest, CompleteSessionResponse (9 campos)
│   ├── History/
│   │   MissionSummaryDto, MissionDetailResponse, MissionStatsResponse
│   ├── Dashboard/
│   │   DashboardSummaryResponse, AlertsResponse
│   ├── Common/
│   │   ApiErrorDto (19 factory methods), PaginationDto, PagedResponse<T>
│   │   ConjunctionDto, LaunchWindowDto, LaunchWindowsResponse, BestWindowDto, BestWindowsResponse
│   ├── Status/
│   │   StatusResponse, SourceStatusDto
│   └── Destination/
│       DestinationDto, DestinationsResponse
│
├── Services/
│   ├── Interfaces/
│   │   ├── IOrbitalCache.cs              (Singleton)
│   │   ├── IOrbitalEngineService.cs      (Singleton — compartilha estado com IOrbitalCache)
│   │   ├── IDataAggregatorService.cs
│   │   ├── IJwtService.cs
│   │   ├── IAuthService.cs
│   │   ├── IUserService.cs
│   │   ├── IConjunctionDetector.cs
│   │   ├── ILaunchWindowCalculator.cs
│   │   ├── ISessionStore.cs
│   │   ├── IMissionSimulationService.cs
│   │   ├── IMissionHistoryService.cs
│   │   └── IDashboardService.cs          GetSummaryAsync(Guid? userId, string? displayName, CancellationToken)
│   │
│   ├── OrbitalCache.cs
│   ├── OrbitalEngineService.cs
│   ├── DataAggregatorService.cs          internal + InternalsVisibleTo("MissionClear.Tests")
│   ├── TleIngestionService.cs
│   ├── JwtService.cs
│   ├── AuthService.cs                    BCrypt.HashPassword/Verify, Role fixo = "Researcher"
│   ├── UserService.cs                    BuildProfileAsync → usr_{Guid:N}, CreatedAt.ToString("O")
│   ├── ConjunctionDetector.cs
│   ├── LaunchWindowCalculator.cs
│   ├── SessionStore.cs
│   ├── MissionSimulationService.cs
│   ├── MissionHistoryService.cs          SaveMissionAsync(11 params posicionais + CancellationToken)
│   └── DashboardService.cs
│
├── Controllers/                          Apenas roteamento + serialização. Zero lógica de negócio.
│   ├── AuthController.cs
│   ├── UsersController.cs
│   ├── DebrisController.cs
│   ├── LaunchWindowsController.cs
│   ├── MissionController.cs              GET /api/missions/stats declarado ANTES de {id}
│   ├── DashboardController.cs
│   ├── DestinationsController.cs
│   ├── StatusController.cs
│   └── MissionSseController.cs
│
├── Middleware/
│   └── GlobalExceptionMiddleware.cs      captura DomainException → ApiErrorDto + HttpStatus
│
├── Helpers/
│   ├── OrbitalMath.cs
│   ├── RiskScoring.cs
│   └── MissionScoring.cs
│
├── Properties/
│   └── AssemblyInfo.cs                   [assembly: InternalsVisibleTo("MissionClear.Tests")]
│
└── Program.cs                            EXCLUSIVO plan-07 — DI completo de todos os serviços
```

---

## MissionClear.Web — MVC com Cookie Auth

```
MissionClear.Web/
├── Program.cs                            Cookie+Claims auth, HttpClient → Api via Aspire
├── ApiClient.cs                          HttpClient wrapper — [JsonPropertyName] snake_case explícito
│                                         LoginApiResponse, RegisterApiResponse, LoginUserDto
├── Controllers/
│   ├── HomeController.cs
│   ├── AuthController.cs (MVC)           Login/Register via ApiClient, SignInWithCookieAsync
│   ├── MissionsController.cs (MVC)
│   ├── DashboardController.cs (MVC)
│   └── UsersController.cs (MVC)          [Authorize(Roles = "Administrator")] — class level
├── ViewModels/
└── Views/
```

### Cookie auth flow

```
Login form
  → POST ApiClient.LoginAsync()
  → API retorna JWT + refresh_token
  → SignInWithCookieAsync:
      extrai Claims: sub, email, role, display_name, access_token, refresh_token
  → Cookie persiste session
  → [Authorize(Roles = "Administrator")] protege rotas administrativas
```

---

## MissionClear.Tests — cobertura por plano

| Arquivo de teste | Plano | Testes |
|---|---|---|
| `Configuration/AppSettingsTests.cs` | 00 | binding POCOs sem banco |
| `Helpers/OrbitalMathTests.cs` | 00 | funções puras |
| `Helpers/RiskScoringTests.cs` | 00 | funções puras |
| `Helpers/MissionScoringTests.cs` | 00 | funções puras |
| `Data/UserRepositoryTests.cs` | 01 | 7 testes InMemory |
| `Data/RefreshTokenRepositoryTests.cs` | 01 | 6 testes InMemory |
| `Data/MissionRepositoryTests.cs` | 01 | 10 testes InMemory (sort, paginação, stats) |
| `Models/DtoCompileTests.cs` | 02 | compile-check todos os DTOs + DomainException + KnownDestinations |
| `Services/DataAggregatorTests.cs` | 03 | white-box via InternalsVisibleTo |
| `Services/OrbitalEngineTests.cs` | 03 | propagação SGP4, deduplicação |
| `Services/AuthServiceTests.cs` | 04 | BCrypt, JWT, refresh, role fixo |
| `Services/UserServiceTests.cs` | 04 | profile, update, Moq repos |
| `Services/ConjunctionDetectorTests.cs` | 05 | detecção de aproximação |
| `Services/LaunchWindowCalculatorTests.cs` | 05 | janelas seguras |
| `Services/MissionSimulationTests.cs` | 05 | SessionStore, simulate, complete |
| `Services/MissionHistoryTests.cs` | 06 | SaveMissionAsync 11 params, list, stats |
| `Services/DashboardServiceTests.cs` | 06 | summary anon vs autenticado |
| `Integration/AuthControllerTests.cs` | 07 | WebApplicationFactory, register/login |
| `Integration/MissionControllerTests.cs` | 07 | simulate, complete, stats (antes de {id}) |
| `Integration/UsersControllerTests.cs` | 07 | 401/403 sem token/role errado |
| `Web/AuthMvcTests.cs` | 08 | cookie gerado, redirect após login |
| `Web/DashboardMvcTests.cs` | 08 | 200 autenticado, 302 anon |

**Meta:** ≥ 80% cobertura em Services. Rodar por categoria:

```powershell
dotnet test --filter "Repository|Config"                          # Wave 2
dotnet test --filter "Orbital|Auth|Jwt"                          # Wave 3
dotnet test --filter "Conjunction|LaunchWindow|Session|Simulation" # Wave 4
dotnet test --filter "History|Dashboard"                          # Wave 5
dotnet test                                                       # Wave 6 — todos
```

---

## Convenções globais

| Categoria | Regra |
|---|---|
| User ID | `usr_{Guid:N}` |
| Mission ID | `msn_{Guid:N}` |
| Session ID | `sess_{Guid:N}` |
| Timestamps | `DateTime.UtcNow.ToString("O")` — ISO 8601 UTC |
| JSON naming | `JsonNamingPolicy.SnakeCaseLower` global via `AddJsonOptions` |
| Exceção ao JSON naming | `[JsonPropertyName]` explícito em `ByAltitudeBandDto` e em `ApiClient` do Web |
| Role padrão | `"Researcher"` — hardcoded em `AuthService.RegisterAsync` |
| Roles válidas | `"Researcher"` \| `"Administrator"` |
| Routes admin (API) | `DELETE /api/missions/{id}` — `[Authorize(Roles = "Administrator")]` |
| Routes admin (MVC) | `UsersController` inteiro — `[Authorize(Roles = "Administrator")]` na classe |
| Program.cs | SOMENTE plan-07 escreve o final. Todos os outros pulos esse passo. |
| IOrbitalEngineService DI | `AddSingleton` — compartilha estado com `IOrbitalCache` (também Singleton) |
| Demais services DI | `AddScoped` |
| Repositories DI | `AddScoped` |
| InternalsVisibleTo | `MissionClear.Api/Properties/AssemblyInfo.cs` → `MissionClear.Tests` |

---

## Pacotes principais

| Projeto | Pacote | Versão |
|---|---|---|
| Api | `Aspire.Pomelo.EntityFrameworkCore.MySql` | 9.1.0 |
| Api | `Pomelo.EntityFrameworkCore.MySql` | 8.0.2 |
| Api | `BCrypt.Net-Next` | 4.0.3 |
| Api | `Microsoft.AspNetCore.Authentication.JwtBearer` | 8.0.10 |
| AppHost | `Aspire.Hosting.AppHost` | 9.1.0 |
| AppHost | `Aspire.Hosting.MySql` | 9.1.0 |
| Tests | `Microsoft.AspNetCore.Mvc.Testing` | 8.0.10 |
| Tests | `Microsoft.EntityFrameworkCore.InMemory` | 8.0.10 |
| Tests | `Moq` | 4.20.70 |
| Tests | `FluentAssertions` | 6.12.1 |
| Tests | `xunit` | 2.9.0 |
