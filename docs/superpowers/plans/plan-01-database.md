# Plan 01 — Database + Repository Pattern (MySQL + EF Core)

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:executing-plans`

**Execution order:** After plan-00-scaffolding. Blocks plan-04-auth and plan-06-history-dashboard.
**Estimated time:** 45 minutes.
**Goal:** Trocar SQLite por MySQL via Pomelo, adicionar campo `Role` nas entidades com snake_case columns, implementar Repository Pattern completo com interfaces, AppDbContextFactory para migrations sem AppHost, e validar tudo com testes xUnit + InMemory.

**Dependencies:**
- `plan-00-scaffolding.md` concluído
- Docker Desktop rodando (para MySQL container em dev)
- `dotnet-ef` global tool instalado

**Unlocks:** `plan-04-auth.md` (UserEntity + RefreshTokenEntity + IUserRepository), `plan-06-history-dashboard.md` (MissionEntity + IMissionRepository)

> **Separação de responsabilidades (inviolável):**
> - `Entities/` — classes EF Core mapeadas para tabelas. Nunca expostas diretamente na API.
> - `Data/Repositories/` — acesso ao banco via EF Core. Implementam interfaces no mesmo namespace.
> - `Data/AppDbContext.cs` — configura DbContext, índices, relacionamentos. Zero lógica de negócio.
> - `Services/` — usa repositórios via interface. **Nunca acessa `AppDbContext` diretamente.**

---

## Pré-requisitos de Ambiente

- [ ] **Step 0.1: Verificar dotnet-ef CLI**

```powershell
dotnet ef --version
# Esperado: 8.x.x ou superior
```

Se não instalado:

```powershell
dotnet tool install --global dotnet-ef --version 8.*
```

- [ ] **Step 0.2: Verificar Docker rodando**

```powershell
docker ps
# Esperado: tabela de containers (pode estar vazia)
```

---

## Task 1.1 — Trocar SQLite por MySQL (pacotes)

**Files:**
- Modify: `MissionClear.Api/MissionClear.Api.csproj`

- [ ] **Step 1.1.1: Remover Sqlite, adicionar Pomelo MySQL**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C\MissionClear.Api"

# Remover SQLite
dotnet remove package Microsoft.EntityFrameworkCore.Sqlite

# Adicionar Pomelo MySQL
dotnet add package Pomelo.EntityFrameworkCore.MySql --version 8.0.2

# Adicionar Aspire integration (para builder.AddMySqlDbContext)
dotnet add package Aspire.Pomelo.EntityFrameworkCore.MySql --version 9.1.0
```

- [ ] **Step 1.1.2: Verificar .csproj resultante**

O arquivo `MissionClear.Api/MissionClear.Api.csproj` deve conter:

```xml
<PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.2" />
<PackageReference Include="Aspire.Pomelo.EntityFrameworkCore.MySql" Version="9.1.0" />
```

E NÃO deve mais conter:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" ... />
```

- [ ] **Step 1.1.3: Adicionar appsettings.Development.json ao .gitignore**

Append ao `.gitignore` na raiz do repositório:

```
# MySQL connection strings with credentials — never commit
MissionClear.Api/appsettings.Development.json

# SQLite remnants
*.db
*.db-shm
*.db-wal
```

- [ ] **Step 1.1.4: Build parcial**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet build MissionClear.Api/MissionClear.Api.csproj
```

Esperado: `Build succeeded` (pode ter warnings sobre código existente usando SQLite — normal neste step).

- [ ] **Step 1.1.5: Commit**

```powershell
git add MissionClear.Api/MissionClear.Api.csproj .gitignore
git commit -m "chore: swap SQLite for Pomelo MySQL 8.0.2 + Aspire integration"
```

---

## Task 1.2 — Entidades com snake_case columns

**Files:**
- Create/Replace: `MissionClear.Api/Entities/UserEntity.cs`
- Create/Replace: `MissionClear.Api/Entities/RefreshTokenEntity.cs`
- Create/Replace: `MissionClear.Api/Entities/MissionEntity.cs`

> **TDD:** Escreva os testes em Task 1.5 ANTES de implementar. Se executando sequencialmente, continue — os testes serão escritos na Task 1.5 e rodados após Task 1.4.

- [ ] **Step 1.2.1: `Entities/UserEntity.cs`**

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

    /// <summary>BCrypt hash — NEVER store plaintext password.</summary>
    [Column("password_hash")]
    [MaxLength(256)]
    public required string PasswordHash { get; set; }

    /// <summary>Researcher | Administrator</summary>
    [Column("role")]
    [MaxLength(50)]
    public string Role { get; set; } = "Researcher";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; } = [];
    public ICollection<MissionEntity> Missions { get; set; } = [];
}
```

- [ ] **Step 1.2.2: `Entities/RefreshTokenEntity.cs`**

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

- [ ] **Step 1.2.3: `Entities/MissionEntity.cs`**

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

    /// <summary>ISS | LEO_GENERIC | SSO</summary>
    [Column("destination")]
    [MaxLength(50)]
    public required string Destination { get; set; }

    /// <summary>success | failure | aborted</summary>
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

    /// <summary>JSON array of obstacle objects — serialized at Service layer.</summary>
    [Column("obstacles_json")]
    [MaxLength(8000)]
    public string ObstaclesJson { get; set; } = "[]";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public UserEntity User { get; set; } = null!;
}
```

- [ ] **Step 1.2.4: Build incremental**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet build MissionClear.Api/MissionClear.Api.csproj
```

Esperado: `Build succeeded. 0 Error(s).`

- [ ] **Step 1.2.5: Commit**

```powershell
git add MissionClear.Api/Entities/
git commit -m "feat(entities): UserEntity with Role, RefreshTokenEntity, MissionEntity — snake_case columns"
```

---

## Task 1.3 — AppDbContext (MySQL provider)

**Files:**
- Create/Replace: `MissionClear.Api/Data/AppDbContext.cs`
- Create: `MissionClear.Api/Data/AppDbContextFactory.cs`
- Modify: `MissionClear.Api/appsettings.json`
- Create: `MissionClear.Api/appsettings.Development.json`
- Modify: `MissionClear.Api/Program.cs`

- [ ] **Step 1.3.1: `Data/AppDbContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<MissionEntity> Missions => Set<MissionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
            e.HasIndex(r => r.UserId);
        });

        modelBuilder.Entity<MissionEntity>(e =>
        {
            e.HasIndex(m => m.UserId);
            e.HasIndex(m => m.CreatedAt);
        });
    }
}
```

- [ ] **Step 1.3.2: `Data/AppDbContextFactory.cs`**

Este factory permite rodar `dotnet ef migrations add` sem precisar do Aspire AppHost rodando.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MissionClear.Api.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// Reads connection string from appsettings.Development.json (never committed).
/// Usage: dotnet ef migrations add InitialCreate --output-dir Data/Migrations
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = config.GetConnectionString("missionclear")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:missionclear not found. " +
                "Add it to appsettings.Development.json (not committed).");

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

        return new AppDbContext(optionsBuilder.Options);
    }
}
```

- [ ] **Step 1.3.3: `appsettings.json` — connection string placeholder**

Substituir conteúdo completo de `MissionClear.Api/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "missionclear": ""
  },
  "Jwt": {
    "Secret": "",
    "Issuer": "mission-clear-api",
    "Audience": "mission-clear-mobile",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 7
  }
}
```

- [ ] **Step 1.3.4: `appsettings.Development.json` — credenciais locais (NÃO commitar)**

Criar `MissionClear.Api/appsettings.Development.json` com credenciais de dev. **Este arquivo está no .gitignore.**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },
  "ConnectionStrings": {
    "missionclear": "Server=localhost;Port=3306;Database=missionclear_dev;User=root;Password=MissionClear_Dev_2025!"
  },
  "Jwt": {
    "Secret": "MissionClear-Dev-Secret-Key-32chars!!",
    "Issuer": "mission-clear-api",
    "Audience": "mission-clear-mobile",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 7
  }
}
```

> **ATENÇÃO:** Nunca commitar este arquivo. Verificar `.gitignore` antes de `git add`.

- [ ] **Step 1.3.5: `Program.cs` — registrar AppDbContext com MySQL via Aspire**

Localizar a seção de DI em `Program.cs` (após `var builder = WebApplication.CreateBuilder(args);`) e substituir (ou adicionar) o bloco de DbContext:

```csharp
// Substituir qualquer UseSqlite existente por:
builder.AddMySqlDbContext<AppDbContext>("missionclear");
```

Garantir que o `using` esteja presente no topo do arquivo:

```csharp
using MissionClear.Api.Data;
```

- [ ] **Step 1.3.6: Build**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet build MissionClear.Api/MissionClear.Api.csproj
```

Esperado: `Build succeeded. 0 Error(s).`

- [ ] **Step 1.3.7: Commit**

```powershell
git add MissionClear.Api/Data/AppDbContext.cs MissionClear.Api/Data/AppDbContextFactory.cs MissionClear.Api/appsettings.json MissionClear.Api/Program.cs
# NÃO adicionar appsettings.Development.json
git commit -m "feat(db): AppDbContext MySQL provider, cascade deletes, unique indexes, AppDbContextFactory"
```

---

## Task 1.4 — Migration Inicial (MySQL)

**Files:**
- Create: `MissionClear.Api/Data/Migrations/*` (auto-gerados)

**Pré-condição:** MySQL acessível na porta 3306 com as credenciais do `appsettings.Development.json`.

- [ ] **Step 1.4.1: Subir MySQL local (se não tiver instância)**

```powershell
docker run -d `
  --name mysql-missionclear `
  -e MYSQL_ROOT_PASSWORD=MissionClear_Dev_2025! `
  -e MYSQL_DATABASE=missionclear_dev `
  -p 3306:3306 `
  mysql:8.0
```

Aguardar ~30s para o MySQL inicializar. Verificar:

```powershell
docker logs mysql-missionclear --tail 20
# Esperado: "ready for connections"
```

- [ ] **Step 1.4.2: Gerar migration `InitialCreate`**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C\MissionClear.Api"
dotnet ef migrations add InitialCreate --output-dir Data/Migrations
```

Esperado: três arquivos criados em `Data/Migrations/`:
- `<timestamp>_InitialCreate.cs`
- `<timestamp>_InitialCreate.Designer.cs`
- `AppDbContextModelSnapshot.cs`

- [ ] **Step 1.4.3: Inspecionar migration gerada**

Abrir `Data/Migrations/<timestamp>_InitialCreate.cs` e verificar:
- `CreateTable("users")` com colunas `id`, `email`, `display_name`, `password_hash`, `role`, `created_at`
- `CreateTable("refresh_tokens")` com FK `user_id` e `onDelete: ReferentialAction.Cascade`
- `CreateTable("missions")` com FK `user_id`, coluna `obstacles_json`, e `onDelete: ReferentialAction.Cascade`
- `AddUniqueConstraint` em `users.email` e `refresh_tokens.token`

Se algo estiver errado: `dotnet ef migrations remove`, corrigir entidades/AppDbContext, repetir Step 1.4.2.

- [ ] **Step 1.4.4: Aplicar migration ao banco local**

```powershell
dotnet ef database update
cd ..
```

Esperado: tabelas `users`, `refresh_tokens`, `missions` criadas no banco `missionclear_dev`.

- [ ] **Step 1.4.5: Verificar schema**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C\MissionClear.Api"
dotnet ef dbcontext info
# Esperado: provider Pomelo.EntityFrameworkCore.MySql
```

- [ ] **Step 1.4.6: Commit**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
git add MissionClear.Api/Data/Migrations/
git commit -m "feat(db): InitialCreate migration — users, refresh_tokens, missions (MySQL)"
```

---

## Task 1.5 — Repository Interfaces

**Files:**
- Create: `MissionClear.Api/Data/Repositories/IUserRepository.cs`
- Create: `MissionClear.Api/Data/Repositories/IRefreshTokenRepository.cs`
- Create: `MissionClear.Api/Data/Repositories/IMissionRepository.cs`

> **TDD — RED phase:** Escreva os testes da Task 1.7 agora. Rode `dotnet test` e confirme que compilam mas falham (NotImplementedException ou similar). Só então avance para Task 1.6.

- [ ] **Step 1.5.1: `Data/Repositories/IUserRepository.cs`**

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

- [ ] **Step 1.5.2: `Data/Repositories/IRefreshTokenRepository.cs`**

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

- [ ] **Step 1.5.3: `Data/Repositories/IMissionRepository.cs`**

```csharp
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

/// <summary>Paginated result for mission queries.</summary>
public record MissionPageResult(IReadOnlyList<MissionEntity> Items, int Total);

/// <summary>Aggregated statistics for a user's missions.</summary>
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

public interface IMissionRepository
{
    /// <param name="sort">Accepted values: created_at_desc | score_desc | risk_score_asc</param>
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
```

- [ ] **Step 1.5.4: Build**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet build MissionClear.Api/MissionClear.Api.csproj
```

Esperado: `Build succeeded. 0 Error(s).`

- [ ] **Step 1.5.5: Commit**

```powershell
git add MissionClear.Api/Data/Repositories/IUserRepository.cs MissionClear.Api/Data/Repositories/IRefreshTokenRepository.cs MissionClear.Api/Data/Repositories/IMissionRepository.cs
git commit -m "feat(repos): IUserRepository, IRefreshTokenRepository, IMissionRepository interfaces"
```

---

## Task 1.6 — Implementações dos Repositories

**Files:**
- Create: `MissionClear.Api/Data/Repositories/UserRepository.cs`
- Create: `MissionClear.Api/Data/Repositories/RefreshTokenRepository.cs`
- Create: `MissionClear.Api/Data/Repositories/MissionRepository.cs`

> **TDD — GREEN phase:** Após escrever cada implementação, rode os testes correspondentes da Task 1.7.

- [ ] **Step 1.6.1: `Data/Repositories/UserRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

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

- [ ] **Step 1.6.2: `Data/Repositories/RefreshTokenRepository.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

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

- [ ] **Step 1.6.3: `Data/Repositories/MissionRepository.cs`**

Suporta sort: `created_at_desc` (default), `score_desc`, `risk_score_asc`.

```csharp
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data.Repositories;

public sealed class MissionRepository(AppDbContext db) : IMissionRepository
{
    public async Task<MissionPageResult> FindByUserIdAsync(
        Guid userId, int page, int limit,
        string? status, string? destination, string sort,
        CancellationToken ct)
    {
        var query = db.Missions.Where(m => m.UserId == userId);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(m => m.Status == status);

        if (!string.IsNullOrEmpty(destination))
            query = query.Where(m => m.Destination == destination);

        query = sort switch
        {
            "score_desc"      => query.OrderByDescending(m => m.MissionScore),
            "risk_score_asc"  => query.OrderBy(m => m.RiskScore),
            _                 => query.OrderByDescending(m => m.CreatedAt), // created_at_desc
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
            return new MissionStatsProjection(0, 0, 0, 0, 0, 0, 0.0, 0.0, 0, null, []);

        var byDestination = missions
            .GroupBy(m => m.Destination)
            .ToDictionary(g => g.Key, g => g.Count());

        var favorite = byDestination
            .OrderByDescending(kv => kv.Value)
            .First().Key;

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

- [ ] **Step 1.6.4: Registrar no DI container (`Program.cs`)**

Adicionar após `builder.AddMySqlDbContext<AppDbContext>("missionclear")`:

```csharp
using MissionClear.Api.Data.Repositories;

// Repository registrations (Scoped — same lifetime as DbContext)
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IMissionRepository, MissionRepository>();
```

- [ ] **Step 1.6.5: Build**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet build MissionClear.Api/MissionClear.Api.csproj
```

Esperado: `Build succeeded. 0 Error(s).`

- [ ] **Step 1.6.6: Commit**

```powershell
git add MissionClear.Api/Data/Repositories/UserRepository.cs MissionClear.Api/Data/Repositories/RefreshTokenRepository.cs MissionClear.Api/Data/Repositories/MissionRepository.cs MissionClear.Api/Program.cs
git commit -m "feat(repos): UserRepository, RefreshTokenRepository, MissionRepository implementations + DI registration"
```

---

## Task 1.7 — Testes xUnit (InMemory)

**Files:**
- Create: `MissionClear.Tests/Data/UserRepositoryTests.cs`
- Create: `MissionClear.Tests/Data/RefreshTokenRepositoryTests.cs`
- Create: `MissionClear.Tests/Data/MissionRepositoryTests.cs`

> **TDD — RED → GREEN → REFACTOR:** Escreva todos os testes, rode `dotnet test` confirmando RED (falhas por falta de implementação), implemente (Task 1.6), rode novamente confirmando GREEN.

- [ ] **Step 1.7.1: Verificar pacotes de teste**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C\MissionClear.Tests"
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.*
dotnet add package FluentAssertions --version 6.*
```

(Idempotente — se já instalado, é no-op.)

- [ ] **Step 1.7.2: `MissionClear.Tests/Data/UserRepositoryTests.cs`**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;

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
    public async Task CreateAsync_PersistsUser_WithDefaultResearcherRole()
    {
        var user = new UserEntity
        {
            Email = "test@test.com",
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123")
        };

        var result = await _repo.CreateAsync(user, default);

        result.Id.Should().NotBe(Guid.Empty);
        result.Role.Should().Be("Researcher");
        result.PasswordHash.Should().NotBe("password123"); // BCrypt hash, not plaintext
    }

    [Fact]
    public async Task FindByEmailAsync_ReturnsUser_WhenEmailExists()
    {
        var user = new UserEntity
        {
            Email = "find@test.com",
            DisplayName = "Find Me",
            PasswordHash = "hashed"
        };
        await _repo.CreateAsync(user, default);

        var found = await _repo.FindByEmailAsync("find@test.com", default);

        found.Should().NotBeNull();
        found!.Email.Should().Be("find@test.com");
    }

    [Fact]
    public async Task FindByEmailAsync_ReturnsNull_WhenEmailNotFound()
    {
        var found = await _repo.FindByEmailAsync("nobody@test.com", default);

        found.Should().BeNull();
    }

    [Fact]
    public async Task EmailExistsAsync_ReturnsTrue_WhenEmailRegistered()
    {
        var user = new UserEntity
        {
            Email = "exists@test.com",
            DisplayName = "Exists",
            PasswordHash = "hashed"
        };
        await _repo.CreateAsync(user, default);

        var exists = await _repo.EmailExistsAsync("exists@test.com", default);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task EmailExistsAsync_ReturnsFalse_WhenEmailNotRegistered()
    {
        var exists = await _repo.EmailExistsAsync("ghost@test.com", default);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ChangesRole_WhenPromotedToAdministrator()
    {
        var user = new UserEntity
        {
            Email = "promote@test.com",
            DisplayName = "Promote Me",
            PasswordHash = "hashed"
        };
        await _repo.CreateAsync(user, default);

        user.Role = "Administrator";
        await _repo.UpdateAsync(user, default);

        var updated = await _repo.FindByIdAsync(user.Id, default);
        updated!.Role.Should().Be("Administrator");
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsNull_WhenIdNotFound()
    {
        var found = await _repo.FindByIdAsync(Guid.NewGuid(), default);

        found.Should().BeNull();
    }

    public void Dispose() => _db.Dispose();
}
```

- [ ] **Step 1.7.3: `MissionClear.Tests/Data/RefreshTokenRepositoryTests.cs`**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;

namespace MissionClear.Tests.Data;

public sealed class RefreshTokenRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RefreshTokenRepository _repo;
    private readonly Guid _userId = Guid.NewGuid();

    public RefreshTokenRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _repo = new RefreshTokenRepository(_db);

        _db.Users.Add(new UserEntity
        {
            Id = _userId,
            Email = "user@test.com",
            DisplayName = "Test",
            PasswordHash = "hashed"
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task CreateAsync_PersistsToken()
    {
        var token = new RefreshTokenEntity
        {
            UserId = _userId,
            Token = "valid-token-abc",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        await _repo.CreateAsync(token, default);

        var found = await _db.RefreshTokens.FindAsync(token.Id);
        found.Should().NotBeNull();
        found!.Token.Should().Be("valid-token-abc");
    }

    [Fact]
    public async Task FindActiveByTokenAsync_ReturnsToken_WhenActiveAndNotExpired()
    {
        var token = new RefreshTokenEntity
        {
            UserId = _userId,
            Token = "active-token",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        };
        await _repo.CreateAsync(token, default);

        var found = await _repo.FindActiveByTokenAsync("active-token", default);

        found.Should().NotBeNull();
        found!.Token.Should().Be("active-token");
    }

    [Fact]
    public async Task FindActiveByTokenAsync_ReturnsNull_WhenTokenRevoked()
    {
        var token = new RefreshTokenEntity
        {
            UserId = _userId,
            Token = "revoked-token",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = true
        };
        await _repo.CreateAsync(token, default);

        var found = await _repo.FindActiveByTokenAsync("revoked-token", default);

        found.Should().BeNull();
    }

    [Fact]
    public async Task FindActiveByTokenAsync_ReturnsNull_WhenTokenExpired()
    {
        var token = new RefreshTokenEntity
        {
            UserId = _userId,
            Token = "expired-token",
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // past
            IsRevoked = false
        };
        await _repo.CreateAsync(token, default);

        var found = await _repo.FindActiveByTokenAsync("expired-token", default);

        found.Should().BeNull();
    }

    [Fact]
    public async Task RevokeByTokenAsync_SetsIsRevoked_ToTrue()
    {
        var token = new RefreshTokenEntity
        {
            UserId = _userId,
            Token = "to-revoke",
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        await _repo.CreateAsync(token, default);

        await _repo.RevokeByTokenAsync("to-revoke", default);

        var updated = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == "to-revoke");
        updated!.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeByUserIdAsync_RevokesAllActiveTokens_ForUser()
    {
        await _repo.CreateAsync(new RefreshTokenEntity
        {
            UserId = _userId, Token = "tok1", ExpiresAt = DateTime.UtcNow.AddDays(7)
        }, default);
        await _repo.CreateAsync(new RefreshTokenEntity
        {
            UserId = _userId, Token = "tok2", ExpiresAt = DateTime.UtcNow.AddDays(7)
        }, default);

        await _repo.RevokeByUserIdAsync(_userId, default);

        var tokens = await _db.RefreshTokens
            .Where(r => r.UserId == _userId)
            .ToListAsync();
        tokens.Should().AllSatisfy(t => t.IsRevoked.Should().BeTrue());
    }

    public void Dispose() => _db.Dispose();
}
```

- [ ] **Step 1.7.4: `MissionClear.Tests/Data/MissionRepositoryTests.cs`**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;

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

        _db.Users.Add(new UserEntity
        {
            Id = _userId,
            Email = "pilot@test.com",
            DisplayName = "Pilot",
            PasswordHash = "hashed"
        });
        _db.SaveChanges();
    }

    private MissionEntity Mission(
        string destination = "ISS",
        string status = "success",
        int score = 80,
        double riskScore = 0.1,
        double deltaV = 9.4) => new()
    {
        UserId = _userId,
        Destination = destination,
        Status = status,
        MissionScore = score,
        RiskScore = riskScore,
        DeltaVKmS = deltaV,
        ObstaclesEncountered = 0,
        DepartureTime = DateTime.UtcNow,
        ArrivalTime = DateTime.UtcNow.AddHours(6)
    };

    [Fact]
    public async Task CreateAsync_PersistsMission_WithObstaclesJsonDefault()
    {
        var mission = Mission();

        var result = await _repo.CreateAsync(mission, default);

        result.Id.Should().NotBe(Guid.Empty);
        result.ObstaclesJson.Should().Be("[]");
    }

    [Fact]
    public async Task FindByUserIdAsync_ReturnsOnlyMissions_ForGivenUser()
    {
        await _repo.CreateAsync(Mission("ISS"), default);
        await _repo.CreateAsync(Mission("SSO"), default);

        var result = await _repo.FindByUserIdAsync(_userId, 1, 20, null, null, "created_at_desc", default);

        result.Total.Should().Be(2);
        result.Items.Should().AllSatisfy(m => m.UserId.Should().Be(_userId));
    }

    [Fact]
    public async Task FindByUserIdAsync_FiltersBy_Status()
    {
        await _repo.CreateAsync(Mission(status: "success"), default);
        await _repo.CreateAsync(Mission(status: "failure"), default);
        await _repo.CreateAsync(Mission(status: "aborted"), default);

        var result = await _repo.FindByUserIdAsync(
            _userId, 1, 20, "failure", null, "created_at_desc", default);

        result.Total.Should().Be(1);
        result.Items[0].Status.Should().Be("failure");
    }

    [Fact]
    public async Task FindByUserIdAsync_FiltersBy_Destination()
    {
        await _repo.CreateAsync(Mission("ISS"), default);
        await _repo.CreateAsync(Mission("SSO"), default);
        await _repo.CreateAsync(Mission("ISS"), default);

        var result = await _repo.FindByUserIdAsync(
            _userId, 1, 20, null, "ISS", "created_at_desc", default);

        result.Total.Should().Be(2);
        result.Items.Should().AllSatisfy(m => m.Destination.Should().Be("ISS"));
    }

    [Fact]
    public async Task FindByUserIdAsync_Pagination_ReturnsCorrectPage()
    {
        for (var i = 0; i < 7; i++)
            await _repo.CreateAsync(Mission(), default);

        var page1 = await _repo.FindByUserIdAsync(_userId, 1, 3, null, null, "created_at_desc", default);
        var page3 = await _repo.FindByUserIdAsync(_userId, 3, 3, null, null, "created_at_desc", default);

        page1.Total.Should().Be(7);
        page1.Items.Should().HaveCount(3);
        page3.Items.Should().HaveCount(1); // 7 items, page 3 of 3
    }

    [Fact]
    public async Task FindByUserIdAsync_Sort_ScoreDesc_OrdersCorrectly()
    {
        await _repo.CreateAsync(Mission(score: 50), default);
        await _repo.CreateAsync(Mission(score: 90), default);
        await _repo.CreateAsync(Mission(score: 70), default);

        var result = await _repo.FindByUserIdAsync(
            _userId, 1, 10, null, null, "score_desc", default);

        result.Items[0].MissionScore.Should().Be(90);
        result.Items[2].MissionScore.Should().Be(50);
    }

    [Fact]
    public async Task FindByUserIdAsync_Sort_RiskScoreAsc_OrdersCorrectly()
    {
        await _repo.CreateAsync(Mission(riskScore: 0.8), default);
        await _repo.CreateAsync(Mission(riskScore: 0.1), default);
        await _repo.CreateAsync(Mission(riskScore: 0.5), default);

        var result = await _repo.FindByUserIdAsync(
            _userId, 1, 10, null, null, "risk_score_asc", default);

        result.Items[0].RiskScore.Should().BeApproximately(0.1, 1e-9);
        result.Items[2].RiskScore.Should().BeApproximately(0.8, 1e-9);
    }

    [Fact]
    public async Task FindByIdAsync_ReturnsNull_WhenMissionNotFound()
    {
        var found = await _repo.FindByIdAsync(Guid.NewGuid(), default);

        found.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesMission()
    {
        var mission = await _repo.CreateAsync(Mission(), default);

        await _repo.DeleteAsync(mission.Id, default);

        var found = await _repo.FindByIdAsync(mission.Id, default);
        found.Should().BeNull();
    }

    [Fact]
    public async Task GetStatsByUserIdAsync_ReturnsEmptyProjection_WhenNoMissions()
    {
        var stats = await _repo.GetStatsByUserIdAsync(_userId, default);

        stats.Total.Should().Be(0);
        stats.FavoriteDestination.Should().BeNull();
        stats.MissionsByDestination.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatsByUserIdAsync_ReturnsFavoriteDestination_AndCounts()
    {
        await _repo.CreateAsync(Mission("ISS", "success", 90), default);
        await _repo.CreateAsync(Mission("ISS", "success", 85), default);
        await _repo.CreateAsync(Mission("SSO", "failure", 40), default);

        var stats = await _repo.GetStatsByUserIdAsync(_userId, default);

        stats.Total.Should().Be(3);
        stats.Successful.Should().Be(2);
        stats.Failed.Should().Be(1);
        stats.FavoriteDestination.Should().Be("ISS");
        stats.BestScore.Should().Be(90);
        stats.WorstScore.Should().Be(40);
        stats.MissionsByDestination["ISS"].Should().Be(2);
        stats.MissionsByDestination["SSO"].Should().Be(1);
    }

    public void Dispose() => _db.Dispose();
}
```

- [ ] **Step 1.7.5: Rodar testes — confirmar GREEN**

```powershell
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --logger "console;verbosity=normal"
```

Esperado: `Failed: 0, Passed: ≥20`.

Se algum teste falhar:
1. Ler mensagem de erro — geralmente indica o que está errado na implementação
2. Corrigir o código do repository (NÃO o teste)
3. Re-rodar até todos passarem

- [ ] **Step 1.7.6: Commit**

```powershell
git add MissionClear.Tests/Data/UserRepositoryTests.cs MissionClear.Tests/Data/RefreshTokenRepositoryTests.cs MissionClear.Tests/Data/MissionRepositoryTests.cs
git commit -m "test(repos): UserRepository, RefreshTokenRepository, MissionRepository — happy path + edge cases"
```

---

## Critérios de Sucesso

- [ ] `MissionClear.Api.csproj` contém `Pomelo.EntityFrameworkCore.MySql 8.0.2` e `Aspire.Pomelo.EntityFrameworkCore.MySql`
- [ ] `MissionClear.Api.csproj` NÃO contém `Microsoft.EntityFrameworkCore.Sqlite`
- [ ] `Entities/UserEntity.cs` tem campo `Role` (string, default "Researcher") com `[Column("role")]`
- [ ] `Entities/MissionEntity.cs` tem campo `ObstaclesJson` (string, default "[]")
- [ ] Todas as entidades usam `[Column("snake_case")]` em cada propriedade
- [ ] `Data/AppDbContext.cs` registra MySQL provider, índice único em `email` e `token`, cascade deletes
- [ ] `Data/AppDbContextFactory.cs` implementa `IDesignTimeDbContextFactory<AppDbContext>` e lê connection string de `appsettings.Development.json`
- [ ] `appsettings.Development.json` está no `.gitignore` (nunca commitado)
- [ ] `Program.cs` usa `builder.AddMySqlDbContext<AppDbContext>("missionclear")`
- [ ] `Data/Repositories/` contém 3 interfaces + 3 implementações
- [ ] `IMissionRepository.FindByUserIdAsync` aceita parâmetros `page`, `limit`, `status`, `destination`, `sort`
- [ ] `MissionRepository` suporta sort `created_at_desc`, `score_desc`, `risk_score_asc`
- [ ] `MissionPageResult` record e `MissionStatsProjection` record definidos
- [ ] `Data/Migrations/` contém `*_InitialCreate.cs` e `AppDbContextModelSnapshot.cs`
- [ ] `dotnet build MissionClear.Api/MissionClear.Api.csproj` — 0 erros
- [ ] `dotnet test` passa ≥ 20 testes em `UserRepositoryTests`, `RefreshTokenRepositoryTests`, `MissionRepositoryTests`
- [ ] 6 commits no histórico desta fase

---

## Riscos & Mitigações

| Risco | Mitigação |
|---|---|
| `dotnet ef` não encontrado no PATH | Step 0.1: instalar global tool antes de qualquer migration |
| MySQL não acessível na porta 3306 | Step 1.4.1: docker run com credenciais explícitas |
| `ServerVersion.AutoDetect` falha sem DB rodando | AppDbContextFactory só é chamado pelo EF CLI — não afeta testes ou runtime via Aspire |
| InMemory ignora índices únicos | Testes cobrem comportamento funcional; índice único validado em produção via migration |
| `appsettings.Development.json` commitado por engano | Arquivo no `.gitignore` (Step 1.1.3); verificar com `git status` antes de push |
| `ExecuteUpdateAsync`/`ExecuteDeleteAsync` não suportados pelo InMemory | Estes métodos funcionam com InMemory no EF Core 7+; se falhar, substituir por `SaveChanges` individual nos testes |
| Migration gerada incorreta após mudança de schema | `dotnet ef migrations remove`, corrigir entidade, repetir `migrations add` |
| BCrypt não instalado no projeto de testes | `MissionClear.Tests` referencia `MissionClear.Api.csproj` que já tem `BCrypt.Net-Next` — disponível automaticamente |

---

## Arquivos Criados/Modificados

| Arquivo | Ação |
|---------|------|
| `MissionClear.Api/MissionClear.Api.csproj` | Modificar (trocar SQLite por Pomelo) |
| `MissionClear.Api/Entities/UserEntity.cs` | Criar/Substituir (Role + snake_case) |
| `MissionClear.Api/Entities/RefreshTokenEntity.cs` | Criar/Substituir (snake_case) |
| `MissionClear.Api/Entities/MissionEntity.cs` | Criar/Substituir (ObstaclesJson + snake_case) |
| `MissionClear.Api/Data/AppDbContext.cs` | Criar/Substituir (MySQL provider) |
| `MissionClear.Api/Data/AppDbContextFactory.cs` | Criar (design-time factory) |
| `MissionClear.Api/Data/Migrations/*` | Auto-gerado por dotnet-ef |
| `MissionClear.Api/Data/Repositories/IUserRepository.cs` | Criar |
| `MissionClear.Api/Data/Repositories/IRefreshTokenRepository.cs` | Criar |
| `MissionClear.Api/Data/Repositories/IMissionRepository.cs` | Criar (com MissionPageResult, MissionStatsProjection) |
| `MissionClear.Api/Data/Repositories/UserRepository.cs` | Criar |
| `MissionClear.Api/Data/Repositories/RefreshTokenRepository.cs` | Criar |
| `MissionClear.Api/Data/Repositories/MissionRepository.cs` | Criar (sort: 3 modos) |
| `MissionClear.Api/appsettings.json` | Modificar (ConnectionStrings placeholder) |
| `MissionClear.Api/appsettings.Development.json` | Criar (credenciais dev — NÃO commitar) |
| `MissionClear.Api/Program.cs` | Modificar (AddMySqlDbContext + DI repos) |
| `MissionClear.Tests/Data/UserRepositoryTests.cs` | Criar (7 testes) |
| `MissionClear.Tests/Data/RefreshTokenRepositoryTests.cs` | Criar (6 testes) |
| `MissionClear.Tests/Data/MissionRepositoryTests.cs` | Criar (10 testes) |
| `.gitignore` | Modificar (appsettings.Development.json + *.db) |
