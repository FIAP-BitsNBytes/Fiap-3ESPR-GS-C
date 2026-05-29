using MissionClear.Api.Configuration;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Middleware;
using MissionClear.Api.Services;
using MissionClear.Api.Services.Background;
using MissionClear.Api.Services.Interfaces;
using MissionClear.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Hosting;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.AddMySqlDbContext<AppDbContext>("missionclear");
}

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
        options.JsonSerializerOptions.PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.WriteIndented               = false;
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
if (!app.Environment.IsEnvironment("Testing"))
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
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