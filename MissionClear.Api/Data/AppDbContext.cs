using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<MissionEntity> Missions => Set<MissionEntity>();
    public DbSet<OrbitalObjectEntity> OrbitalObjects => Set<OrbitalObjectEntity>();
    public DbSet<UserFavoriteDebrisEntity> FavoriteDebris => Set<UserFavoriteDebrisEntity>();
    public DbSet<UserSavedWindowEntity> SavedWindows => Set<UserSavedWindowEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Seed 300+ Orbital Objects
        var seed = GenerateSeedData();
        modelBuilder.Entity<OrbitalObjectEntity>().HasData(seed);

        // User indexes
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Mission Cascade Delete
        modelBuilder.Entity<MissionEntity>()
            .HasOne(m => m.User)
            .WithMany(u => u.Missions)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // RefreshToken Cascade Delete
        modelBuilder.Entity<RefreshTokenEntity>()
            .HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index p/ busca rápida de tokens
        modelBuilder.Entity<RefreshTokenEntity>()
            .HasIndex(rt => rt.Token);

        // FavoriteDebris Cascade Delete
        modelBuilder.Entity<UserFavoriteDebrisEntity>()
            .HasOne(f => f.User)
            .WithMany(u => u.FavoriteDebris)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique: one entry per user+debris_id pair
        modelBuilder.Entity<UserFavoriteDebrisEntity>()
            .HasIndex(f => new { f.UserId, f.DebrisId })
            .IsUnique();

        // SavedWindows Cascade Delete
        modelBuilder.Entity<UserSavedWindowEntity>()
            .HasOne(w => w.User)
            .WithMany(u => u.SavedWindows)
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique: one entry per user+window_id pair
        modelBuilder.Entity<UserSavedWindowEntity>()
            .HasIndex(w => new { w.UserId, w.WindowId })
            .IsUnique();
    }

    private static List<OrbitalObjectEntity> GenerateSeedData()
    {
        var random = new Random(42); // Seed fixa para reprodutibilidade
        var list = new List<OrbitalObjectEntity>();
        var now = new DateTime(2026, 05, 29, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 1; i <= 350; i++)
        {
            list.Add(new OrbitalObjectEntity
            {
                Id            = $"SEED_{i:D5}",
                Name          = $"Debris MC-{i}",
                Type          = i % 10 == 0 ? "rocket_body" : i % 5 == 0 ? "satellite" : "debris",
                Latitude      = (random.NextDouble() * 180.0) - 90.0,
                Longitude     = (random.NextDouble() * 360.0) - 180.0,
                AltitudeKm    = 300.0 + (random.NextDouble() * 1200.0), // 300km - 1500km
                VelocityKmS   = 7.0 + (random.NextDouble() * 1.5),      // 7.0 - 8.5 km/s
                Source        = "local_seed",
                UpdatedAt     = now
            });
        }
        return list;
    }
}
