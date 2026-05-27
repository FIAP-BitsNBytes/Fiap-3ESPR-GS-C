# Implementation Plan 07: Controllers & API Endpoints

## Overview

Final plan in the Mission Clear backend series. Wires all 23 REST endpoints across 9 controllers, integrating every service built in plans 00-06. Controllers act as thin HTTP adapters: validate input shape, call a service, map the result. Zero business logic. Includes global exception middleware, base controller helpers, and integration tests using `WebApplicationFactory` with InMemory database.

## Requirements

- All 23 routes from `docs/API_CONTRACT.md` implemented exactly as specified
- Zero business logic in controllers (anti-pattern from CLAUDE.md)
- Unified error envelope via `ApiErrorDto`
- Centralized domain exception mapping via `BaseApiController.DomainError(...)`
- Global exception middleware as last-resort handler (no stack traces in production)
- `[Authorize]` enforced on protected routes; optional auth on dual-mode routes
- Integration tests using `WebApplicationFactory<Program>` with InMemory DB
- Route order: `/missions/stats` declared BEFORE `/missions/{id}`

## Architecture Changes

```
MissionClear.Api/
├── Controllers/
│   ├── BaseApiController.cs          (NEW — helpers)
│   ├── AuthController.cs             (NEW)
│   ├── UsersController.cs            (NEW)
│   ├── StatusController.cs           (NEW)
│   ├── DebrisController.cs           (NEW)
│   ├── DestinationsController.cs     (NEW)
│   ├── LaunchWindowsController.cs    (NEW)
│   ├── MissionController.cs          (NEW)
│   ├── MissionsController.cs         (NEW — history)
│   └── DashboardController.cs        (NEW)
├── Middleware/
│   └── GlobalExceptionMiddleware.cs  (NEW)
└── Program.cs                        (UPDATE — middleware + CORS)

MissionClear.Tests/
└── Controllers/
    ├── TestWebApplicationFactory.cs  (NEW)
    ├── AuthControllerTests.cs        (NEW)
    ├── DebrisControllerTests.cs      (NEW)
    └── MissionsControllerTests.cs    (NEW)
```

## Implementation Steps

---

### Phase 1: Foundation — Base Controller & Middleware

#### Task 1.1: BaseApiController

- [ ] Create `MissionClear.Api/Controllers/BaseApiController.cs`

```csharp
using MissionClear.Api.Dtos;
using MissionClear.Api.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected string? GetUserId() =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    protected IActionResult DomainError(DomainException ex) => ex.StatusCode switch
    {
        400 => BadRequest(ApiErrorDto.Make(ex.ErrorCode, ex.Message)),
        401 => Unauthorized(ApiErrorDto.Make(ex.ErrorCode, ex.Message)),
        403 => StatusCode(403, ApiErrorDto.Make(ex.ErrorCode, ex.Message)),
        404 => NotFound(ApiErrorDto.Make(ex.ErrorCode, ex.Message)),
        409 => Conflict(ApiErrorDto.Make(ex.ErrorCode, ex.Message)),
        503 => StatusCode(503, ApiErrorDto.Make(ex.ErrorCode, ex.Message)),
        _   => StatusCode(500, ApiErrorDto.InternalError())
    };

    protected IActionResult? RequireParam(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BadRequest(ApiErrorDto.Make("MISSING_PARAMETER", $"Required parameter '{paramName}' is missing."));
        return null;
    }
}
```

#### Task 1.2: Global Exception Middleware

- [ ] Create `MissionClear.Api/Middleware/GlobalExceptionMiddleware.cs`

```csharp
using System.Text.Json;
using MissionClear.Api.Dtos;
using MissionClear.Api.Exceptions;

namespace MissionClear.Api.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (DomainException dex)
        {
            _logger.LogWarning(dex, "Domain exception: {Code}", dex.ErrorCode);
            await WriteAsync(ctx, dex.StatusCode, ApiErrorDto.Make(dex.ErrorCode, dex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            var body = _env.IsProduction()
                ? ApiErrorDto.InternalError()
                : ApiErrorDto.Make("INTERNAL_ERROR", ex.Message);
            await WriteAsync(ctx, 500, body);
        }
    }

    private static async Task WriteAsync(HttpContext ctx, int status, ApiErrorDto body)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
```

- [ ] Register in `Program.cs` BEFORE `app.UseAuthentication()`:

```csharp
app.UseMiddleware<MissionClear.Api.Middleware.GlobalExceptionMiddleware>();
```

- [ ] Commit: `feat(api): base controller + global exception middleware`

---

### Phase 2: Auth & Users

#### Task 2.1: AuthController

- [ ] Create `MissionClear.Api/Controllers/AuthController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Dtos;
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;

namespace MissionClear.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : BaseApiController
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] AuthRegisterRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _auth.RegisterAsync(request, ct);
            return StatusCode(201, result);
        }
        catch (DomainException ex) { return DomainError(ex); }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthLoginRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _auth.LoginAsync(request, ct);
            return Ok(result);
        }
        catch (DomainException ex) { return DomainError(ex); }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] AuthRefreshRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _auth.RefreshAsync(request, ct);
            return Ok(result);
        }
        catch (DomainException ex) { return DomainError(ex); }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        try
        {
            var userId = GetUserId();
            if (userId is null) return Unauthorized(ApiErrorDto.Make("UNAUTHORIZED", "Missing user claim."));
            await _auth.LogoutAsync(userId, ct);
            return NoContent();
        }
        catch (DomainException ex) { return DomainError(ex); }
    }
}
```

#### Task 2.2: UsersController

- [ ] Create `MissionClear.Api/Controllers/UsersController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Dtos;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;

namespace MissionClear.Api.Controllers;

[Authorize]
[Route("api/users")]
public sealed class UsersController : BaseApiController
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized(ApiErrorDto.Make("UNAUTHORIZED", "Missing user claim."));
        try
        {
            var profile = await _users.GetMeAsync(userId, ct);
            return Ok(profile);
        }
        catch (DomainException ex) { return DomainError(ex); }
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized(ApiErrorDto.Make("UNAUTHORIZED", "Missing user claim."));
        try
        {
            var updated = await _users.UpdateMeAsync(userId, request, ct);
            return Ok(updated);
        }
        catch (DomainException ex) { return DomainError(ex); }
    }
}
```

- [ ] Commit: `feat(controllers): auth + users endpoints`

---

### Phase 3: Status, Debris, Destinations

#### Task 3.1: StatusController

- [ ] Create `MissionClear.Api/Controllers/StatusController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Services.Orbital;

namespace MissionClear.Api.Controllers;

[Route("api/status")]
public sealed class StatusController : BaseApiController
{
    private readonly OrbitalCache _cache;
    private readonly IConfiguration _config;
    private static readonly long _startTicks = Environment.TickCount64;

    public StatusController(OrbitalCache cache, IConfiguration config)
    {
        _cache = cache;
        _config = config;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var keepTrackKey = _config["KeepTrack:ApiKey"];
        return Ok(new
        {
            status = _cache.IsReady ? "ready" : "loading",
            tle_count = _cache.TleCount,
            propagated_count = _cache.GetPropagatedObjects().Count,
            last_tle_fetch = _cache.LastFetch,
            last_propagation = _cache.LastPropagation,
            uptime_seconds = (Environment.TickCount64 - _startTicks) / 1000,
            sources = new
            {
                celestrak = "active",
                keeptrack = string.IsNullOrWhiteSpace(keepTrackKey) ? "not_configured" : "configured"
            }
        });
    }
}
```

#### Task 3.2: DebrisController

- [ ] Create `MissionClear.Api/Controllers/DebrisController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Dtos;
using MissionClear.Api.Services.Orbital;

namespace MissionClear.Api.Controllers;

[Route("api/debris")]
public sealed class DebrisController : BaseApiController
{
    private const int MaxLimit = 1000;
    private const int DefaultLimit = 100;

    private readonly OrbitalCache _cache;

    public DebrisController(OrbitalCache cache) => _cache = cache;

    [HttpGet]
    public IActionResult List(
        [FromQuery] string? type,
        [FromQuery(Name = "min_altitude")] double? minAltitude,
        [FromQuery(Name = "max_altitude")] double? maxAltitude,
        [FromQuery] string? source,
        [FromQuery] int? limit)
    {
        if (!_cache.IsReady)
            return StatusCode(503, ApiErrorDto.CacheNotReady());

        var effectiveLimit = Math.Min(limit ?? DefaultLimit, MaxLimit);

        var query = _cache.GetPropagatedObjects().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase));
        if (minAltitude.HasValue)
            query = query.Where(d => d.AltitudeKm >= minAltitude.Value);
        if (maxAltitude.HasValue)
            query = query.Where(d => d.AltitudeKm <= maxAltitude.Value);
        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(d => string.Equals(d.Source, source, StringComparison.OrdinalIgnoreCase));

        return Ok(query.Take(effectiveLimit).Select(DebrisDto.From).ToList());
    }

    [HttpGet("stats")]
    public IActionResult Stats()
    {
        if (!_cache.IsReady)
            return StatusCode(503, ApiErrorDto.CacheNotReady());

        var all = _cache.GetPropagatedObjects();
        var byType = all
            .GroupBy(d => d.Type ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        var byBand = new Dictionary<string, int>
        {
            ["leo_low_200_500"]    = all.Count(d => d.AltitudeKm >= 200 && d.AltitudeKm < 500),
            ["leo_mid_500_1000"]   = all.Count(d => d.AltitudeKm >= 500 && d.AltitudeKm < 1000),
            ["leo_high_1000_2000"] = all.Count(d => d.AltitudeKm >= 1000 && d.AltitudeKm <= 2000),
        };

        return Ok(new
        {
            total_count = all.Count,
            by_type = byType,
            by_altitude_band = byBand,
            last_updated = _cache.LastPropagation
        });
    }

    [HttpGet("{id}")]
    public IActionResult Get(string id)
    {
        if (!_cache.IsReady)
            return StatusCode(503, ApiErrorDto.CacheNotReady());

        var item = _cache.GetPropagatedObjects()
            .FirstOrDefault(d => string.Equals(d.NoradCatId, id, StringComparison.OrdinalIgnoreCase));

        if (item is null)
            return NotFound(ApiErrorDto.Make("DEBRIS_NOT_FOUND", $"Debris '{id}' not found."));

        return Ok(DebrisDto.From(item));
    }
}
```

#### Task 3.3: DestinationsController

- [ ] Create `MissionClear.Api/Controllers/DestinationsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Domain;

namespace MissionClear.Api.Controllers;

[Route("api/destinations")]
public sealed class DestinationsController : BaseApiController
{
    [HttpGet]
    public IActionResult List() =>
        Ok(KnownDestinations.AllDestinations.Select(DestinationDto.From).ToList());
}
```

- [ ] Commit: `feat(controllers): status + debris + destinations endpoints`

---

### Phase 4: Launch Windows & Mission

#### Task 4.1: LaunchWindowsController

- [ ] Create `MissionClear.Api/Controllers/LaunchWindowsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Dtos;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.LaunchWindows;
using MissionClear.Api.Services.Orbital;

namespace MissionClear.Api.Controllers;

[Route("api/launch-windows")]
public sealed class LaunchWindowsController : BaseApiController
{
    private const int MaxRangeDays = 7;
    private const int MaxTopN = 20;
    private const int DefaultTopN = 5;

    private readonly ILaunchWindowCalculator _calculator;
    private readonly OrbitalCache _cache;

    public LaunchWindowsController(ILaunchWindowCalculator calculator, OrbitalCache cache)
    {
        _calculator = calculator;
        _cache = cache;
    }

    [HttpGet]
    public IActionResult List(
        [FromQuery] string? destination,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        if (!_cache.IsReady) return StatusCode(503, ApiErrorDto.CacheNotReady());

        var paramError = ValidateRange(destination, from, to);
        if (paramError is not null) return paramError;

        var result = _calculator.Calculate(destination!, from!.Value, to!.Value,
            _cache.GetPropagatedObjects());
        return Ok(new { destination, from, to, windows = result });
    }

    [HttpGet("best")]
    public IActionResult Best(
        [FromQuery] string? destination,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery(Name = "top_n")] int? topN)
    {
        if (!_cache.IsReady) return StatusCode(503, ApiErrorDto.CacheNotReady());

        var paramError = ValidateRange(destination, from, to);
        if (paramError is not null) return paramError;

        var effectiveTopN = Math.Min(topN ?? DefaultTopN, MaxTopN);
        if (effectiveTopN <= 0)
            return BadRequest(ApiErrorDto.Make("INVALID_PARAMETER", "top_n must be > 0."));

        var result = _calculator.Calculate(destination!, from!.Value, to!.Value,
            _cache.GetPropagatedObjects(), maxWindows: effectiveTopN);
        return Ok(new { destination, from, to, windows = result });
    }

    private IActionResult? ValidateRange(string? destination, DateTime? from, DateTime? to)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return BadRequest(ApiErrorDto.Make("MISSING_PARAMETER", "destination is required."));
        if (!from.HasValue || !to.HasValue)
            return BadRequest(ApiErrorDto.Make("MISSING_PARAMETER", "from and to are required."));
        if (from.Value >= to.Value)
            return BadRequest(ApiErrorDto.Make("INVALID_RANGE", "from must be earlier than to."));
        if ((to.Value - from.Value).TotalDays > MaxRangeDays)
            return BadRequest(ApiErrorDto.Make("INVALID_RANGE", $"Range cannot exceed {MaxRangeDays} days."));
        return null;
    }
}
```

#### Task 4.2: MissionController

- [ ] Create `MissionClear.Api/Controllers/MissionController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Dtos;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Missions;
using MissionClear.Api.Services.Orbital;
using MissionClear.Api.Services.Sessions;
using MissionClear.Api.Services.Streaming;

namespace MissionClear.Api.Controllers;

[Route("api/mission")]
public sealed class MissionController : BaseApiController
{
    private readonly IMissionSimulationService _simulation;
    private readonly SessionStore _sessions;
    private readonly MissionSseService _sse;
    private readonly OrbitalCache _cache;
    private readonly IMissionHistoryService _history;

    public MissionController(
        IMissionSimulationService simulation,
        SessionStore sessions,
        MissionSseService sse,
        OrbitalCache cache,
        IMissionHistoryService history)
    {
        _simulation = simulation;
        _sessions = sessions;
        _sse = sse;
        _cache = cache;
        _history = history;
    }

    [HttpPost("simulate")]
    public IActionResult Simulate([FromBody] MissionSimulateRequest request)
    {
        if (!_cache.IsReady) return StatusCode(503, ApiErrorDto.CacheNotReady());
        if (request is null || string.IsNullOrWhiteSpace(request.DestinationId))
            return BadRequest(ApiErrorDto.Make("MISSING_PARAMETER", "destination is required."));

        try
        {
            var result = _simulation.Simulate(request, _cache.GetPropagatedObjects());
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(ApiErrorDto.Make("DESTINATION_NOT_FOUND", ex.Message));
        }
        catch (DomainException ex) { return DomainError(ex); }
    }

    [HttpPost("session")]
    public IActionResult CreateSession([FromBody] SessionRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.DestinationId))
            return BadRequest(ApiErrorDto.Make("MISSING_PARAMETER", "destination is required."));

        var session = _sessions.CreateSession(GetUserId(), request);
        var streamUrl = $"/api/mission/session/{session.SessionId}/stream";
        return Ok(new { session_id = session.SessionId, stream_url = streamUrl, created_at = session.CreatedAtUtc });
    }

    [HttpGet("session/{id}/stream")]
    public async Task Stream(string id, CancellationToken ct)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var debris = _cache.IsReady ? _cache.GetPropagatedObjects() : Array.Empty<OrbitalObject>();
        await _sse.StreamAsync(id, Response, debris, ct);
    }

    [HttpPost("session/{id}/complete")]
    public async Task<IActionResult> Complete(string id, [FromBody] SessionCompleteRequest request, CancellationToken ct)
    {
        try
        {
            var userId = GetUserId();
            string? missionId = null;

            if (request?.SaveToHistory == true && userId is not null)
            {
                var session = _sessions.GetSession(id);
                if (session is not null)
                {
                    var saved = await _history.SaveMissionAsync(
                        userId, id,
                        session.Status,
                        session.FinalRiskScore,
                        session.DeltaVKmS,
                        session.ObstaclesEncountered,
                        "[]",
                        session.DestinationId ?? "ISS",
                        session.CreatedAtUtc,
                        session.CompletedAtUtc ?? DateTime.UtcNow,
                        ct);
                    missionId = saved.Id;
                }
            }

            _sessions.TryCompleteSession(id, Domain.SessionStatus.Success, 0, 0, 0, 0, 0);

            return Ok(new { session_id = id, saved = missionId is not null, mission_id = missionId });
        }
        catch (DomainException ex) { return DomainError(ex); }
    }
}
```

- [ ] Commit: `feat(controllers): launch-windows + mission (simulate + sse)`

---

### Phase 5: History & Dashboard

#### Task 5.1: MissionsController (history)

- [ ] Create `MissionClear.Api/Controllers/MissionsController.cs`

**CRITICAL:** `[HttpGet("stats")]` MUST be declared BEFORE `[HttpGet("{id}")]` to prevent "stats" being matched as an id.

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Dtos;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;

namespace MissionClear.Api.Controllers;

[Authorize]
[Route("api/missions")]
public sealed class MissionsController : BaseApiController
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    private readonly IMissionHistoryService _history;

    public MissionsController(IMissionHistoryService history) => _history = history;

    // IMPORTANT: BEFORE {id} route — "stats" must never be matched as an id
    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized(ApiErrorDto.Make("UNAUTHORIZED", "Missing user claim."));
        try
        {
            var stats = await _history.GetStatsAsync(userId, ct);
            return Ok(stats);
        }
        catch (DomainException ex) { return DomainError(ex); }
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? limit,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized(ApiErrorDto.Make("UNAUTHORIZED", "Missing user claim."));

        var effectivePage = Math.Max(page ?? 1, 1);
        var effectiveLimit = Math.Min(Math.Max(limit ?? DefaultLimit, 1), MaxLimit);

        try
        {
            var result = await _history.GetMissionsAsync(userId, effectivePage, effectiveLimit, status, ct);
            return Ok(result);
        }
        catch (DomainException ex) { return DomainError(ex); }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized(ApiErrorDto.Make("UNAUTHORIZED", "Missing user claim."));
        try
        {
            var mission = await _history.GetMissionAsync(userId, id, ct);
            return Ok(mission);
        }
        catch (DomainException ex) { return DomainError(ex); }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized(ApiErrorDto.Make("UNAUTHORIZED", "Missing user claim."));
        try
        {
            await _history.DeleteMissionAsync(userId, id, ct);
            return NoContent();
        }
        catch (DomainException ex) { return DomainError(ex); }
    }
}
```

#### Task 5.2: DashboardController

- [ ] Create `MissionClear.Api/Controllers/DashboardController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using MissionClear.Api.Dtos;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Orbital;

namespace MissionClear.Api.Controllers;

[Route("api/dashboard")]
public sealed class DashboardController : BaseApiController
{
    private const int DefaultWindowHours = 24;
    private const int MaxWindowHours = 72;

    private readonly IDashboardService _dashboard;
    private readonly OrbitalCache _cache;
    private readonly IUserService _users;

    public DashboardController(IDashboardService dashboard, OrbitalCache cache, IUserService users)
    {
        _dashboard = dashboard;
        _cache = cache;
        _users = users;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        if (!_cache.IsReady) return StatusCode(503, ApiErrorDto.CacheNotReady());

        var userId = GetUserId();
        UserResponse? userDto = null;
        if (userId is not null)
        {
            try { userDto = await _users.GetMeAsync(userId, ct); }
            catch (DomainException) { /* anonymous fallback */ }
        }

        var debris = _cache.GetPropagatedObjects();
        var result = _dashboard.GetSummary(userId, userDto, debris);
        return Ok(result);
    }

    [HttpGet("alerts")]
    public IActionResult Alerts([FromQuery(Name = "window_hours")] int? windowHours)
    {
        if (!_cache.IsReady) return StatusCode(503, ApiErrorDto.CacheNotReady());

        var effective = Math.Min(Math.Max(windowHours ?? DefaultWindowHours, 1), MaxWindowHours);
        var debris = _cache.GetPropagatedObjects();
        var alerts = _dashboard.GetAlerts(debris, effective);
        return Ok(alerts);
    }
}
```

- [ ] Commit: `feat(controllers): missions history + dashboard endpoints`

---

### Phase 6: Integration Tests

#### Task 6.1: Add NuGet packages to MissionClear.Tests

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.10" />
```

#### Task 6.2: TestWebApplicationFactory

- [ ] Create `MissionClear.Tests/Controllers/TestWebApplicationFactory.cs`

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MissionClear.Api.Data;

namespace MissionClear.Tests.Controllers;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]             = "test-secret-key-with-at-least-32-characters-long",
                ["Jwt:Issuer"]             = "mission-clear-api-test",
                ["Jwt:Audience"]           = "mission-clear-mobile-test",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"]   = "7",
                ["KeepTrack:ApiKey"]       = "",
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase($"TestDb-{Guid.NewGuid()}"));
        });
    }
}
```

#### Task 6.3: AuthControllerTests

- [ ] Create `MissionClear.Tests/Controllers/AuthControllerTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MissionClear.Api.Dtos;
using Xunit;

namespace MissionClear.Tests.Controllers;

public sealed class AuthControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public AuthControllerTests(TestWebApplicationFactory factory) => _factory = factory;

    private static AuthRegisterRequest NewUser(string email = "pilot@test.com") =>
        new(email, "StrongPass23!", "Test Pilot");

    [Fact]
    public async Task Register_WithValidData_Returns201AndAccessToken()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", NewUser("new1@test.com"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409()
    {
        var client = _factory.CreateClient();
        var payload = NewUser("dup@test.com");

        await client.PostAsJsonAsync("/api/auth/register", payload);
        var second = await client.PostAsJsonAsync("/api/auth/register", payload);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_WithWeakPassword_Returns400()
    {
        var client = _factory.CreateClient();
        var weak = new AuthRegisterRequest("weak@test.com", "123", "Weak");
        var response = await client.PostAsJsonAsync("/api/auth/register", weak);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_Returns200()
    {
        var client = _factory.CreateClient();
        var payload = NewUser("login@test.com");
        await client.PostAsJsonAsync("/api/auth/register", payload);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new AuthLoginRequest(payload.Email, payload.Password));

        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var payload = NewUser("wrong@test.com");
        await client.PostAsJsonAsync("/api/auth/register", payload);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new AuthLoginRequest(payload.Email, "WrongPass9!"));

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_Returns200()
    {
        var client = _factory.CreateClient();
        var payload = NewUser("refresh@test.com");
        var reg = await client.PostAsJsonAsync("/api/auth/register", payload);
        var auth = await reg.Content.ReadFromJsonAsync<AuthResponse>();

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new AuthRefreshRequest(auth!.RefreshToken));

        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await refresh.Content.ReadFromJsonAsync<AuthResponse>();
        refreshed!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Logout_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithAuth_Returns204()
    {
        var client = _factory.CreateClient();
        var payload = NewUser("logout@test.com");
        var reg = await client.PostAsJsonAsync("/api/auth/register", payload);
        var auth = await reg.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var response = await client.PostAsync("/api/auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
```

#### Task 6.4: DebrisControllerTests

- [ ] Create `MissionClear.Tests/Controllers/DebrisControllerTests.cs`

```csharp
using System.Net;
using FluentAssertions;
using Xunit;

namespace MissionClear.Tests.Controllers;

public sealed class DebrisControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public DebrisControllerTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetDebris_WhenCacheNotReady_Returns503()
    {
        // Test factory starts with empty cache → IsReady = false
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/debris");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetStatus_AlwaysReturns200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDestinations_Returns200WithList()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/destinations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

#### Task 6.5: MissionsControllerTests

- [ ] Create `MissionClear.Tests/Controllers/MissionsControllerTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MissionClear.Api.Dtos;
using Xunit;

namespace MissionClear.Tests.Controllers;

public sealed class MissionsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public MissionsControllerTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ListMissions_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/missions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListMissions_WithToken_Returns200WithPagination()
    {
        var client = _factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new AuthRegisterRequest("history@test.com", "StrongPass23!", "History Tester"));
        var auth = await reg.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var response = await client.GetAsync("/api/missions?page=1&limit=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMissionStats_WithToken_Returns200()
    {
        var client = _factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new AuthRegisterRequest("stats@test.com", "StrongPass23!", "Stats Tester"));
        var auth = await reg.Content.ReadFromJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var response = await client.GetAsync("/api/missions/stats");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] Commit: `test(controllers): integration tests for auth + debris + missions`

---

### Phase 7: Final Wiring & Verification

#### Task 7.1: Program.cs final state

```csharp
// Critical ordering:
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors("MobileApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
```

- [ ] Expose `public partial class Program {}` at end of `Program.cs` for `WebApplicationFactory<Program>`
- [ ] Verify CORS allows `*` in Development, mobile origin in Production
- [ ] `dotnet build` — 0 warnings, 0 errors
- [ ] `dotnet test` — all tests green

- [ ] Commit: `chore(api): final wiring + smoke verification`

---

## Testing Strategy

- **Unit tests:** covered in plans 03-06 (services)
- **Integration tests:** `WebApplicationFactory` + InMemory DB — auth flow, protected routes, 503 on cache empty
- **Manual smoke tests:** curl against running API (status, register, login, debris)
- **Coverage target:** 80%+ on Services; controllers validated via integration

## Risks & Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Route `/missions/stats` matched as id | HIGH | Declare `[HttpGet("stats")]` BEFORE `[HttpGet("{id}")]` in controller source |
| SSE buffered by reverse proxy | MEDIUM | Set `X-Accel-Buffering: no` header |
| Global middleware swallowing errors in dev | MEDIUM | Branch on `IHostEnvironment.IsProduction()` |
| InMemory DB doesn't enforce unique constraints | MEDIUM | Duplicate email check in service layer |
| `Program` not visible to test project | HIGH | Add `public partial class Program {}` at bottom of `Program.cs` |
| Claim name mismatch (`sub` vs `NameIdentifier`) | HIGH | `GetUserId()` checks both |
| CORS misconfig blocks Mobile | MEDIUM | Validate `CorsAllowedOrigins` at startup; fail fast if empty in Production |

## Success Criteria

- [ ] All 23 routes implemented and reachable
- [ ] Zero business logic in controllers
- [ ] All controllers extend `BaseApiController`
- [ ] `DomainException` handled uniformly via `DomainError()` helper
- [ ] `GlobalExceptionMiddleware` registered and tested
- [ ] `[Authorize]` enforced on all protected routes
- [ ] `/api/missions/stats` resolves before `/api/missions/{id}`
- [ ] SSE endpoint sets correct headers and delegates to `MissionSseService`
- [ ] Integration tests pass: 14+ scenarios
- [ ] No stack traces in production responses
- [ ] `dotnet build` clean (zero warnings)
- [ ] `dotnet test` green
- [ ] 6 commits with conventional format

---

**This is the final plan. Plans 00-07 fully cover the Mission Clear backend.**
