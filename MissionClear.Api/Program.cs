using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MissionClear.Api.Configuration;
using MissionClear.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire: OpenTelemetry, health checks, service discovery ─────────────────
builder.AddServiceDefaults();

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

// ── Database (MySQL via Aspire service discovery) ───────────────────────────
// "missionclear" = nome do database registrado no AppHost
// Quando rodando via AppHost: connection string injetada automaticamente
// Quando rodando stand-alone: lê ConnectionStrings:missionclear do appsettings
builder.AddMySqlDbContext<MissionClear.Api.Data.AppDbContext>("missionclear");

// ── HTTP clients ─────────────────────────────────────────────────────────────
builder.Services.AddHttpClient();

// ── CORS ────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(opt => opt.AddPolicy("MobileApp", policy =>
{
    if (allowedOrigins.Contains("*"))
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

// ── Authentication ──────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            // "sub" permanece como "sub" — sem remapeamento para NameIdentifier
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// ── DI registrations (preenchido nas fases seguintes) ───────────────────────
// builder.Services.AddScoped<IUserRepository, UserRepository>();
// builder.Services.AddScoped<IAuthService, AuthService>();
// ... demais serviços adicionados nos planos 01–07

var app = builder.Build();

app.UseMiddleware<MissionClear.Api.Middleware.GlobalExceptionMiddleware>();
app.UseCors("MobileApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints(); // /health + /alive (dev only)

app.Run();

// Required for WebApplicationFactory<Program> in integration tests
public partial class Program { }
