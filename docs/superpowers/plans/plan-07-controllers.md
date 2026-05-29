# Implementation Plan 07 — API Controllers + Authorization + Program.cs

> **Fonte da verdade.** Substitui toda versão anterior deste plano.
> **Para workers agenticos:** sub-skill obrigatória: `superpowers:executing-plans`

**Goal:** Implementar os 9 controllers com autorização por Role, Program.cs final com DI completo (Aspire), GlobalExceptionMiddleware atualizado e testes de integração com WebApplicationFactory + InMemory DB.

**Dependências:** Fases 00–06 devem estar com `dotnet build` + `dotnet test` verdes antes de iniciar esta fase.

---

## Regras críticas (ler antes de qualquer código)

| Regra | Detalhe |
|---|---|
| Zero lógica de negócio em controllers | Todos os controllers são thin adapters — call service → return result |
| `stats` ANTES de `{id}` | Em `MissionsController`, `[HttpGet("stats")]` DEVE vir antes de `[HttpGet("{id}")]` |
| DELETE requer Administrator | `DELETE /api/missions/{id}` → `[Authorize(Roles = "Administrator")]` |
| Rotas públicas | `status`, `debris`, `destinations`, `launch-windows`, `mission/simulate`, `mission/session` |
| Rotas autenticadas | `users/me`, `missions/*`, `auth/logout` |
| Dashboard summary | Opcional — retorna `user: null` sem token, dados do usuário com token |
| Never expose stack traces | GlobalExceptionMiddleware nunca inclui `ex.StackTrace` na resposta |
| `public partial class Program {}` | Obrigatório no fim de Program.cs para `WebApplicationFactory<Program>` |

---

## Arquitetura — arquivos desta fase

```
MissionClear.Api/
├── Program.cs                                      (REWRITE — DI completo + Aspire)
├── Controllers/
│   ├── BaseApiController.cs                        (CREATE)
│   ├── AuthController.cs                           (CREATE)
│   ├── UsersController.cs                          (CREATE)
│   ├── StatusController.cs                         (CREATE)
│   ├── DestinationsController.cs                   (CREATE)
│   ├── DebrisController.cs                         (CREATE)
│   ├── LaunchWindowsController.cs                  (CREATE)
│   ├── MissionController.cs                        (CREATE)
│   ├── MissionsController.cs                       (CREATE — histórico)
│   └── DashboardController.cs                      (CREATE)
└── Middleware/
    └── GlobalExceptionMiddleware.cs                (UPDATE — adicionar DomainException handling)

MissionClear.Tests/
├── MissionClear.Tests.csproj                       (UPDATE — adicionar Mvc.Testing)
└── Integration/
    ├── TestWebApplicationFactory.cs                (CREATE)
    ├── AuthEndpointTests.cs                        (CREATE)
    ├── MissionsAuthorizationTests.cs               (CREATE)
    └── StatusEndpointTests.cs                      (CREATE)
```

---

## Phase 1: Program.cs Final

### Task 1.1 — Substituir Program.cs completo

**Files:** `MissionClear.Api/Program.cs`

- [ ] Substituir conteúdo completo do arquivo

```csharp
using MissionClear.Api.Configuration;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Middleware;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// ── Startup guard ────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
if (jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be at least 32 characters.");

// ── Typed configuration ───────────────────────────────────────────────────────
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<OrbitalSettings>(
    builder.Configuration.GetSection(OrbitalSettings.SectionName));
builder.Services.Configure<ExternalApiSettings>(
    builder.Configuration.GetSection(ExternalApiSettings.SectionName));
builder.Services.Configure<CorsSettings>(
    builder.Configuration.GetSection(CorsSettings.SectionName));

// ── MySQL via Aspire Pomelo integration ───────────────────────────────────────
builder.AddMySqlDbContext<AppDbContext>("missionclear");

// ── CORS ─────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileApp", policy =>
    {
        if (builder.Environment.IsDevelopment())
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
        {
            var cors = builder.Configuration
                .GetSection(CorsSettings.SectionName)
                .Get<CorsSettings>();
            policy.WithOrigins(cors?.AllowedOrigins ?? [])
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

// ── JWT Bearer Authentication ─────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtSettings = builder.Configuration
            .GetSection(JwtSettings.SectionName)
            .Get<JwtSettings>()!;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSettings.Issuer,
            ValidAudience            = jwtSettings.Audience,
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        };
    });

builder.Services.AddAuthorization();

// ── Controllers ───────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    });

builder.Services.AddHttpClient();

// ── Repositories (Scoped — EF Core DbContext lifecycle) ───────────────────────
builder.Services.AddScoped<IUserRepository,         UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IMissionRepository,      MissionRepository>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IOrbitalCache,                OrbitalCache>();
builder.Services.AddScoped<IDataAggregatorService,          DataAggregatorService>();
builder.Services.AddSingleton<IOrbitalEngineService,         OrbitalEngineService>();
builder.Services.AddScoped<IConjunctionDetector,            ConjunctionDetector>();
builder.Services.AddScoped<ILaunchWindowCalculator,         LaunchWindowCalculator>();
builder.Services.AddScoped<IMissionSimulationService,       MissionSimulationService>();
builder.Services.AddSingleton<ISessionStore,                SessionStore>();
builder.Services.AddScoped<IMissionSseService,              MissionSseService>();
builder.Services.AddScoped<IJwtService,                     JwtService>();
builder.Services.AddScoped<IAuthService,                    AuthService>();
builder.Services.AddScoped<IUserService,                    UserService>();
builder.Services.AddScoped<IMissionHistoryService,          MissionHistoryService>();
builder.Services.AddScoped<IDashboardService,               DashboardService>();

// ── Background services ───────────────────────────────────────────────────────
builder.Services.AddHostedService<TleIngestionService>();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Auto-migrate on startup ───────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// ── Middleware pipeline (order matters) ───────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors("MobileApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints(); // Aspire health endpoints

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
```

- [ ] Commit: `chore(api): Program.cs final — Aspire DI, JWT, CORS, auto-migrate`

---

## Phase 2: BaseApiController + GlobalExceptionMiddleware

### Task 2.1 — BaseApiController

**Files:** `MissionClear.Api/Controllers/BaseApiController.cs`

- [ ] Criar arquivo

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Returns the authenticated user's Guid.
    /// Checks ClaimTypes.NameIdentifier first, then "sub" claim (JWT standard).
    /// Returns Guid.Empty when not authenticated or claim is missing/malformed.
    /// </summary>
    protected Guid CurrentUserId =>
        Guid.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub"),
            out var id)
        ? id : Guid.Empty;

    /// <summary>True when the request carries a valid, authenticated identity.</summary>
    protected bool IsAuthenticated => User.Identity?.IsAuthenticated == true;
}
```

### Task 2.2 — GlobalExceptionMiddleware (update)

**Files:** `MissionClear.Api/Middleware/GlobalExceptionMiddleware.cs`

- [ ] Reescrever/atualizar para capturar `DomainException` com `ErrorCode` + `HttpStatus` da exceção, e nunca expor stack trace

```csharp
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Exceptions;
using System.Text.Json;

namespace MissionClear.Api.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException ex)
        {
            logger.LogWarning("Domain error {Code} (HTTP {Status}): {Message}",
                ex.ErrorCode, ex.HttpStatus, ex.Message);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode    = ex.HttpStatus;
                context.Response.ContentType   = "application/json";
                var error = new ApiErrorDto(
                    ex.ErrorCode,
                    ex.Message,
                    DateTime.UtcNow.ToString("O"));
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(error, JsonOptions));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected unhandled exception");

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode    = 500;
                context.Response.ContentType   = "application/json";
                // Never expose ex.Message or stack trace in production response
                var error = new ApiErrorDto(
                    "INTERNAL_ERROR",
                    "An unexpected error occurred.",
                    DateTime.UtcNow.ToString("O"));
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(error, JsonOptions));
            }
        }
    }
}
```

**Nota:** Use `ex.HttpStatus` — this is the canonical property name defined in plan-02.

- [ ] Commit: `feat(api): BaseApiController + updated GlobalExceptionMiddleware`

---

## Phase 3: Auth & Users Controllers

### Task 3.1 — AuthController

**Files:** `MissionClear.Api/Controllers/AuthController.cs`

Route: `api/auth`

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class AuthController(IAuthService authService) : BaseApiController
{
    // POST api/auth/register — public
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct)
    {
        var result = await authService.RegisterAsync(request, ct);
        return StatusCode(201, result);
    }

    // POST api/auth/login — public
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        return Ok(result);
    }

    // POST api/auth/refresh — public
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken ct)
    {
        var result = await authService.RefreshAsync(request, ct);
        return Ok(result);
    }

    // POST api/auth/logout — requires auth
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken ct)
    {
        await authService.LogoutAsync(request, ct);
        return NoContent();
    }
}
```

### Task 3.2 — UsersController

**Files:** `MissionClear.Api/Controllers/UsersController.cs`

Route: `api/users` — all endpoints require `[Authorize]`

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Authorize]
public sealed class UsersController(IUserService userService) : BaseApiController
{
    // GET api/users/me
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var profile = await userService.GetProfileAsync(CurrentUserId, ct);
        return Ok(profile);
    }

    // PUT api/users/me
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(
        [FromBody] UpdateUserRequest request,
        CancellationToken ct)
    {
        var profile = await userService.UpdateProfileAsync(CurrentUserId, request, ct);
        return Ok(profile);
    }
}
```

- [ ] Commit: `feat(controllers): AuthController + UsersController`

---

## Phase 4: Orbital Public Endpoints

### Task 4.1 — StatusController

**Files:** `MissionClear.Api/Controllers/StatusController.cs`

Route: `api/status` — public

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Dtos.Status;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class StatusController(IOrbitalCache cache) : BaseApiController
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    // GET api/status
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new StatusResponse(
            Status:            cache.IsReady ? "ready" : "loading",
            TleCount:          cache.Count,
            PropagatedCount:   cache.Count,
            LastTleFetch:      cache.LastFetch?.ToString("O"),
            LastPropagation:   cache.LastPropagation?.ToString("O"),
            UptimeSeconds:     (long)(DateTime.UtcNow - StartTime).TotalSeconds,
            Sources:           new SourceStatusDto("ok", "unavailable")));
    }
}
```

### Task 4.2 — DestinationsController

**Files:** `MissionClear.Api/Controllers/DestinationsController.cs`

Route: `api/destinations` — public

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Dtos.Destination;
using MissionClear.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class DestinationsController : BaseApiController
{
    // GET api/destinations
    [HttpGet]
    public IActionResult Get()
    {
        var dtos = KnownDestinations.All
            .Select(d => new DestinationDto(
                d.Id,
                d.DisplayName,
                d.AltitudeKm,
                d.InclinationDeg,
                d.Description,
                d.DeltaVKmS,
                d.MissionDurationHours,
                d.Icon))
            .ToList();

        return Ok(new DestinationsResponse(dtos));
    }
}
```

### Task 4.3 — DebrisController

**Files:** `MissionClear.Api/Controllers/DebrisController.cs`

Route: `api/debris` — public. CRITICAL: `stats` route declared BEFORE `{id}`.

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class DebrisController(IOrbitalCache cache) : BaseApiController
{
    // GET api/debris
    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] double altitudeMinKm = 200,
        [FromQuery] double altitudeMaxKm = 2000,
        [FromQuery] string? type         = null,
        [FromQuery] int    limit         = 500)
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);

        var query = cache.GetAll()
            .Where(o => o.AltitudeKm >= altitudeMinKm && o.AltitudeKm <= altitudeMaxKm);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(o => o.Type == type);

        var result = query
            .Take(Math.Min(limit, 2000))
            .Select(o => new DebrisDto(
                o.Id, o.Name, o.Type,
                o.Latitude, o.Longitude,
                o.AltitudeKm, o.VelocityKmS,
                o.Source, o.UpdatedAt.ToString("O")))
            .ToList();

        Response.Headers.CacheControl = "max-age=60";
        return Ok(result);
    }

    // GET api/debris/stats  ← MUST be before {id}
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);

        var all = cache.GetAll();
        int debris = 0, satellite = 0, rocket = 0, low = 0, mid = 0, high = 0;

        foreach (var o in all)
        {
            switch (o.Type)
            {
                case "debris":      debris++;  break;
                case "satellite":   satellite++; break;
                case "rocket_body": rocket++;  break;
            }
            if      (o.AltitudeKm < 500)  low++;
            else if (o.AltitudeKm < 1000) mid++;
            else                          high++;
        }

        return Ok(new DebrisStatsDto(
            TotalTracked: all.Count,
            ByType:       new ByTypeDto(debris, satellite, rocket),
            ByAltitudeBand: new ByAltitudeBandDto(low, mid, high),
            Sources:      new SourcesDto(
                              all.Count(o => o.Source == "celestrak"),
                              all.Count(o => o.Source == "keeptrack")),
            LastUpdated:  (cache.LastPropagation ?? DateTime.UtcNow).ToString("O")));
    }

    // GET api/debris/{id}  ← AFTER stats
    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);

        var obj = cache.GetById(id)
            ?? throw new DomainException("DEBRIS_NOT_FOUND", $"Object {id} not found.", 404);

        TleDto? tle = null;
        if (obj.TleLine1 != null && obj.TleLine2 != null)
            tle = new TleDto(obj.TleEpoch ?? "", obj.TleLine1, obj.TleLine2);

        OrbitParamsDto? orbit = null;
        if (obj.InclinationDeg.HasValue)
            orbit = new OrbitParamsDto(
                obj.InclinationDeg.Value,
                obj.Eccentricity    ?? 0,
                obj.PeriodMinutes   ?? 0,
                obj.ApogeeKm        ?? 0,
                obj.PerigeeKm       ?? 0);

        return Ok(new DebrisDetailDto(
            obj.Id, obj.Name, obj.Type,
            obj.Latitude, obj.Longitude,
            obj.AltitudeKm, obj.VelocityKmS,
            obj.Source, obj.UpdatedAt.ToString("O"),
            tle, orbit));
    }
}
```

- [ ] Commit: `feat(controllers): StatusController + DestinationsController + DebrisController`

---

## Phase 5: Launch Windows & Mission Controllers

### Task 5.1 — LaunchWindowsController

**Files:** `MissionClear.Api/Controllers/LaunchWindowsController.cs`

Route: `api/launch-windows` — public

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class LaunchWindowsController(
    ILaunchWindowCalculator calculator,
    IOrbitalCache           cache) : BaseApiController
{
    // GET api/launch-windows
    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] string? destination,
        [FromQuery] string? from,
        [FromQuery] string? to)
    {
        ParseAndValidate(destination, from, to, out var dest, out var fromDt, out var toDt);
        var debris  = RequireCache();
        var windows = calculator.Calculate(dest, fromDt, toDt, debris);

        var dtos = windows
            .Select(w => new LaunchWindowDto(
                w.Start.ToString("O"), w.End.ToString("O"),
                w.RiskScore, w.DeltaVKmS, w.DurationHours, w.IsRecommended,
                w.Conjunctions
                    .Select(c => new ConjunctionDto(
                        c.DebrisId, c.DebrisName,
                        c.ClosestApproachKm,
                        c.TimeOfClosestApproach.ToString("O"),
                        c.RiskLevel.ToString().ToLowerInvariant()))
                    .ToList()))
            .ToList();

        return Ok(new
        {
            destination  = dest.DisplayName,
            from         = fromDt.ToString("O"),
            to           = toDt.ToString("O"),
            total_windows = dtos.Count,
            safe_windows  = dtos.Count(w => w.IsRecommended),
            windows       = dtos
        });
    }

    // GET api/launch-windows/best
    [HttpGet("best")]
    public IActionResult GetBest(
        [FromQuery] string? destination,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int     count   = 5,
        [FromQuery] double  maxRisk = 0.3)
    {
        ParseAndValidate(destination, from, to, out var dest, out var fromDt, out var toDt);
        var debris  = RequireCache();
        var windows = calculator.Calculate(dest, fromDt, toDt, debris);

        var best = windows
            .Where(w => w.RiskScore <= maxRisk)
            .OrderBy(w => w.RiskScore)
            .Take(Math.Min(count, 20))
            .Select((w, i) => new BestWindowDto(
                i + 1,
                w.Start.ToString("O"), w.End.ToString("O"),
                w.RiskScore, w.DeltaVKmS, w.DurationHours,
                w.Conjunctions
                    .Select(c => new ConjunctionDto(
                        c.DebrisId, c.DebrisName,
                        c.ClosestApproachKm,
                        c.TimeOfClosestApproach.ToString("O"),
                        c.RiskLevel.ToString().ToLowerInvariant()))
                    .ToList()))
            .ToList();

        return Ok(new
        {
            destination  = dest.DisplayName,
            from         = fromDt.ToString("O"),
            to           = toDt.ToString("O"),
            best_windows = best
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IReadOnlyList<OrbitalObject> RequireCache()
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);
        return cache.GetAll();
    }

    private static void ParseAndValidate(
        string? dest, string? from, string? to,
        out MissionDestination destination,
        out DateTime fromDt, out DateTime toDt)
    {
        if (string.IsNullOrEmpty(dest))
            throw new DomainException("MISSING_PARAMETER", "'destination' is required.", 400);
        if (string.IsNullOrEmpty(from))
            throw new DomainException("MISSING_PARAMETER", "'from' is required.", 400);
        if (string.IsNullOrEmpty(to))
            throw new DomainException("MISSING_PARAMETER", "'to' is required.", 400);

        destination = KnownDestinations.FindById(dest)
            ?? throw new DomainException("INVALID_DESTINATION",
                $"Unknown destination: {dest}", 400);

        if (!DateTime.TryParse(from, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out fromDt))
            throw new DomainException("INVALID_DATE_FORMAT",
                "'from' is not a valid ISO 8601 date.", 400);

        if (!DateTime.TryParse(to, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out toDt))
            throw new DomainException("INVALID_DATE_FORMAT",
                "'to' is not a valid ISO 8601 date.", 400);

        fromDt = fromDt.ToUniversalTime();
        toDt   = toDt.ToUniversalTime();

        if ((toDt - fromDt).TotalHours > 48)
            throw new DomainException("TIME_RANGE_EXCEEDED",
                "Range cannot exceed 48 hours.", 400);
    }
}
```

### Task 5.2 — MissionController

**Files:** `MissionClear.Api/Controllers/MissionController.cs`

Route: `api/mission` — all endpoints public (save_to_history requires optional auth at service level)

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class MissionController(
    IMissionSimulationService simulation) : BaseApiController
{
    // POST api/mission/simulate — public
    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(
        [FromBody] SimulateRequest request,
        CancellationToken ct)
    {
        var result = await simulation.SimulateAsync(request, ct);
        return Ok(result);
    }

    // POST api/mission/session — public
    [HttpPost("session")]
    public async Task<IActionResult> CreateSession(
        [FromBody] SessionRequest request,
        CancellationToken ct)
    {
        var result = await simulation.CreateSessionAsync(request, ct);
        return StatusCode(201, result);
    }

    // GET api/mission/session/{sessionId}/stream — public (SSE)
    [HttpGet("session/{sessionId}/stream")]
    public async Task StreamSession(
        string sessionId,
        [FromServices] IMissionSseService sseService,
        CancellationToken ct)
    {
        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["Connection"]        = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await sseService.StreamAsync(sessionId, Response, ct);
    }

    // POST api/mission/session/{sessionId}/complete — optional auth
    [HttpPost("session/{sessionId}/complete")]
    public async Task<IActionResult> CompleteSession(
        string sessionId,
        [FromBody] CompleteSessionRequest request,
        CancellationToken ct)
    {
        Guid? userId = IsAuthenticated ? CurrentUserId : null;
        var result = await simulation.CompleteSessionAsync(sessionId, request, userId, ct);
        return Ok(result);
    }
}
```

- [ ] Commit: `feat(controllers): LaunchWindowsController + MissionController`

---

## Phase 6: History & Dashboard Controllers

### Task 6.1 — MissionsController

**Files:** `MissionClear.Api/Controllers/MissionsController.cs`

Route: `api/missions` — all endpoints require `[Authorize]`. DELETE requires `Administrator` role.

**CRITICAL:** `[HttpGet("stats")]` MUST be declared before `[HttpGet("{id}")]` in source to prevent routing ambiguity.

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Authorize]
public sealed class MissionsController(
    IMissionHistoryService historyService) : BaseApiController
{
    // GET api/missions — any authenticated role
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int     page        = 1,
        [FromQuery] int     limit       = 20,
        [FromQuery] string? status      = null,
        [FromQuery] string? destination = null,
        [FromQuery] string  sort        = "created_at_desc",
        CancellationToken   ct          = default)
    {
        limit = Math.Min(limit, 50);
        var result = await historyService.GetMissionsAsync(
            CurrentUserId, page, limit, status, destination, sort, ct);
        return Ok(result);
    }

    // GET api/missions/stats — MUST be before {id} route
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var result = await historyService.GetStatsAsync(CurrentUserId, ct);
        return Ok(result);
    }

    // GET api/missions/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id.Replace("msn_", ""), out var guid))
            return BadRequest(new { error = "INVALID_ID", message = "Invalid mission ID format." });

        var result = await historyService.GetMissionDetailAsync(guid, CurrentUserId, ct);
        return Ok(result);
    }

    // DELETE api/missions/{id} — Administrator role only
    [HttpDelete("{id}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id.Replace("msn_", ""), out var guid))
            return BadRequest(new { error = "INVALID_ID", message = "Invalid mission ID format." });

        await historyService.DeleteMissionAsync(guid, CurrentUserId, ct);
        return NoContent();
    }
}
```

### Task 6.2 — DashboardController

**Files:** `MissionClear.Api/Controllers/DashboardController.cs`

Route: `api/dashboard` — public (optional auth on summary)

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;

namespace MissionClear.Api.Controllers;

public sealed class DashboardController(
    IDashboardService dashboardService) : BaseApiController
{
    // GET api/dashboard/summary — optional auth
    // Without token: user section is null
    // With token: user section populated (display_name patched from JWT claims)
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        Guid? userId = null;
        string? displayName = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(sub, out var uid))
                userId = uid;
            displayName = User.FindFirst("display_name")?.Value;
        }

        var result = await dashboardService.GetSummaryAsync(userId, ct);

        // Patch display_name from JWT claims if available
        if (displayName != null && result.User != null)
        {
            // Since record is immutable, reconstruct UserDashboardDto with correct name
            var patchedUser = result.User with { DisplayName = displayName };
            result = result with { User = patchedUser };
        }

        return Ok(result);
    }

    // GET api/dashboard/alerts — public
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] int    windowHours = 6,
        [FromQuery] string minRisk     = "medium",
        CancellationToken  ct          = default)
    {
        windowHours = Math.Clamp(windowHours, 1, 24);
        var result = await dashboardService.GetAlertsAsync(windowHours, minRisk, ct);
        return Ok(result);
    }
}
```

- [ ] Commit: `feat(controllers): MissionsController (role-based) + DashboardController`

---

## Phase 7: Integration Tests

### Task 7.1 — Add NuGet package

**Files:** `MissionClear.Tests/MissionClear.Tests.csproj`

- [ ] Adicionar referência ao pacote de testing

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.*" />
```

### Task 7.2 — appsettings.Testing.json

**Files:** `MissionClear.Api/appsettings.Testing.json`

- [ ] Criar arquivo (copiado ao output via csproj ou ConfigureAppConfiguration)

```json
{
  "Jwt": {
    "Secret": "test-secret-key-with-at-least-32-characters-long!!",
    "Issuer": "mission-clear-api-test",
    "Audience": "mission-clear-mobile-test",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 7
  },
  "KeepTrack": {
    "ApiKey": ""
  }
}
```

### Task 7.3 — TestWebApplicationFactory

**Files:** `MissionClear.Tests/Integration/TestWebApplicationFactory.cs`

- [ ] Criar arquivo

```csharp
using MissionClear.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MissionClear.Tests.Integration;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Inject test configuration — must satisfy startup guard (Jwt:Secret >= 32 chars)
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]             = "test-secret-key-with-at-least-32-characters-long!!",
                ["Jwt:Issuer"]             = "mission-clear-api-test",
                ["Jwt:Audience"]           = "mission-clear-mobile-test",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"]   = "7",
                ["KeepTrack:ApiKey"]       = "",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace MySQL DbContext with InMemory for tests
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));
        });
    }
}
```

### Task 7.4 — AuthEndpointTests

**Files:** `MissionClear.Tests/Integration/AuthEndpointTests.cs`

- [ ] Criar arquivo

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MissionClear.Api.Dtos.Auth;
using Xunit;

namespace MissionClear.Tests.Integration;

public sealed class AuthEndpointTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Register_Returns201_WithResearcherRole()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email        = $"{Guid.NewGuid():N}@test.com",
            password     = "Test@Pass1",
            display_name = "New User"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        body!.User.Role.Should().Be("Researcher");
        body.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_Returns409_WhenEmailDuplicate()
    {
        var email = $"{Guid.NewGuid():N}@test.com";
        var payload = new { email, password = "Test@Pass1", display_name = "Dup" };

        await _client.PostAsJsonAsync("/api/auth/register", payload);
        var second = await _client.PostAsJsonAsync("/api/auth/register", payload);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_Returns401_WithWrongPassword()
    {
        var email = $"{Guid.NewGuid():N}@test.com";
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Test@Pass1", display_name = "User" });

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Wrong@Pass1" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_Returns200_WithCorrectCredentials()
    {
        var email = $"{Guid.NewGuid():N}@test.com";
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Test@Pass1", display_name = "User" });

        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Test@Pass1" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

### Task 7.5 — MissionsAuthorizationTests

**Files:** `MissionClear.Tests/Integration/MissionsAuthorizationTests.cs`

- [ ] Criar arquivo

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MissionClear.Api.Dtos.Auth;
using Xunit;

namespace MissionClear.Tests.Integration;

public sealed class MissionsAuthorizationTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> RegisterAndGetTokenAsync(string role = "Researcher")
    {
        var email    = $"{Guid.NewGuid():N}@test.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password     = "Test@Pass1",
            display_name = "Test User",
            role          // service must accept role hint; otherwise all users are Researcher
        });
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        return auth!.AccessToken;
    }

    [Fact]
    public async Task GetMissions_Returns401_WithoutToken()
    {
        var response = await _client.GetAsync("/api/missions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMissions_Returns200_WithValidToken()
    {
        var token = await RegisterAndGetTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/missions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMissionStats_Returns200_WithValidToken()
    {
        var token = await RegisterAndGetTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/missions/stats");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMission_Returns403_ForResearcher()
    {
        // Researcher role does not have Administrator — must receive 403
        var token = await RegisterAndGetTokenAsync("Researcher");
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Non-existent mission ID is fine — auth check happens before DB lookup
        var response = await _client.DeleteAsync(
            "/api/missions/msn_00000000000000000000000000000001");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

### Task 7.6 — StatusEndpointTests

**Files:** `MissionClear.Tests/Integration/StatusEndpointTests.cs`

- [ ] Criar arquivo

```csharp
using System.Net;
using FluentAssertions;
using Xunit;

namespace MissionClear.Tests.Integration;

public sealed class StatusEndpointTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetStatus_Returns200_Always()
    {
        var response = await _client.GetAsync("/api/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDestinations_Returns200_WithList()
    {
        var response = await _client.GetAsync("/api/destinations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDebris_WhenCacheNotReady_Returns503()
    {
        // Factory starts with empty cache → IsReady = false → CACHE_NOT_READY
        var response = await _client.GetAsync("/api/debris");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
```

- [ ] Commit: `test(controllers): integration tests — auth, authorization, status`

---

## Phase 8: Final Verification

### Task 8.1 — Build & Test

- [ ] Verificar build limpo (zero warnings, zero errors)

```powershell
dotnet build MissionClear.sln
```

- [ ] Rodar todos os testes

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj -v normal
```

- [ ] Rodar apenas testes de integração

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "Integration" -v normal
```

### Task 8.2 — Middleware pipeline verification

Confirmar ordem no `Program.cs`:

```
app.UseMiddleware<GlobalExceptionMiddleware>();   ← 1st
app.UseCors("MobileApp");                         ← 2nd
app.UseAuthentication();                          ← 3rd
app.UseAuthorization();                           ← 4th
app.MapControllers();                             ← 5th
app.MapDefaultEndpoints();                        ← Aspire health
```

### Task 8.3 — Route order verification

Em `MissionsController.cs`, confirmar que `[HttpGet("stats")]` aparece textualmente ANTES de `[HttpGet("{id}")]` no arquivo fonte.

### Task 8.4 — Final commit

```powershell
git add MissionClear.Api/Controllers/ `
        MissionClear.Api/Program.cs `
        MissionClear.Api/Middleware/ `
        MissionClear.Tests/Integration/
git commit -m "feat(controllers): all 9 controllers, JWT role auth, Administrator-only DELETE"
```

---

## Authorization Matrix

| Endpoint | Method | Auth Required | Role |
|---|---|---|---|
| `/api/auth/register` | POST | No | — |
| `/api/auth/login` | POST | No | — |
| `/api/auth/refresh` | POST | No | — |
| `/api/auth/logout` | POST | Yes | Any |
| `/api/users/me` | GET | Yes | Any |
| `/api/users/me` | PUT | Yes | Any |
| `/api/status` | GET | No | — |
| `/api/destinations` | GET | No | — |
| `/api/debris` | GET | No | — |
| `/api/debris/stats` | GET | No | — |
| `/api/debris/{id}` | GET | No | — |
| `/api/launch-windows` | GET | No | — |
| `/api/launch-windows/best` | GET | No | — |
| `/api/mission/simulate` | POST | No | — |
| `/api/mission/session` | POST | No | — |
| `/api/mission/session/{id}/stream` | GET | No | — |
| `/api/mission/session/{id}/complete` | POST | Optional | — |
| `/api/missions` | GET | Yes | Any |
| `/api/missions/stats` | GET | Yes | Any |
| `/api/missions/{id}` | GET | Yes | Any |
| `/api/missions/{id}` | DELETE | Yes | **Administrator** |
| `/api/dashboard/summary` | GET | Optional | — |
| `/api/dashboard/alerts` | GET | No | — |

---

## Risks & Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| `/missions/stats` matched as `{id}` | HIGH | `[HttpGet("stats")]` declared first in source; test verifies 200 not 404 |
| Startup fails when Aspire MySQL not available | HIGH | InMemory DB in Testing env; `AddMySqlDbContext` is Aspire-only |
| `Program` not visible to test project | HIGH | `public partial class Program {}` at end of `Program.cs` |
| Claim mismatch (`sub` vs `NameIdentifier`) | HIGH | `BaseApiController.CurrentUserId` checks both |
| Stack trace in error response | HIGH | `GlobalExceptionMiddleware` catches all — never serializes `ex.StackTrace` |
| SSE buffered by reverse proxy | MEDIUM | `X-Accel-Buffering: no` + `Connection: keep-alive` headers |
| CORS blocks Mobile in production | MEDIUM | Startup guard: log warning if `AllowedOrigins` is empty in non-Development |
| InMemory DB lacks unique constraints | MEDIUM | Duplicate email check in `AuthService.RegisterAsync`, not DB |
| `DeleteMissionAsync` called without admin check at service | MEDIUM | Both `[Authorize(Roles="Administrator")]` on controller AND service-level ownership check |

---

## Success Criteria

- [ ] All 23 routes implemented and reachable
- [ ] Zero business logic in controllers — all delegation to services
- [ ] All controllers extend `BaseApiController`
- [ ] `GlobalExceptionMiddleware` catches `DomainException` with correct HTTP status from exception
- [ ] `GlobalExceptionMiddleware` never exposes stack traces
- [ ] `[Authorize(Roles = "Administrator")]` on `DELETE /api/missions/{id}`
- [ ] `GET /api/missions`, `GET /api/missions/stats`, `GET /api/missions/{id}` → `[Authorize]` any role
- [ ] `/api/missions/stats` route declared before `{id}` in source file
- [ ] SSE endpoint sets `text/event-stream`, `X-Accel-Buffering: no`
- [ ] `public partial class Program {}` at end of `Program.cs`
- [ ] `builder.AddMySqlDbContext<AppDbContext>("missionclear")` in Program.cs
- [ ] `app.MapDefaultEndpoints()` in Program.cs
- [ ] Auto-migrate runs on startup
- [ ] Integration tests pass: register→201+Researcher role, duplicate→409, wrong password→401, GET /missions without token→401, GET /missions with token→200, DELETE as Researcher→403, GET stats→200, GET /api/status→200
- [ ] `dotnet build` — 0 warnings, 0 errors
- [ ] `dotnet test` — all tests green

---

**This is the definitive plan for Phase 07. Plans 00–07 + Phase 08 (MVC Web) fully cover the Mission Clear backend.**
