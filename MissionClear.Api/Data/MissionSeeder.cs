using System.Text.Json;
using MissionClear.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace MissionClear.Api.Data;

/// <summary>
/// Garante usuários de demonstração e histórico de missões no banco.
/// Executado uma vez no startup, antes da aplicação aceitar requisições.
/// </summary>
public static class MissionSeeder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task SeedAsync(AppDbContext db, ILogger logger)
    {
        var demoUserId  = await EnsureUserAsync(db, logger,
            email:       "demo@missionclear.app",
            displayName: "Piloto Demo",
            password:    "Demo@123456",
            role:        "Researcher");

        await EnsureUserAsync(db, logger,
            email:       "admin@missionclear.app",
            displayName: "Administrador",
            password:    "Admin@123456",
            role:        "Administrator");

        await SeedMissionsAsync(db, logger, demoUserId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<Guid> EnsureUserAsync(
        AppDbContext db, ILogger logger,
        string email, string displayName, string password, string role)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
            return existing.Id;

        var entity = new UserEntity
        {
            Email        = email,
            DisplayName  = displayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            Role         = role,
            CreatedAt    = DateTime.UtcNow
        };
        db.Users.Add(entity);
        await db.SaveChangesAsync();
        logger.LogInformation("[Seeder] Usuário criado: {Email} ({Role})", email, role);
        return entity.Id;
    }

    private static async Task SeedMissionsAsync(AppDbContext db, ILogger logger, Guid userId)
    {
        if (await db.Missions.AnyAsync(m => m.UserId == userId))
            return;

        var now    = DateTime.UtcNow;
        var rng    = new Random(7);
        var batch  = BuildMissions(userId, now, rng);

        db.Missions.AddRange(batch);
        await db.SaveChangesAsync();
        logger.LogInformation("[Seeder] {Count} missões de demonstração inseridas.", batch.Count);
    }

    private static List<MissionEntity> BuildMissions(Guid userId, DateTime now, Random rng)
    {
        // Obstáculos pré-definidos por destino (realistas mas ficcionais)
        var issObstacles = ObstaclesJson(
            ("COSMOS 1408 DEB", 12.4, "2026-05-10T08:22:00Z", "high"),
            ("SL-8 R/B DEB",    38.1, "2026-05-10T08:51:00Z", "medium"),
            ("IRIDIUM 33 DEB",  61.7, "2026-05-10T09:18:00Z", "low"));

        var leoObstacles = ObstaclesJson(
            ("FENGYUN 1C DEB",  8.2, "2026-05-12T14:05:00Z", "critical"),
            ("BREEZE-M DEB",   22.9, "2026-05-12T14:33:00Z", "high"));

        var ssoObstacles = ObstaclesJson(
            ("RESURS-1 DEB",   15.6, "2026-05-14T03:11:00Z", "medium"));

        return
        [
            Mission(userId, "ISS",        "success", score:892, risk:0.18, dv:9.38, dep:now.AddDays(-28), durHours:6.2f, obstacles:issObstacles, rng),
            Mission(userId, "LEO_GENERIC","success", score:810, risk:0.24, dv:9.21, dep:now.AddDays(-25), durHours:5.8f, obstacles:leoObstacles, rng),
            Mission(userId, "SSO",        "success", score:955, risk:0.09, dv:10.08,dep:now.AddDays(-22), durHours:7.0f, obstacles:"[]",          rng),
            Mission(userId, "ISS",        "failure", score:340, risk:0.74, dv:9.41, dep:now.AddDays(-20), durHours:2.1f, obstacles:issObstacles,  rng),
            Mission(userId, "LEO_GENERIC","success", score:778, risk:0.31, dv:9.19, dep:now.AddDays(-18), durHours:5.8f, obstacles:leoObstacles,  rng),
            Mission(userId, "SSO",        "aborted", score:210, risk:0.62, dv:10.12,dep:now.AddDays(-16), durHours:1.4f, obstacles:ssoObstacles,  rng),
            Mission(userId, "ISS",        "success", score:921, risk:0.14, dv:9.40, dep:now.AddDays(-14), durHours:6.2f, obstacles:"[]",          rng),
            Mission(userId, "LEO_GENERIC","success", score:863, risk:0.19, dv:9.22, dep:now.AddDays(-12), durHours:5.8f, obstacles:leoObstacles,  rng),
            Mission(userId, "ISS",        "failure", score:290, risk:0.81, dv:9.39, dep:now.AddDays(-10), durHours:1.8f, obstacles:issObstacles,  rng),
            Mission(userId, "SSO",        "success", score:908, risk:0.11, dv:10.09,dep:now.AddDays(-8),  durHours:7.0f, obstacles:"[]",          rng),
            Mission(userId, "LEO_GENERIC","aborted", score:175, risk:0.55, dv:9.20, dep:now.AddDays(-6),  durHours:0.9f, obstacles:leoObstacles,  rng),
            Mission(userId, "ISS",        "success", score:947, risk:0.08, dv:9.42, dep:now.AddDays(-5),  durHours:6.2f, obstacles:"[]",          rng),
            Mission(userId, "SSO",        "success", score:879, risk:0.16, dv:10.11,dep:now.AddDays(-3),  durHours:7.0f, obstacles:ssoObstacles,  rng),
            Mission(userId, "LEO_GENERIC","success", score:841, risk:0.22, dv:9.18, dep:now.AddDays(-2),  durHours:5.8f, obstacles:"[]",          rng),
            Mission(userId, "ISS",        "success", score:966, risk:0.06, dv:9.41, dep:now.AddDays(-1),  durHours:6.2f, obstacles:"[]",          rng),
        ];
    }

    private static MissionEntity Mission(
        Guid userId, string dest, string status,
        int score, double risk, double dv,
        DateTime dep, float durHours,
        string obstacles, Random rng)
    {
        var arr = dep.AddHours(durHours);
        return new MissionEntity
        {
            Id                   = Guid.NewGuid(),
            UserId               = userId,
            Destination          = dest,
            Status               = status,
            MissionScore         = score,
            RiskScore            = risk,
            DeltaVKmS            = dv,
            ObstaclesEncountered = ParseObstacleCount(obstacles),
            DepartureTime        = dep,
            ArrivalTime          = arr,
            ObstaclesJson        = obstacles,
            CreatedAt            = dep.AddMinutes(rng.Next(1, 15))
        };
    }

    private static string ObstaclesJson(
        params (string name, double dist, string tca, string risk)[] items)
    {
        var list = items.Select(i => new
        {
            debris_id               = $"SEED_{i.name.Replace(" ", "_")}",
            debris_name             = i.name,
            closest_approach_km     = i.dist,
            time_of_closest_approach= i.tca,
            risk_level              = i.risk
        });
        return JsonSerializer.Serialize(list, JsonOpts);
    }

    private static int ParseObstacleCount(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetArrayLength();
        }
        catch { return 0; }
    }
}
