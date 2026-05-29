# Phase 01 — Database + Repository Pattern

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Trocar SQLite por MySQL, adicionar campo Role nas entidades, implementar Repository Pattern com interfaces, gerar migration inicial.

**Architecture:** DbContext → Repository implementations → IRepository interfaces → Services (services não tocam DbContext diretamente).

**Tech Stack:** EF Core 8, Pomelo MySQL, xUnit

---

### Task 1: Criar Entities (UserEntity, RefreshTokenEntity, MissionEntity)

**Files:**
- Create: `MissionClear.Api/Entities/UserEntity.cs`
- Create: `MissionClear.Api/Entities/RefreshTokenEntity.cs`
- Create: `MissionClear.Api/Entities/MissionEntity.cs`

- [ ] **Step 1: Criar diretório Entities**

```powershell
mkdir MissionClear.Api/Entities
```

- [ ] **Step 2: Escrever UserEntity.cs**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

[Table("users")]
public sealed class UserEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("email")]
    [MaxLength(256)]
    public required string Email { get; set; }

    [Column("display_name")]
    [MaxLength(100)]
    public required string DisplayName { get; set; }

    [Column("password_hash")]
    [MaxLength(256)]
    public required string PasswordHash { get; set; }

    [Column("role")]
    [MaxLength(50)]
    public string Role { get; set; } = "Researcher";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; } = [];
    public ICollection<MissionEntity> Missions { get; set; } = [];
}
```

- [ ] **Step 3: Escrever RefreshTokenEntity.cs**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

[Table("refresh_tokens")]
public sealed class RefreshTokenEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("token")]
    [MaxLength(512)]
    public required string Token { get; set; }

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("is_revoked")]
    public bool IsRevoked { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
```

- [ ] **Step 4: Escrever MissionEntity.cs**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MissionClear.Api.Entities;

[Table("missions")]
public sealed class MissionEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("destination")]
    [MaxLength(50)]
    public required string Destination { get; set; }

    [Column("status")]
    [MaxLength(20)]
    public required string Status { get; set; }

    [Column("mission_score")]
    public int MissionScore { get; set; }

    [Column("risk_score")]
    public double RiskScore { get; set; }

    [Column("delta_v_km_s")]
    public double DeltaVKmS { get; set; }

    [Column("obstacles_encountered")]
    public int ObstaclesEncountered { get; set; }

    [Column("departure_time")]
    public DateTime DepartureTime { get; set; }

    [Column("arrival_time")]
    public DateTime ArrivalTime { get; set; }

    [Column("obstacles_json")]
    [MaxLength(8000)]
    public string? ObstaclesJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
```

---

### Task 2: Atualizar AppDbContext

**Files:**
- Modify: `MissionClear.Api/Data/AppDbContext.cs`

- [ ] **Step 1: Substituir AppDbContext**

```csharp
using MissionClear.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace MissionClear.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<MissionEntity> Missions => Set<MissionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.HasMany(u => u.RefreshTokens)
             .WithOne(r => r.User)
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(u => u.Missions)
             .WithOne(m => m.User)
             .HasForeignKey(m => m.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshTokenEntity>(e =>
        {
            e.HasIndex(r => r.Token).IsUnique();
        });
    }
}
```

---

### Task 3: Criar Repository Interfaces

**Files:**
- Create: `MissionClear.Api/Data/Repositories/IUserRepository.cs`
- Create: `MissionClear.Api/Data/Repositories/IRefreshTokenRepository.cs`
- Create: `MissionClear.Api/Data/Repositories/IMissionRepository.cs`

- [ ] **Step 1: Criar diretório**

```powershell
mkdir MissionClear.Api/Data/Repositories
```

- [ ] **Step 2: Escrever IUserRepository.cs**

```csharp
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public interface IUserRepository
{
    Task<UserEntity?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserEntity?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<UserEntity> CreateAsync(UserEntity user, CancellationToken ct = default);
    Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct = default);
}
```

- [ ] **Step 3: Escrever IRefreshTokenRepository.cs**

```csharp
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshTokenEntity?> FindActiveByTokenAsync(string token, CancellationToken ct = default);
    Task CreateAsync(RefreshTokenEntity token, CancellationToken ct = default);
    Task RevokeByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task RevokeByTokenAsync(string token, CancellationToken ct = default);
    Task DeleteExpiredAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Escrever IMissionRepository.cs**

```csharp
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public record MissionPageResult(IReadOnlyList<MissionEntity> Items, int Total);

public interface IMissionRepository
{
    Task<MissionPageResult> FindByUserIdAsync(
        Guid userId,
        int page,
        int limit,
        string? status,
        string? destination,
        string sort,
        CancellationToken ct = default);

    Task<MissionEntity?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<MissionEntity> CreateAsync(MissionEntity mission, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<MissionStatsProjection> GetStatsByUserIdAsync(Guid userId, CancellationToken ct = default);
}

public record MissionStatsProjection(
    int Total,
    int Successful,
    int Failed,
    int Aborted,
    int BestScore,
    int WorstScore,
    double AverageScore,
    double TotalDeltaV,
    int TotalObstacles,
    string? FavoriteDestination,
    Dictionary<string, int> MissionsByDestination);
```

---

### Task 4: Implementar Repositories

**Files:**
- Create: `MissionClear.Api/Data/Repositories/UserRepository.cs`
- Create: `MissionClear.Api/Data/Repositories/RefreshTokenRepository.cs`
- Create: `MissionClear.Api/Data/Repositories/MissionRepository.cs`

- [ ] **Step 1: Escrever UserRepository.cs**

```csharp
using MissionClear.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace MissionClear.Api.Data.Repositories;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<UserEntity?> FindByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<UserEntity?> FindByEmailAsync(string email, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task<UserEntity> CreateAsync(UserEntity user, CancellationToken ct)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct)
    {
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
```

- [ ] **Step 2: Escrever RefreshTokenRepository.cs**

```csharp
using MissionClear.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace MissionClear.Api.Data.Repositories;

public sealed class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshTokenEntity?> FindActiveByTokenAsync(string token, CancellationToken ct) =>
        db.RefreshTokens.FirstOrDefaultAsync(
            r => r.Token == token && !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow, ct);

    public async Task CreateAsync(RefreshTokenEntity token, CancellationToken ct)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeByUserIdAsync(Guid userId, CancellationToken ct)
    {
        await db.RefreshTokens
            .Where(r => r.UserId == userId && !r.IsRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsRevoked, true), ct);
    }

    public async Task RevokeByTokenAsync(string token, CancellationToken ct)
    {
        await db.RefreshTokens
            .Where(r => r.Token == token)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsRevoked, true), ct);
    }

    public async Task DeleteExpiredAsync(CancellationToken ct)
    {
        await db.RefreshTokens
            .Where(r => r.ExpiresAt < DateTime.UtcNow)
            .ExecuteDeleteAsync(ct);
    }
}
```

- [ ] **Step 3: Escrever MissionRepository.cs**

```csharp
using MissionClear.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace MissionClear.Api.Data.Repositories;

public sealed class MissionRepository(AppDbContext db) : IMissionRepository
{
    public async Task<MissionPageResult> FindByUserIdAsync(
        Guid userId, int page, int limit, string? status, string? destination, string sort,
        CancellationToken ct)
    {
        var query = db.Missions.Where(m => m.UserId == userId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(m => m.Status == status);

        if (!string.IsNullOrEmpty(destination))
            query = query.Where(m => m.Destination == destination);

        query = sort switch
        {
            "score_desc" => query.OrderByDescending(m => m.MissionScore),
            "risk_score_asc" => query.OrderBy(m => m.RiskScore),
            _ => query.OrderByDescending(m => m.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync(ct);

        return new MissionPageResult(items, total);
    }

    public Task<MissionEntity?> FindByIdAsync(Guid id, CancellationToken ct) =>
        db.Missions.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<MissionEntity> CreateAsync(MissionEntity mission, CancellationToken ct)
    {
        db.Missions.Add(mission);
        await db.SaveChangesAsync(ct);
        return mission;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await db.Missions.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task<MissionStatsProjection> GetStatsByUserIdAsync(Guid userId, CancellationToken ct)
    {
        var missions = await db.Missions
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

        if (missions.Count == 0)
            return new MissionStatsProjection(0, 0, 0, 0, 0, 0, 0, 0, 0, null, []);

        var byDestination = missions
            .GroupBy(m => m.Destination)
            .ToDictionary(g => g.Key, g => g.Count());

        var favorite = byDestination
            .OrderByDescending(kv => kv.Value)
            .FirstOrDefault().Key;

        return new MissionStatsProjection(
            Total: missions.Count,
            Successful: missions.Count(m => m.Status == "success"),
            Failed: missions.Count(m => m.Status == "failure"),
            Aborted: missions.Count(m => m.Status == "aborted"),
            BestScore: missions.Max(m => m.MissionScore),
            WorstScore: missions.Min(m => m.MissionScore),
            AverageScore: missions.Average(m => m.MissionScore),
            TotalDeltaV: missions.Sum(m => m.DeltaVKmS),
            TotalObstacles: missions.Sum(m => m.ObstaclesEncountered),
            FavoriteDestination: favorite,
            MissionsByDestination: byDestination);
    }
}
```

---

### Task 5: Testes para Repositories

**Files:**
- Create: `MissionClear.Tests/Data/UserRepositoryTests.cs`
- Create: `MissionClear.Tests/Data/MissionRepositoryTests.cs`

- [ ] **Step 1: Escrever UserRepositoryTests.cs**

```csharp
using FluentAssertions;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace MissionClear.Tests.Data;

public sealed class UserRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly UserRepository _repo;

    public UserRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new UserRepository(_db);
    }

    [Fact]
    public async Task CreateAsync_PersistsUser_WithResearcherRole()
    {
        var user = new UserEntity
        {
            Email = "test@test.com",
            DisplayName = "Test",
            PasswordHash = "hash"
        };

        var result = await _repo.CreateAsync(user, default);

        result.Id.Should().NotBe(Guid.Empty);
        result.Role.Should().Be("Researcher");
    }

    [Fact]
    public async Task FindByEmailAsync_ReturnsUser_WhenEmailExists()
    {
        var user = new UserEntity { Email = "a@b.com", DisplayName = "A", PasswordHash = "h" };
        await _repo.CreateAsync(user, default);

        var found = await _repo.FindByEmailAsync("a@b.com", default);

        found.Should().NotBeNull();
        found!.Email.Should().Be("a@b.com");
    }

    [Fact]
    public async Task FindByEmailAsync_ReturnsNull_WhenEmailNotFound()
    {
        var found = await _repo.FindByEmailAsync("notfound@test.com", default);
        found.Should().BeNull();
    }

    [Fact]
    public async Task EmailExistsAsync_ReturnsTrue_WhenEmailRegistered()
    {
        var user = new UserEntity { Email = "exists@test.com", DisplayName = "E", PasswordHash = "h" };
        await _repo.CreateAsync(user, default);

        var exists = await _repo.EmailExistsAsync("exists@test.com", default);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_ChangesRole_WhenSetToAdministrator()
    {
        var user = new UserEntity { Email = "u@test.com", DisplayName = "U", PasswordHash = "h" };
        await _repo.CreateAsync(user, default);

        user.Role = "Administrator";
        await _repo.UpdateAsync(user, default);

        var updated = await _repo.FindByIdAsync(user.Id, default);
        updated!.Role.Should().Be("Administrator");
    }

    public void Dispose() => _db.Dispose();
}
```

- [ ] **Step 2: Escrever MissionRepositoryTests.cs**

```csharp
using FluentAssertions;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace MissionClear.Tests.Data;

public sealed class MissionRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MissionRepository _repo;
    private readonly Guid _userId = Guid.NewGuid();

    public MissionRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new MissionRepository(_db);

        // Seed user (FK required)
        _db.Users.Add(new UserEntity
        {
            Id = _userId,
            Email = "pilot@test.com",
            DisplayName = "Pilot",
            PasswordHash = "hash"
        });
        _db.SaveChanges();
    }

    private MissionEntity CreateMission(string destination = "ISS", string status = "success", int score = 80) =>
        new()
        {
            UserId = _userId,
            Destination = destination,
            Status = status,
            MissionScore = score,
            RiskScore = 0.1,
            DeltaVKmS = 9.4,
            DepartureTime = DateTime.UtcNow,
            ArrivalTime = DateTime.UtcNow.AddHours(6)
        };

    [Fact]
    public async Task CreateAsync_PersistsMission()
    {
        var mission = CreateMission();
        var result = await _repo.CreateAsync(mission, default);
        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task FindByUserIdAsync_ReturnsOnlyUserMissions()
    {
        await _repo.CreateAsync(CreateMission("ISS"), default);
        await _repo.CreateAsync(CreateMission("SSO"), default);

        var result = await _repo.FindByUserIdAsync(_userId, 1, 20, null, null, "created_at_desc", default);

        result.Total.Should().Be(2);
        result.Items.Should().AllSatisfy(m => m.UserId.Should().Be(_userId));
    }

    [Fact]
    public async Task FindByUserIdAsync_FiltersbyStatus()
    {
        await _repo.CreateAsync(CreateMission(status: "success"), default);
        await _repo.CreateAsync(CreateMission(status: "failure"), default);

        var result = await _repo.FindByUserIdAsync(_userId, 1, 20, "failure", null, "created_at_desc", default);

        result.Total.Should().Be(1);
        result.Items[0].Status.Should().Be("failure");
    }

    [Fact]
    public async Task DeleteAsync_RemovesMission()
    {
        var mission = await _repo.CreateAsync(CreateMission(), default);
        await _repo.DeleteAsync(mission.Id, default);
        var found = await _repo.FindByIdAsync(mission.Id, default);
        found.Should().BeNull();
    }

    [Fact]
    public async Task GetStatsByUserIdAsync_ReturnsFavoriteDestination()
    {
        await _repo.CreateAsync(CreateMission("ISS"), default);
        await _repo.CreateAsync(CreateMission("ISS"), default);
        await _repo.CreateAsync(CreateMission("SSO"), default);

        var stats = await _repo.GetStatsByUserIdAsync(_userId, default);

        stats.FavoriteDestination.Should().Be("ISS");
        stats.Total.Should().Be(3);
    }

    public void Dispose() => _db.Dispose();
}
```

- [ ] **Step 3: Rodar testes**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj -v normal
```

Resultado esperado: todos passam.

---

### Task 6: Gerar Migration MySQL

Nota: a migration deve ser gerada com a API configurada para usar MySQL (via Aspire AppHost rodando), OU usando uma connection string direta de dev.

- [ ] **Step 1: Criar appsettings.Development.json com connection string para design-time**

Em `MissionClear.Api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "missionclear": "Server=localhost;Port=3306;Database=missionclear_dev;User=root;Password=MissionClear_Dev_2025!"
  },
  "Jwt": {
    "Secret": "MissionClear-Dev-Secret-Key-32chars!!",
    "Issuer": "MissionClear.Api",
    "Audience": "MissionClear.Mobile",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 7
  }
}
```

**ATENÇÃO:** Nunca commitar senhas reais. Este arquivo está em `.gitignore`.

- [ ] **Step 2: Adicionar DesignTimeDbContextFactory para migrations sem AppHost**

Criar `MissionClear.Api/Data/AppDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MissionClear.Api.Data;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        var connectionString = config.GetConnectionString("missionclear")!;

        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

        return new AppDbContext(optionsBuilder.Options);
    }
}
```

- [ ] **Step 3: Verificar MySQL está rodando (via Docker ou local)**

```powershell
# Opção A: MySQL local rodando na porta 3306
# Opção B: Subir container diretamente
docker run -d --name mysql-missionclear -e MYSQL_ROOT_PASSWORD=MissionClear_Dev_2025! -e MYSQL_DATABASE=missionclear_dev -p 3306:3306 mysql:8.0
```

- [ ] **Step 4: Gerar migration**

```powershell
cd MissionClear.Api
dotnet ef migrations add InitialCreate --output-dir Data/Migrations
cd ..
```

Resultado esperado: `MissionClear.Api/Data/Migrations/` com `*_InitialCreate.cs` e `AppDbContextModelSnapshot.cs`.

- [ ] **Step 5: Aplicar migration**

```powershell
cd MissionClear.Api
dotnet ef database update
cd ..
```

Verificar no MySQL Workbench: tabelas `users`, `refresh_tokens`, `missions` criadas com colunas corretas.

- [ ] **Step 6: Commit**

```powershell
git add MissionClear.Api/Entities/ MissionClear.Api/Data/ MissionClear.Tests/Data/ MissionClear.Api/appsettings.Development.json
git commit -m "feat(db): MySQL entities, Repository pattern, InitialCreate migration"
```

Nota: verificar que `appsettings.Development.json` está no `.gitignore` antes de commitar.
