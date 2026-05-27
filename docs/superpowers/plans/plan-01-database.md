# Plan 01 — Database (EF Core + SQLite)

**Execution order:** After plan-00-scaffolding. Parallel with plan-02-models.
**Estimated time:** 20 minutes.
**Goal:** Definir entidades EF Core (User, RefreshToken, Mission), DbContext com índices únicos e cascade deletes, criar e aplicar migration inicial no SQLite, validar via testes xUnit com InMemory provider.
**Dependencies:** `plan-00-scaffolding.md` concluído — solução criada, pacotes `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` instalados em `MissionClear.Api`; `Microsoft.EntityFrameworkCore.InMemory` + `FluentAssertions` em `MissionClear.Tests`.
**Unlocks:** `plan-04-auth.md` (precisa de UserEntity + RefreshTokenEntity), `plan-06-history-dashboard.md` (precisa de MissionEntity).

> **Separação de responsabilidades:**
> - `Entities/` — classes EF Core mapeadas para tabelas. Nunca expostas direto na API.
> - `Repositories/` — acesso ao banco via EF Core. Implementam interfaces em `Interfaces/`.
> - `Data/AppDbContext.cs` — só configura o DbContext, índices, relacionamentos.
> - `Services/` — usa repositórios via interface. Nunca acessa `AppDbContext` diretamente.
>
> Arquivos deste plano: `Entities/UserEntity.cs`, `Entities/RefreshTokenEntity.cs`, `Entities/MissionEntity.cs`, `Data/AppDbContext.cs`.
> Repositórios (`UserRepository.cs` etc.) são criados no plan-04.

---

## Pré-requisitos de Ambiente

- [ ] **Step 0.1: Instalar dotnet-ef CLI (uma vez por máquina)**

```bash
dotnet tool install --global dotnet-ef --version 8.*
# Se já instalado:
dotnet tool update --global dotnet-ef --version 8.*
```

- [ ] **Step 0.2: Verificar versão**

```bash
dotnet ef --version
# Esperado: 8.x.x
```

---

## Task 1.1 — Entidades

**Files:**
- Create: `MissionClear.Api/Entities/UserEntity.cs`
- Create: `MissionClear.Api/Entities/RefreshTokenEntity.cs`
- Create: `MissionClear.Api/Entities/MissionEntity.cs`

- [ ] **Step 1.1.1: `Entities/UserEntity.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Entities;

public class UserEntity
{
    [Key]
    public string Id { get; set; } = "usr_" + Guid.NewGuid().ToString("N");

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RefreshTokenEntity> RefreshTokens { get; set; } = new List<RefreshTokenEntity>();
    public ICollection<MissionEntity> Missions { get; set; } = new List<MissionEntity>();
}
```

- [ ] **Step 1.1.2: `Entities/RefreshTokenEntity.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Entities;

public class RefreshTokenEntity
{
    [Key]
    public string Id { get; set; } = "rtk_" + Guid.NewGuid().ToString("N");

    [Required]
    [MaxLength(512)]
    public string Token { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRevoked { get; set; } = false;

    public UserEntity User { get; set; } = null!;
}
```

- [ ] **Step 1.1.3: `Entities/MissionEntity.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Api.Entities;

public class MissionEntity
{
    [Key]
    public string Id { get; set; } = "msn_" + Guid.NewGuid().ToString("N");

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Destination { get; set; } = string.Empty; // ISS | LEO_GENERIC | SSO

    public DateTime DepartureTime { get; set; }
    public DateTime ArrivalTime { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = string.Empty; // success | failure | aborted

    public int MissionScore { get; set; }
    public double RiskScore { get; set; }
    public double DeltaVKmS { get; set; }
    public int ObstaclesEncountered { get; set; }

    [Required]
    public string ObstaclesJson { get; set; } = "[]";

    [Required]
    public string ScoreBreakdownJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UserEntity User { get; set; } = null!;
}
```

- [ ] **Step 1.1.4: Build incremental**

```bash
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet build MissionClear.Api/MissionClear.Api.csproj
```

Esperado: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 1.1.5: Commit**

```bash
git add MissionClear.Api/Entities/
git commit -m "feat: entities UserEntity, RefreshTokenEntity, MissionEntity"
```

---

## Task 1.2 — AppDbContext

**Files:**
- Create: `MissionClear.Api/Data/AppDbContext.cs`
- Modify: `MissionClear.Api/Program.cs` (registrar DbContext)
- Modify: `MissionClear.Api/appsettings.json` (connection string)
- Modify: `MissionClear.Api/appsettings.Development.json` (connection string)

- [ ] **Step 1.2.1: `Data/AppDbContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Entities;

namespace MissionClear.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<MissionEntity> Missions => Set<MissionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---- UserEntity ----
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();

            entity.HasMany(u => u.RefreshTokens)
                  .WithOne(rt => rt.User)
                  .HasForeignKey(rt => rt.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(u => u.Missions)
                  .WithOne(m => m.User)
                  .HasForeignKey(m => m.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- RefreshTokenEntity ----
        modelBuilder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(rt => rt.Id);
            entity.HasIndex(rt => rt.Token).IsUnique();
            entity.HasIndex(rt => rt.UserId);
        });

        // ---- MissionEntity ----
        modelBuilder.Entity<MissionEntity>(entity =>
        {
            entity.ToTable("missions");
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => m.UserId);
            entity.HasIndex(m => m.CreatedAt);
        });
    }
}
```

- [ ] **Step 1.2.2: `appsettings.json` — adicionar bloco ConnectionStrings**

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
    "DefaultConnection": "Data Source=missionclear.db"
  }
}
```

- [ ] **Step 1.2.3: `appsettings.Development.json` — connection string dev**

Substituir conteúdo completo de `MissionClear.Api/appsettings.Development.json`:

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
    "DefaultConnection": "Data Source=missionclear-dev.db"
  }
}
```

- [ ] **Step 1.2.4: `Program.cs` — registrar AppDbContext**

Localize a linha `var builder = WebApplication.CreateBuilder(args);` e logo após o bloco de `AddControllers()` (ou antes do `builder.Build()`) insira:

```csharp
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;

// ... dentro da configuração de services:
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection")
    )
);
```

- [ ] **Step 1.2.5: Adicionar `missionclear*.db*` ao `.gitignore`**

Append ao arquivo `.gitignore` na raiz do repositório:

```
# SQLite local DBs
*.db
*.db-shm
*.db-wal
missionclear*.db
```

- [ ] **Step 1.2.6: Build**

```bash
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet build MissionClear.Api/MissionClear.Api.csproj
```

Esperado: `Build succeeded. 0 Error(s).`

- [ ] **Step 1.2.7: Commit**

```bash
git add MissionClear.Api/Data/AppDbContext.cs MissionClear.Api/appsettings.json MissionClear.Api/appsettings.Development.json MissionClear.Api/Program.cs .gitignore
git commit -m "feat: AppDbContext with unique indexes and cascade deletes"
```

---

## Task 1.3 — Migration Inicial

**Files:**
- Create: `MissionClear.Api/Data/Migrations/*` (auto-geradas)

- [ ] **Step 1.3.1: Criar migration `InitialCreate`**

```bash
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C\MissionClear.Api"
dotnet ef migrations add InitialCreate --output-dir Data/Migrations
```

Esperado: três arquivos criados em `Data/Migrations/`:
- `<timestamp>_InitialCreate.cs`
- `<timestamp>_InitialCreate.Designer.cs`
- `AppDbContextModelSnapshot.cs`

- [ ] **Step 1.3.2: Inspeção rápida — confirmar tabelas geradas**

Abrir `Data/Migrations/<timestamp>_InitialCreate.cs` e verificar manualmente:
- `CreateTable("users")` com coluna `Email` e `CreateIndex` único em `Email`
- `CreateTable("refresh_tokens")` com FK em `UserId` e `onDelete: ReferentialAction.Cascade`
- `CreateTable("missions")` com FK em `UserId` e `onDelete: ReferentialAction.Cascade`
- `CreateIndex` único em `refresh_tokens.Token`

Se algo estiver errado: deletar a pasta `Data/Migrations`, corrigir `AppDbContext.OnModelCreating`, repetir Step 1.3.1.

- [ ] **Step 1.3.3: Aplicar migration ao banco local**

```bash
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C\MissionClear.Api"
dotnet ef database update
```

Esperado: arquivo `missionclear-dev.db` (ou `missionclear.db`) criado na pasta `MissionClear.Api/`.

- [ ] **Step 1.3.4: Verificar schema via dotnet-ef**

```bash
dotnet ef dbcontext info
```

Esperado: lista do provider `Microsoft.EntityFrameworkCore.Sqlite` e o connection string atual.

- [ ] **Step 1.3.5: Commit**

```bash
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
git add MissionClear.Api/Data/Migrations/
git commit -m "feat: initial EF Core migration (users, refresh_tokens, missions)"
```

---

## Task 1.4 — Testes (xUnit + InMemory)

**Files:**
- Create: `MissionClear.Tests/Data/AppDbContextTests.cs`

- [ ] **Step 1.4.1: Confirmar pacotes de teste**

```bash
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C\MissionClear.Tests"
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.*
dotnet add package FluentAssertions --version 6.*
```

(Idempotente — se já instalado por plan-00, comando é no-op.)

- [ ] **Step 1.4.2: `MissionClear.Tests/Data/AppDbContextTests.cs`**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Entities;
using Xunit;

namespace MissionClear.Tests.Data;

public class AppDbContextTests
{
    private static AppDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task UserEntity_CanBeCreatedAndRetrieved()
    {
        using var ctx = CreateContext($"test_user_{Guid.NewGuid():N}");
        await ctx.Database.EnsureCreatedAsync();

        ctx.Users.Add(new UserEntity
        {
            Id = "usr_test",
            Email = "a@b.com",
            PasswordHash = "hash",
            DisplayName = "Test",
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var user = await ctx.Users.FindAsync("usr_test");
        user.Should().NotBeNull();
        user!.Email.Should().Be("a@b.com");
        user.DisplayName.Should().Be("Test");
    }

    [Fact]
    public async Task UserEntity_HasAutoGeneratedIdWithUsrPrefix()
    {
        using var ctx = CreateContext($"test_userid_{Guid.NewGuid():N}");
        await ctx.Database.EnsureCreatedAsync();

        var user = new UserEntity
        {
            Email = "auto@id.com",
            PasswordHash = "h",
            DisplayName = "Auto",
            CreatedAt = DateTime.UtcNow
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        user.Id.Should().StartWith("usr_");
        user.Id.Length.Should().Be(36); // "usr_" (4) + 32 hex chars
    }

    [Fact]
    public async Task RefreshToken_IsLinkedToUser()
    {
        using var ctx = CreateContext($"test_rtk_{Guid.NewGuid():N}");
        await ctx.Database.EnsureCreatedAsync();

        var user = new UserEntity
        {
            Id = "usr_owner",
            Email = "owner@x.com",
            PasswordHash = "h",
            DisplayName = "Owner",
            CreatedAt = DateTime.UtcNow
        };
        ctx.Users.Add(user);

        ctx.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = "rtk_1",
            Token = "secret-token-value",
            UserId = "usr_owner",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        });
        await ctx.SaveChangesAsync();

        var retrieved = await ctx.RefreshTokens
            .Include(rt => rt.User)
            .FirstAsync(rt => rt.Id == "rtk_1");

        retrieved.Token.Should().Be("secret-token-value");
        retrieved.User.Should().NotBeNull();
        retrieved.User.Email.Should().Be("owner@x.com");
    }

    [Fact]
    public async Task MissionEntity_PersistsAllFieldsIncludingJson()
    {
        using var ctx = CreateContext($"test_msn_{Guid.NewGuid():N}");
        await ctx.Database.EnsureCreatedAsync();

        ctx.Users.Add(new UserEntity
        {
            Id = "usr_pilot",
            Email = "pilot@x.com",
            PasswordHash = "h",
            DisplayName = "Pilot",
            CreatedAt = DateTime.UtcNow
        });

        var departure = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var arrival = departure.AddHours(6);

        ctx.Missions.Add(new MissionEntity
        {
            Id = "msn_1",
            UserId = "usr_pilot",
            Destination = "ISS",
            DepartureTime = departure,
            ArrivalTime = arrival,
            Status = "success",
            MissionScore = 87,
            RiskScore = 0.03,
            DeltaVKmS = 9.4,
            ObstaclesEncountered = 2,
            ObstaclesJson = "[{\"debris_id\":\"1234\",\"closest_approach_km\":4.2}]",
            ScoreBreakdownJson = "{\"efficiency_score\":90,\"safety_score\":85,\"total\":87}",
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var mission = await ctx.Missions.FindAsync("msn_1");
        mission.Should().NotBeNull();
        mission!.Destination.Should().Be("ISS");
        mission.MissionScore.Should().Be(87);
        mission.RiskScore.Should().BeApproximately(0.03, 1e-9);
        mission.DeltaVKmS.Should().BeApproximately(9.4, 1e-9);
        mission.ObstaclesEncountered.Should().Be(2);
        mission.ObstaclesJson.Should().Contain("1234");
        mission.ScoreBreakdownJson.Should().Contain("efficiency_score");
        mission.DepartureTime.Should().Be(departure);
        mission.ArrivalTime.Should().Be(arrival);
    }

    [Fact]
    public async Task DeletingUser_CascadesToRefreshTokensAndMissions()
    {
        using var ctx = CreateContext($"test_cascade_{Guid.NewGuid():N}");
        await ctx.Database.EnsureCreatedAsync();

        var user = new UserEntity
        {
            Id = "usr_cascade",
            Email = "cascade@x.com",
            PasswordHash = "h",
            DisplayName = "Cascade",
            CreatedAt = DateTime.UtcNow
        };
        ctx.Users.Add(user);
        ctx.RefreshTokens.Add(new RefreshTokenEntity
        {
            Id = "rtk_c",
            Token = "tok-cascade",
            UserId = "usr_cascade",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow
        });
        ctx.Missions.Add(new MissionEntity
        {
            Id = "msn_c",
            UserId = "usr_cascade",
            Destination = "LEO_GENERIC",
            DepartureTime = DateTime.UtcNow,
            ArrivalTime = DateTime.UtcNow.AddHours(2),
            Status = "success",
            MissionScore = 50,
            RiskScore = 0.1,
            DeltaVKmS = 9.0,
            ObstaclesEncountered = 0,
            ObstaclesJson = "[]",
            ScoreBreakdownJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        ctx.Users.Remove(user);
        await ctx.SaveChangesAsync();

        (await ctx.Users.FindAsync("usr_cascade")).Should().BeNull();
        (await ctx.RefreshTokens.FindAsync("rtk_c")).Should().BeNull();
        (await ctx.Missions.FindAsync("msn_c")).Should().BeNull();
    }

    [Fact]
    public async Task Email_UniqueIndex_PreventsDuplicates_OnSqlite()
    {
        // InMemory provider ignora índices únicos — usar SQLite real aqui.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source=test_unique_{Guid.NewGuid():N}.db")
            .Options;

        using var ctx = new AppDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        try
        {
            ctx.Users.Add(new UserEntity
            {
                Id = "usr_u1",
                Email = "dup@x.com",
                PasswordHash = "h",
                DisplayName = "U1",
                CreatedAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();

            ctx.Users.Add(new UserEntity
            {
                Id = "usr_u2",
                Email = "dup@x.com",
                PasswordHash = "h",
                DisplayName = "U2",
                CreatedAt = DateTime.UtcNow
            });

            Func<Task> act = async () => await ctx.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await ctx.Database.EnsureDeletedAsync();
        }
    }
}
```

- [ ] **Step 1.4.3: Rodar testes**

```bash
cd "C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C"
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --logger "console;verbosity=normal"
```

Esperado: `Passed! - Failed: 0, Passed: 6, Skipped: 0`.

- [ ] **Step 1.4.4: Commit**

```bash
git add MissionClear.Tests/Data/AppDbContextTests.cs
git commit -m "test: AppDbContext entities, cascade deletes and unique index"
```

---

## Critérios de Sucesso

- [ ] `MissionClear.Api/Entities/` contém `UserEntity.cs`, `RefreshTokenEntity.cs`, `MissionEntity.cs`
- [ ] `MissionClear.Api/Data/AppDbContext.cs` registrado em `Program.cs` via `AddDbContext<AppDbContext>` com SQLite
- [ ] `dotnet build MissionClear.Api/MissionClear.Api.csproj` retorna 0 erros
- [ ] `dotnet ef migrations add InitialCreate` gerou 3 arquivos em `Data/Migrations/`
- [ ] `dotnet ef database update` criou `missionclear-dev.db` localmente
- [ ] `dotnet test` passa todos os 6 testes em `AppDbContextTests`
- [ ] Índice único em `users.Email` validado por teste com SQLite real
- [ ] Cascade delete validado: remover User remove RefreshTokens e Missions
- [ ] `.gitignore` exclui `*.db`, `*.db-shm`, `*.db-wal`
- [ ] 4 commits no histórico: entidades, DbContext, migration, testes

---

## Riscos & Mitigações

| Risco | Mitigação |
|---|---|
| `dotnet ef` não está no PATH | Step 0.1 instala global tool antes de qualquer migration |
| InMemory provider ignora índices únicos | Teste `Email_UniqueIndex_PreventsDuplicates_OnSqlite` usa SQLite real |
| Migration gerada errada após mudança de schema | Inspeção manual no Step 1.3.2; deletar pasta e refazer |
| Connection string ausente em runtime | `throw new InvalidOperationException` no Step 1.2.4 falha rápido |
| Arquivo `.db` commitado por engano | `.gitignore` no Step 1.2.5 cobre `*.db`, `*.db-shm`, `*.db-wal` |
| Cascade não dispara em produção SQLite | Configurado via Fluent API `OnDelete(DeleteBehavior.Cascade)` — gera FK com `ON DELETE CASCADE` |

---

## Arquivos Criados/Modificados

| Arquivo | Ação |
|---------|------|
| `MissionClear.Api/Entities/UserEntity.cs` | Criar |
| `MissionClear.Api/Entities/RefreshTokenEntity.cs` | Criar |
| `MissionClear.Api/Entities/MissionEntity.cs` | Criar |
| `MissionClear.Api/Data/AppDbContext.cs` | Criar |
| `MissionClear.Api/Data/Migrations/*` | Auto-gerado por dotnet-ef |
| `MissionClear.Api/appsettings.json` | Modificar (ConnectionStrings) |
| `MissionClear.Api/appsettings.Development.json` | Modificar (ConnectionStrings) |
| `MissionClear.Api/Program.cs` | Modificar (AddDbContext) |
| `MissionClear.Tests/Data/AppDbContextTests.cs` | Criar |
| `.gitignore` | Modificar (excluir *.db) |
