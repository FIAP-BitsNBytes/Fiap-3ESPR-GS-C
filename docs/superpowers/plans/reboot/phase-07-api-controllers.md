# Phase 07 — API Controllers + Authorization

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Implementar todos os 9 controllers com autorização por Role. Atualizar Program.cs com DI completo. Testes de integração com WebApplicationFactory.

**Regras críticas de autorização:**
- `DELETE /api/missions/{id}` — `[Authorize(Roles = "Administrator")]`
- `DELETE /api/users/me` (se existir) — requer auth
- Rotas públicas: `/api/status`, `/api/debris`, `/api/destinations`, `/api/launch-windows`, `/api/mission/simulate`, `/api/mission/session`
- Rotas autenticadas: `/api/users/me`, `/api/missions`, `/api/dashboard/summary` (opcional auth)

**CRÍTICO:** `/api/missions/stats` DEVE ser declarado ANTES de `/api/missions/{id}` no controller.

---

### Task 1: Program.cs Final (DI completo + Aspire)

**Files:**
- Modify: `MissionClear.Api/Program.cs`

- [ ] **Step 1: Substituir Program.cs completo**

```csharp
using MissionClear.Api.Configuration;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Middleware;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Interfaces;
using MissionClear.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Aspire service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Startup guard
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
if (jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret must be at least 32 characters.");

// Typed config
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<OrbitalSettings>(builder.Configuration.GetSection(OrbitalSettings.SectionName));
builder.Services.Configure<ExternalApiSettings>(builder.Configuration.GetSection(ExternalApiSettings.SectionName));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection(CorsSettings.SectionName));

// MySQL via Aspire Pomelo integration
builder.AddMySqlDbContext<AppDbContext>("missionclear");

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileApp", policy =>
    {
        if (builder.Environment.IsDevelopment())
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        else
        {
            var cors = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>();
            policy.WithOrigins(cors?.AllowedOrigins ?? []).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

// JWT Bearer Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Repositories (Scoped — EF Core DbContext lifecycle)
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IMissionRepository, MissionRepository>();

// Services
builder.Services.AddSingleton<IOrbitalCache, OrbitalCache>();
builder.Services.AddScoped<IDataAggregatorService, DataAggregatorService>();
builder.Services.AddScoped<IOrbitalEngineService, OrbitalEngineService>();
builder.Services.AddScoped<IConjunctionDetector, ConjunctionDetector>();
builder.Services.AddScoped<ILaunchWindowCalculator, LaunchWindowCalculator>();
builder.Services.AddScoped<IMissionSimulationService, MissionSimulationService>();
builder.Services.AddSingleton<ISessionStore, SessionStore>();
builder.Services.AddScoped<IMissionSseService, MissionSseService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IMissionHistoryService, MissionHistoryService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

// Background services
builder.Services.AddHostedService<TleIngestionService>();

var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseCors("MobileApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints(); // Aspire health endpoints

app.Run();

// Needed for WebApplicationFactory in tests
public partial class Program { }
```

---

### Task 2: BaseApiController + GlobalExceptionMiddleware

**Files:**
- Create: `MissionClear.Api/Controllers/BaseApiController.cs`
- Verify: `MissionClear.Api/Middleware/GlobalExceptionMiddleware.cs` (should already exist)

- [ ] **Step 1: Criar BaseApiController.cs**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    protected Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub"), out var id)
            ? id : Guid.Empty;

    protected bool IsAuthenticated => User.Identity?.IsAuthenticated == true;
}
```

- [ ] **Step 2: Verificar/atualizar GlobalExceptionMiddleware.cs**

O middleware deve capturar `DomainException` e retornar o shape correto. Se o arquivo já existe mas não trata `DomainException`, atualizar:

```csharp
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Exceptions;
using System.Text.Json;

namespace MissionClear.Api.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
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
            logger.LogWarning("Domain error {Code}: {Message}", ex.ErrorCode, ex.Message);
            context.Response.StatusCode = ex.HttpStatus;
            context.Response.ContentType = "application/json";
            var error = new ApiErrorDto(ex.ErrorCode, ex.Message, DateTime.UtcNow.ToString("O"));
            await context.Response.WriteAsync(JsonSerializer.Serialize(error, JsonOptions));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error");
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var error = new ApiErrorDto("INTERNAL_ERROR", "An unexpected error occurred.", DateTime.UtcNow.ToString("O"));
            await context.Response.WriteAsync(JsonSerializer.Serialize(error, JsonOptions));
        }
    }
}
```

---

### Task 3: AuthController

**Files:**
- Create: `MissionClear.Api/Controllers/AuthController.cs`

- [ ] **Step 1: Criar AuthController.cs**

```csharp
using MissionClear.Api.Dtos.Auth;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class AuthController(IAuthService authService) : BaseApiController
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await authService.RegisterAsync(request, ct);
        return CreatedAtAction(nameof(Register), result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await authService.RefreshAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request, ct);
        return NoContent();
    }
}
```

---

### Task 4: UsersController

**Files:**
- Create: `MissionClear.Api/Controllers/UsersController.cs`

- [ ] **Step 1: Criar UsersController.cs**

```csharp
using MissionClear.Api.Dtos.User;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Authorize]
public sealed class UsersController(IUserService userService) : BaseApiController
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var profile = await userService.GetProfileAsync(CurrentUserId, ct);
        return Ok(profile);
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(
        [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        var profile = await userService.UpdateProfileAsync(CurrentUserId, request, ct);
        return Ok(profile);
    }
}
```

---

### Task 5: StatusController + DestinationsController

**Files:**
- Create: `MissionClear.Api/Controllers/StatusController.cs`
- Create: `MissionClear.Api/Controllers/DestinationsController.cs`

- [ ] **Step 1: Criar StatusController.cs**

```csharp
using MissionClear.Api.Dtos.Status;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class StatusController(IOrbitalCache cache) : BaseApiController
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    [HttpGet]
    public IActionResult Get()
    {
        var status = cache.IsReady ? "ready" : "loading";
        return Ok(new StatusResponse(
            status,
            cache.Count,
            cache.Count,
            cache.LastFetch?.ToString("O"),
            cache.LastPropagation?.ToString("O"),
            (long)(DateTime.UtcNow - StartTime).TotalSeconds,
            new SourceStatusDto("ok", "unavailable")));
    }
}
```

- [ ] **Step 2: Criar DestinationsController.cs**

```csharp
using MissionClear.Api.Dtos.Destination;
using MissionClear.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class DestinationsController : BaseApiController
{
    [HttpGet]
    public IActionResult Get()
    {
        var dtos = KnownDestinations.All.Select(d => new DestinationDto(
            d.Id, d.DisplayName, d.AltitudeKm, d.InclinationDeg,
            d.Description, d.DeltaVKmS, d.MissionDurationHours, d.Icon)).ToList();
        return Ok(new DestinationsResponse(dtos));
    }
}
```

---

### Task 6: DebrisController

**Files:**
- Create: `MissionClear.Api/Controllers/DebrisController.cs`

- [ ] **Step 1: Criar DebrisController.cs**

```csharp
using MissionClear.Api.Dtos.Orbital;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class DebrisController(IOrbitalCache cache) : BaseApiController
{
    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] double altitudeMinKm = 200,
        [FromQuery] double altitudeMaxKm = 2000,
        [FromQuery] string? type = null,
        [FromQuery] int limit = 500)
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);

        var query = cache.GetAll()
            .Where(o => o.AltitudeKm >= altitudeMinKm && o.AltitudeKm <= altitudeMaxKm);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(o => o.Type == type);

        var result = query.Take(Math.Min(limit, 2000))
            .Select(o => new DebrisDto(o.Id, o.Name, o.Type, o.Latitude, o.Longitude,
                o.AltitudeKm, o.VelocityKmS, o.Source, o.UpdatedAt.ToString("O")))
            .ToList();

        Response.Headers.CacheControl = "max-age=60";
        return Ok(result);
    }

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
                case "debris": debris++; break;
                case "satellite": satellite++; break;
                case "rocket_body": rocket++; break;
            }
            if (o.AltitudeKm < 500) low++;
            else if (o.AltitudeKm < 1000) mid++;
            else high++;
        }

        return Ok(new DebrisStatsDto(
            all.Count,
            new ByTypeDto(debris, satellite, rocket),
            new ByAltitudeBandDto(low, mid, high),
            new SourcesDto(all.Count(o => o.Source == "celestrak"), all.Count(o => o.Source == "keeptrack")),
            (cache.LastPropagation ?? DateTime.UtcNow).ToString("O")));
    }

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
                obj.InclinationDeg.Value, obj.Eccentricity ?? 0,
                obj.PeriodMinutes ?? 0, obj.ApogeeKm ?? 0, obj.PerigeeKm ?? 0);

        return Ok(new DebrisDetailDto(obj.Id, obj.Name, obj.Type, obj.Latitude, obj.Longitude,
            obj.AltitudeKm, obj.VelocityKmS, obj.Source, obj.UpdatedAt.ToString("O"), tle, orbit));
    }
}
```

---

### Task 7: LaunchWindowsController

**Files:**
- Create: `MissionClear.Api/Controllers/LaunchWindowsController.cs`

- [ ] **Step 1: Criar LaunchWindowsController.cs**

```csharp
using MissionClear.Api.Dtos.Common;
using MissionClear.Api.Exceptions;
using MissionClear.Api.Models;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class LaunchWindowsController(
    ILaunchWindowCalculator calculator,
    IOrbitalCache cache) : BaseApiController
{
    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] string? destination,
        [FromQuery] string? from,
        [FromQuery] string? to)
    {
        ValidateParams(destination, from, to, out var dest, out var fromDt, out var toDt);
        var debris = GetDebris();
        var windows = calculator.Calculate(dest, fromDt, toDt, debris);

        var dtos = windows.Select(w => new LaunchWindowDto(
            w.Start.ToString("O"), w.End.ToString("O"),
            w.RiskScore, w.DeltaVKmS, w.DurationHours, w.IsRecommended,
            w.Conjunctions.Select(c => new ConjunctionDto(
                c.DebrisId, c.DebrisName,
                c.ClosestApproachKm, c.TimeOfClosestApproach.ToString("O"),
                c.RiskLevel.ToString().ToLowerInvariant())).ToList())).ToList();

        return Ok(new
        {
            destination = dest.DisplayName,
            from = fromDt.ToString("O"),
            to = toDt.ToString("O"),
            total_windows = dtos.Count,
            safe_windows = dtos.Count(w => w.IsRecommended),
            windows = dtos
        });
    }

    [HttpGet("best")]
    public IActionResult GetBest(
        [FromQuery] string? destination,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int count = 5,
        [FromQuery] double maxRisk = 0.3)
    {
        ValidateParams(destination, from, to, out var dest, out var fromDt, out var toDt);
        var debris = GetDebris();
        var windows = calculator.Calculate(dest, fromDt, toDt, debris);

        var best = windows
            .Where(w => w.RiskScore <= maxRisk)
            .OrderBy(w => w.RiskScore)
            .Take(Math.Min(count, 20))
            .Select((w, i) => new BestWindowDto(
                i + 1, w.Start.ToString("O"), w.End.ToString("O"),
                w.RiskScore, w.DeltaVKmS, w.DurationHours,
                w.Conjunctions.Select(c => new ConjunctionDto(
                    c.DebrisId, c.DebrisName,
                    c.ClosestApproachKm, c.TimeOfClosestApproach.ToString("O"),
                    c.RiskLevel.ToString().ToLowerInvariant())).ToList()))
            .ToList();

        return Ok(new
        {
            destination = dest.DisplayName,
            from = fromDt.ToString("O"),
            to = toDt.ToString("O"),
            best_windows = best
        });
    }

    private void ValidateParams(string? dest, string? from, string? to,
        out MissionDestination destination, out DateTime fromDt, out DateTime toDt)
    {
        if (string.IsNullOrEmpty(dest))
            throw new DomainException("MISSING_PARAMETER", "'destination' is required.", 400);
        if (string.IsNullOrEmpty(from))
            throw new DomainException("MISSING_PARAMETER", "'from' is required.", 400);
        if (string.IsNullOrEmpty(to))
            throw new DomainException("MISSING_PARAMETER", "'to' is required.", 400);

        destination = KnownDestinations.FindById(dest)
            ?? throw new DomainException("INVALID_DESTINATION", $"Unknown destination: {dest}", 400);

        if (!DateTime.TryParse(from, out fromDt))
            throw new DomainException("INVALID_DATE_FORMAT", "'from' is not a valid ISO 8601 date.", 400);
        if (!DateTime.TryParse(to, out toDt))
            throw new DomainException("INVALID_DATE_FORMAT", "'to' is not a valid ISO 8601 date.", 400);

        fromDt = fromDt.ToUniversalTime();
        toDt = toDt.ToUniversalTime();

        if ((toDt - fromDt).TotalHours > 48)
            throw new DomainException("TIME_RANGE_EXCEEDED", "Range cannot exceed 48 hours.", 400);
    }

    private IReadOnlyList<OrbitalObject> GetDebris()
    {
        if (!cache.IsReady)
            throw new DomainException("CACHE_NOT_READY", "Orbital data is still loading.", 503);
        return cache.GetAll();
    }
}
```

---

### Task 8: MissionController (simulate + session)

**Files:**
- Create: `MissionClear.Api/Controllers/MissionController.cs`

- [ ] **Step 1: Criar MissionController.cs**

```csharp
using MissionClear.Api.Dtos.Mission;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class MissionController(IMissionSimulationService simulation) : BaseApiController
{
    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(
        [FromBody] SimulateRequest request, CancellationToken ct)
    {
        var result = await simulation.SimulateAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("session")]
    public async Task<IActionResult> CreateSession(
        [FromBody] SessionRequest request, CancellationToken ct)
    {
        var result = await simulation.CreateSessionAsync(request, ct);
        return CreatedAtAction(nameof(CreateSession), result);
    }

    [HttpGet("session/{sessionId}/stream")]
    public async Task StreamSession(string sessionId, IMissionSseService sseService, CancellationToken ct)
    {
        await sseService.StreamAsync(sessionId, Response, ct);
    }

    [HttpPost("session/{sessionId}/complete")]
    public async Task<IActionResult> CompleteSession(
        string sessionId,
        [FromBody] CompleteSessionRequest request, CancellationToken ct)
    {
        var userId = IsAuthenticated ? CurrentUserId : (Guid?)null;
        var result = await simulation.CompleteSessionAsync(sessionId, request, userId, ct);
        return Ok(result);
    }
}
```

---

### Task 9: MissionsController (histórico — autorização por Role)

**Files:**
- Create: `MissionClear.Api/Controllers/MissionsController.cs`

**CRÍTICO:** `stats` antes de `{id}`.

- [ ] **Step 1: Criar MissionsController.cs**

```csharp
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Authorize]
public sealed class MissionsController(IMissionHistoryService historyService) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? destination = null,
        [FromQuery] string sort = "created_at_desc",
        CancellationToken ct = default)
    {
        limit = Math.Min(limit, 50);
        var result = await historyService.GetMissionsAsync(
            CurrentUserId, page, limit, status, destination, sort, ct);
        return Ok(result);
    }

    // MUST be before {id} route
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var result = await historyService.GetStatsAsync(CurrentUserId, ct);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id.Replace("msn_", ""), out var guid))
            return BadRequest(new { error = "INVALID_ID", message = "Invalid mission ID format." });

        var result = await historyService.GetMissionDetailAsync(guid, CurrentUserId, ct);
        return Ok(result);
    }

    // DELETE requires Administrator role
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

---

### Task 10: DashboardController

**Files:**
- Create: `MissionClear.Api/Controllers/DashboardController.cs`

- [ ] **Step 1: Criar DashboardController.cs**

```csharp
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

public sealed class DashboardController(IDashboardService dashboardService) : BaseApiController
{
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var userId = IsAuthenticated ? CurrentUserId : (Guid?)null;
        var result = await dashboardService.GetSummaryAsync(userId, ct);
        return Ok(result);
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] int windowHours = 6,
        [FromQuery] string minRisk = "medium",
        CancellationToken ct = default)
    {
        windowHours = Math.Min(windowHours, 24);
        var result = await dashboardService.GetAlertsAsync(windowHours, minRisk, ct);
        return Ok(result);
    }
}
```

---

### Task 11: Integration Tests

**Files:**
- Create: `MissionClear.Tests/Integration/TestWebApplicationFactory.cs`
- Create: `MissionClear.Tests/Integration/AuthEndpointTests.cs`
- Create: `MissionClear.Tests/Integration/DebrisEndpointTests.cs`
- Create: `MissionClear.Tests/Integration/MissionsAuthorizationTests.cs`

- [ ] **Step 1: TestWebApplicationFactory.cs**

```csharp
using MissionClear.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MissionClear.Tests.Integration;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace MySQL with InMemory for tests
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid()));
        });

        builder.UseEnvironment("Testing");
    }
}
```

- [ ] **Step 2: AuthEndpointTests.cs**

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MissionClear.Api.Dtos.Auth;

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
            email = "new@test.com",
            password = "Test@Pass1",
            display_name = "New User"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        body!.User.Role.Should().Be("Researcher");
    }

    [Fact]
    public async Task Register_Returns409_WhenEmailDuplicate()
    {
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "dup@test.com", password = "Test@Pass1", display_name = "Dup"
        });

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "dup@test.com", password = "Test@Pass1", display_name = "Dup2"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_Returns401_WithWrongPassword()
    {
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "login@test.com", password = "Test@Pass1", display_name = "Login"
        });

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "login@test.com", password = "Wrong@Pass1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 3: MissionsAuthorizationTests.cs**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MissionClear.Api.Dtos.Auth;

namespace MissionClear.Tests.Integration;

public sealed class MissionsAuthorizationTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> GetTokenAsync(string role = "Researcher")
    {
        var email = $"{Guid.NewGuid():N}@test.com";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email, password = "Test@Pass1", display_name = "User", role
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
        var token = await GetTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/missions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteMission_Returns403_ForResearcher()
    {
        var token = await GetTokenAsync("Researcher");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.DeleteAsync("/api/missions/msn_00000000000000000000000000000001");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetStats_Returns200_WithValidToken()
    {
        var token = await GetTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/missions/stats");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 4: Rodar integration tests**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "Integration" -v normal
```

- [ ] **Step 5: Build completo**

```powershell
dotnet build MissionClear.sln
dotnet test MissionClear.Tests/MissionClear.Tests.csproj -v normal
```

- [ ] **Step 6: Commit**

```powershell
git add MissionClear.Api/Controllers/ MissionClear.Api/Program.cs MissionClear.Tests/Integration/
git commit -m "feat(controllers): all 9 controllers, JWT role auth, Administrator-only DELETE"
```
