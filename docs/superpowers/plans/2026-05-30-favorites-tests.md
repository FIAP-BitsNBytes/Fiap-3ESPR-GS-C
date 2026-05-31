# Favorites — Plano de Testes Completo

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cobrir o fluxo de favoritos em todas as camadas — entidade, repositório, serviço, rota HTTP, autenticação e persistência no banco.

**Architecture:** Três camadas de teste: (1) repositório com EF InMemory isolado por `Guid.NewGuid()` por instância; (2) serviço via Moq já existente em `UserServiceTests`; (3) integração HTTP via `TestWebApplicationFactory` + `HttpClient` já existente em `MobileContractTests`. O repositório é o único layer sem cobertura.

**Tech Stack:** xUnit, FluentAssertions, Moq, EF Core InMemory, `WebApplicationFactory<ApiMarker>`, `System.Text.Json`, BCrypt.Net.

---

## Inventário de cobertura atual

| Camada | Arquivo | Status |
|---|---|---|
| Entidade | `UserFavoriteDebrisEntity`, `UserSavedWindowEntity` | ❌ sem teste |
| Repositório | `FavoritesRepository` | ❌ sem teste |
| Serviço | `UserService.GetFavoritesAsync/UpdateFavoritesAsync` | ✅ `UserServiceTests.cs` (12 testes) |
| HTTP — Auth | `[Authorize]` em `UsersController` | ✅ `MobileContractTests.cs` CONTRACT 10 |
| HTTP — Shape | snake_case, campos corretos | ✅ `MobileContractTests.cs` CONTRACT 10 |
| HTTP — Lifecycle | PUT→GET→persist | ✅ `MobileContractTests.cs` CONTRACT 10 |
| HTTP — Semântica | null=preserve, []=clear, dedup | ✅ `MobileContractTests.cs` CONTRACT 10 |

**Gap:** apenas `FavoritesRepositoryTests.cs` está faltando.

---

## Mapa de arquivos

| Ação | Arquivo |
|---|---|
| **Criar** | `MissionClear.Tests/Data/FavoritesRepositoryTests.cs` |
| Verificar rodando | `MissionClear.Tests/Services/UserServiceTests.cs` |
| Verificar rodando | `MissionClear.Tests/Integration/MobileContractTests.cs` |

---

## Task 1: FavoritesRepositoryTests — camada de dados

**Files:**
- Create: `MissionClear.Tests/Data/FavoritesRepositoryTests.cs`

### 1.1 — Escrever o arquivo de testes completo

- [ ] **Step 1: Criar `FavoritesRepositoryTests.cs`**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MissionClear.Api.Data;
using MissionClear.Api.Data.Repositories;
using MissionClear.Api.Entities;
using Xunit;

namespace MissionClear.Tests.Data;

/// <summary>
/// Testa FavoritesRepository contra EF InMemory.
/// Cada instância de teste usa um banco isolado (Guid único) para evitar estado compartilhado.
/// </summary>
public sealed class FavoritesRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly FavoritesRepository _sut;

    public FavoritesRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _sut = new FavoritesRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static UserEntity MakeUser()
    {
        var user = new UserEntity
        {
            Email        = $"{Guid.NewGuid():N}@test.com",
            DisplayName  = "Test",
            PasswordHash = "hash",
        };
        return user;
    }

    private async Task<Guid> SeedUserAsync()
    {
        var user = MakeUser();
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user.Id;
    }

    // ── GetDebrisAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDebrisAsync_NewUser_ReturnsEmptyList()
    {
        // Arrange
        var userId = await SeedUserAsync();

        // Act
        var result = await _sut.GetDebrisAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDebrisAsync_WithEntries_ReturnsAll()
    {
        // Arrange
        var userId = await SeedUserAsync();
        _context.FavoriteDebris.AddRange(
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "25544", SavedAt = DateTime.UtcNow },
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "37820", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetDebrisAsync(userId);

        // Assert
        result.Should().HaveCount(2);
        result.Select(d => d.DebrisId).Should().BeEquivalentTo(["25544", "37820"]);
    }

    [Fact]
    public async Task GetDebrisAsync_OrdersBySavedAt_Ascending()
    {
        // Arrange
        var userId = await SeedUserAsync();
        var t0     = DateTime.UtcNow.AddMinutes(-5);
        var t1     = DateTime.UtcNow;

        _context.FavoriteDebris.AddRange(
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "NEWER", SavedAt = t1 },
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "OLDER", SavedAt = t0 });
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetDebrisAsync(userId);

        // Assert — older entry comes first
        result[0].DebrisId.Should().Be("OLDER");
        result[1].DebrisId.Should().Be("NEWER");
    }

    [Fact]
    public async Task GetDebrisAsync_DoesNotReturnOtherUsersDebris()
    {
        // Arrange
        var userId1 = await SeedUserAsync();
        var userId2 = await SeedUserAsync();

        _context.FavoriteDebris.Add(
            new UserFavoriteDebrisEntity { UserId = userId2, DebrisId = "OTHER", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetDebrisAsync(userId1);

        // Assert
        result.Should().BeEmpty("must not leak another user's favorites");
    }

    // ── ReplaceDebrisAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDebrisAsync_WithNew_InsertsEntries()
    {
        // Arrange
        var userId = await SeedUserAsync();

        // Act
        await _sut.ReplaceDebrisAsync(userId, ["25544", "37820"]);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.FavoriteDebris.Where(f => f.UserId == userId).ToListAsync();
        saved.Should().HaveCount(2);
        saved.Select(d => d.DebrisId).Should().BeEquivalentTo(["25544", "37820"]);
    }

    [Fact]
    public async Task ReplaceDebrisAsync_RemovesExistingBeforeInserting()
    {
        // Arrange
        var userId = await SeedUserAsync();
        _context.FavoriteDebris.Add(
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "OLD", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        // Act
        await _sut.ReplaceDebrisAsync(userId, ["NEW"]);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.FavoriteDebris.Where(f => f.UserId == userId).ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].DebrisId.Should().Be("NEW", "OLD must have been removed");
    }

    [Fact]
    public async Task ReplaceDebrisAsync_Deduplicates_BeforeInserting()
    {
        // Arrange
        var userId = await SeedUserAsync();

        // Act
        await _sut.ReplaceDebrisAsync(userId, ["DUP", "DUP", "DUP", "UNIQUE"]);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.FavoriteDebris.Where(f => f.UserId == userId).ToListAsync();
        saved.Should().HaveCount(2, "DUP must appear only once");
        saved.Select(d => d.DebrisId).Should().BeEquivalentTo(["DUP", "UNIQUE"]);
    }

    [Fact]
    public async Task ReplaceDebrisAsync_IgnoresBlankAndWhitespaceIds()
    {
        // Arrange
        var userId = await SeedUserAsync();

        // Act
        await _sut.ReplaceDebrisAsync(userId, ["25544", "", "  ", "37820"]);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.FavoriteDebris.Where(f => f.UserId == userId).ToListAsync();
        saved.Should().HaveCount(2, "blank/whitespace IDs must be filtered out");
        saved.Select(d => d.DebrisId).Should().NotContain("");
        saved.Select(d => d.DebrisId).Should().NotContain("  ");
    }

    [Fact]
    public async Task ReplaceDebrisAsync_EnforcesMax500Limit()
    {
        // Arrange
        var userId = await SeedUserAsync();
        var ids    = Enumerable.Range(1, 600).Select(i => $"ID_{i:D4}");

        // Act
        await _sut.ReplaceDebrisAsync(userId, ids);
        await _sut.SaveChangesAsync();

        // Assert
        var count = await _context.FavoriteDebris.CountAsync(f => f.UserId == userId);
        count.Should().Be(500, "repository must cap at 500 entries");
    }

    [Fact]
    public async Task ReplaceDebrisAsync_WithEmptyList_ClearsAllDebris()
    {
        // Arrange
        var userId = await SeedUserAsync();
        _context.FavoriteDebris.Add(
            new UserFavoriteDebrisEntity { UserId = userId, DebrisId = "EXISTING", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        // Act
        await _sut.ReplaceDebrisAsync(userId, []);
        await _sut.SaveChangesAsync();

        // Assert
        var count = await _context.FavoriteDebris.CountAsync(f => f.UserId == userId);
        count.Should().Be(0, "empty list must clear all favorites");
    }

    [Fact]
    public async Task ReplaceDebrisAsync_SetsUserId_Correctly()
    {
        // Arrange
        var userId = await SeedUserAsync();

        // Act
        await _sut.ReplaceDebrisAsync(userId, ["99999"]);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.FavoriteDebris.FirstAsync(f => f.UserId == userId);
        saved.UserId.Should().Be(userId);
    }

    // ── GetWindowsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetWindowsAsync_NewUser_ReturnsEmptyList()
    {
        // Arrange
        var userId = await SeedUserAsync();

        // Act
        var result = await _sut.GetWindowsAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWindowsAsync_WithEntries_ReturnsAll()
    {
        // Arrange
        var userId = await SeedUserAsync();
        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId      = userId,
            WindowId    = "ISS_W1",
            Destination = "ISS",
            WindowJson  = """{"id":"ISS_W1","destination":"ISS"}""",
            SavedAt     = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetWindowsAsync(userId);

        // Assert
        result.Should().HaveCount(1);
        result[0].WindowId.Should().Be("ISS_W1");
        result[0].Destination.Should().Be("ISS");
    }

    [Fact]
    public async Task GetWindowsAsync_OrdersBySavedAt_Ascending()
    {
        // Arrange
        var userId = await SeedUserAsync();
        var t0     = DateTime.UtcNow.AddMinutes(-5);
        var t1     = DateTime.UtcNow;

        _context.SavedWindows.AddRange(
            new UserSavedWindowEntity { UserId = userId, WindowId = "W_NEWER", Destination = "ISS",
                                        WindowJson = "{}", SavedAt = t1 },
            new UserSavedWindowEntity { UserId = userId, WindowId = "W_OLDER", Destination = "LEO_GENERIC",
                                        WindowJson = "{}", SavedAt = t0 });
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetWindowsAsync(userId);

        // Assert
        result[0].WindowId.Should().Be("W_OLDER");
        result[1].WindowId.Should().Be("W_NEWER");
    }

    [Fact]
    public async Task GetWindowsAsync_DoesNotReturnOtherUsersWindows()
    {
        // Arrange
        var userId1 = await SeedUserAsync();
        var userId2 = await SeedUserAsync();

        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId = userId2, WindowId = "THEIRS", Destination = "SSO",
            WindowJson = "{}", SavedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        // Act
        var result = await _sut.GetWindowsAsync(userId1);

        // Assert
        result.Should().BeEmpty("must not leak another user's saved windows");
    }

    // ── ReplaceWindowsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceWindowsAsync_WithNew_InsertsEntries()
    {
        // Arrange
        var userId = await SeedUserAsync();
        var newWindow = MakeWindowEntity(userId, "ISS_W1", "ISS");

        // Act
        await _sut.ReplaceWindowsAsync(userId, [newWindow]);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.SavedWindows.Where(w => w.UserId == userId).ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].WindowId.Should().Be("ISS_W1");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_RemovesExistingBeforeInserting()
    {
        // Arrange
        var userId = await SeedUserAsync();
        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId = userId, WindowId = "OLD_W", Destination = "SSO",
            WindowJson = "{}", SavedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        // Act
        await _sut.ReplaceWindowsAsync(userId, [MakeWindowEntity(userId, "NEW_W", "ISS")]);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.SavedWindows.Where(w => w.UserId == userId).ToListAsync();
        saved.Should().HaveCount(1);
        saved[0].WindowId.Should().Be("NEW_W", "OLD_W must have been removed");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_WithEmptyList_ClearsAllWindows()
    {
        // Arrange
        var userId = await SeedUserAsync();
        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId = userId, WindowId = "EXISTING_W", Destination = "ISS",
            WindowJson = "{}", SavedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        // Act
        await _sut.ReplaceWindowsAsync(userId, []);
        await _sut.SaveChangesAsync();

        // Assert
        var count = await _context.SavedWindows.CountAsync(w => w.UserId == userId);
        count.Should().Be(0, "empty list must clear all saved windows");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_EnforcesMax200Limit()
    {
        // Arrange
        var userId  = await SeedUserAsync();
        var windows = Enumerable.Range(1, 250)
            .Select(i => MakeWindowEntity(userId, $"W_{i:D4}", "ISS"));

        // Act
        await _sut.ReplaceWindowsAsync(userId, windows);
        await _sut.SaveChangesAsync();

        // Assert
        var count = await _context.SavedWindows.CountAsync(w => w.UserId == userId);
        count.Should().Be(200, "repository must cap at 200 entries");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_SetsUserId_Correctly()
    {
        // Arrange
        var userId = await SeedUserAsync();

        // Act
        await _sut.ReplaceWindowsAsync(userId, [MakeWindowEntity(userId, "W1", "ISS")]);
        await _sut.SaveChangesAsync();

        // Assert
        var saved = await _context.SavedWindows.FirstAsync(w => w.UserId == userId);
        saved.UserId.Should().Be(userId);
    }

    // ── SaveChangesAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task SaveChangesAsync_PersistsNewFavorites()
    {
        // Arrange
        var userId = await SeedUserAsync();
        await _sut.ReplaceDebrisAsync(userId, ["SAVE_TEST"]);

        // Act
        await _sut.SaveChangesAsync();

        // Assert — fresh query on the context to bypass tracking cache
        var count = await _context.FavoriteDebris.CountAsync(f => f.DebrisId == "SAVE_TEST");
        count.Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutChanges_DoesNotThrow()
    {
        // Arrange / Act
        var act = async () => await _sut.SaveChangesAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ── Isolation between users ───────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDebrisAsync_OnlyAffects_TargetUser()
    {
        // Arrange — two users, each with existing debris
        var userId1 = await SeedUserAsync();
        var userId2 = await SeedUserAsync();

        _context.FavoriteDebris.AddRange(
            new UserFavoriteDebrisEntity { UserId = userId1, DebrisId = "U1_ID", SavedAt = DateTime.UtcNow },
            new UserFavoriteDebrisEntity { UserId = userId2, DebrisId = "U2_ID", SavedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        // Act — replace only user1's debris
        await _sut.ReplaceDebrisAsync(userId1, ["U1_NEW"]);
        await _sut.SaveChangesAsync();

        // Assert — user2's debris untouched
        var u2debris = await _context.FavoriteDebris.Where(f => f.UserId == userId2).ToListAsync();
        u2debris.Should().HaveCount(1);
        u2debris[0].DebrisId.Should().Be("U2_ID", "replacing user1's debris must not affect user2");
    }

    [Fact]
    public async Task ReplaceWindowsAsync_OnlyAffects_TargetUser()
    {
        // Arrange
        var userId1 = await SeedUserAsync();
        var userId2 = await SeedUserAsync();

        _context.SavedWindows.Add(new UserSavedWindowEntity
        {
            UserId = userId2, WindowId = "U2_W", Destination = "ISS",
            WindowJson = "{}", SavedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        // Act — replace only user1's windows
        await _sut.ReplaceWindowsAsync(userId1, [MakeWindowEntity(userId1, "U1_W", "ISS")]);
        await _sut.SaveChangesAsync();

        // Assert — user2's windows untouched
        var u2windows = await _context.SavedWindows.Where(w => w.UserId == userId2).ToListAsync();
        u2windows.Should().HaveCount(1);
        u2windows[0].WindowId.Should().Be("U2_W");
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private static UserSavedWindowEntity MakeWindowEntity(Guid userId, string windowId, string destination) =>
        new()
        {
            UserId      = userId,
            WindowId    = windowId,
            Destination = destination,
            WindowJson  = $$"""{"id":"{{windowId}}","destination":"{{destination}}"}""",
            SavedAt     = DateTime.UtcNow,
        };
}
```

- [ ] **Step 2: Rodar para confirmar RED (arquivo existe mas EF InMemory pode não compilar sem referências)**

```powershell
cd C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C
dotnet build MissionClear.Tests --no-restore -v q 2>&1 | tail -20
```

Expected: `Build succeeded` — se falhar, verificar using directives e namespaces.

- [ ] **Step 3: Rodar só os testes de repositório**

```powershell
dotnet test MissionClear.Tests --filter "FullyQualifiedName~FavoritesRepositoryTests" --no-build -v normal 2>&1
```

Expected: todos os testes passam (GREEN). O EF InMemory não valida constraints unique, então os testes de deduplicação e limites exercitam a **lógica do repositório**, não as constraints do banco.

- [ ] **Step 4: Commit**

```bash
git add MissionClear.Tests/Data/FavoritesRepositoryTests.cs
git commit -m "test(data): add FavoritesRepositoryTests covering all CRUD and isolation scenarios"
```

---

## Task 2: Confirmar cobertura completa existente

Os arquivos abaixo já existem e cobrem as camadas de serviço e integração. Verificar que estão passando antes de fechar a task.

**Files:**
- Verify: `MissionClear.Tests/Services/UserServiceTests.cs` (12 testes de serviço)
- Verify: `MissionClear.Tests/Integration/MobileContractTests.cs` (CONTRACT 10 — 11 testes HTTP)

- [ ] **Step 1: Rodar testes de serviço de favoritos**

```powershell
dotnet test MissionClear.Tests --filter "FullyQualifiedName~UserServiceTests" --no-build -v normal 2>&1
```

Expected: todos passam. Cobertura de serviço:
- `GetFavoritesAsync_UserNotFound_Throws` ✓
- `GetFavoritesAsync_NewUser_ReturnsEmptyArrays` ✓
- `GetFavoritesAsync_WithData_ReturnsDebrisIds` ✓
- `GetFavoritesAsync_WithWindows_ReturnsDeserializedWindows` ✓
- `GetFavoritesAsync_UpdatedAt_IsIso8601` ✓
- `UpdateFavoritesAsync_UserNotFound_Throws` ✓
- `UpdateFavoritesAsync_WithDebrisIds_CallsReplaceDebris` ✓
- `UpdateFavoritesAsync_NullDebrisIds_DoesNotCallReplaceDebris` ✓
- `UpdateFavoritesAsync_WithWindows_CallsReplaceWindows` ✓
- `UpdateFavoritesAsync_NullWindows_DoesNotCallReplaceWindows` ✓
- `UpdateFavoritesAsync_CallsSaveChangesExactlyOnce` ✓
- `UpdateFavoritesAsync_RepositoryThrows_PropagatesWithoutSwallowing` ✓
- `UpdateFavoritesAsync_WindowEntity_ExtractsWindowIdFromJson` ✓

- [ ] **Step 2: Rodar testes de contrato de favoritos**

```powershell
dotnet test MissionClear.Tests --filter "FullyQualifiedName~MobileContractTests" --no-build -v normal 2>&1
```

Expected: todos passam. Cobertura de integração HTTP:
- `Favorites_GET_RequiresAuthentication` — 401 sem token ✓
- `Favorites_PUT_RequiresAuthentication` — 401 sem token ✓
- `Favorites_GET_NewUser_ReturnsEmptyArrays` — novo usuário ✓
- `Favorites_GET_ResponseShape_MatchesMobileContract` — snake_case, campos corretos ✓
- `Favorites_PUT_UpdatesDebrisIds_AndReturnsUpdatedShape` — PUT retorna shape correto ✓
- `Favorites_GET_AfterPUT_ReturnsPersisted_Ids` — persistência real ✓
- `Favorites_PUT_ServerDeduplicates_DebrisIds` — dedup no servidor ✓
- `Favorites_PUT_NullDebrisIds_PreservesExistingDebris` — null = preserve ✓
- `Favorites_PUT_EmptyDebrisArray_ClearsDebris` — [] = clear ✓
- `Favorites_PUT_UpdatedAt_ChangesAfterUpdate` — updated_at ISO 8601 ✓
- `Favorites_PUT_WithWindowsPayload_Roundtrips` — windows round-trip ✓

- [ ] **Step 3: Rodar toda a suite**

```powershell
dotnet test MissionClear.Tests --no-build -v minimal 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0`

- [ ] **Step 4: Commit final**

```bash
git add MissionClear.Tests/Integration/MobileContractTests.cs
git commit -m "test(integration): add MobileContractTests CONTRACT 10 — favorites HTTP contract"
```

---

## Resumo de cobertura pós-plano

| Camada | Arquivo | Testes |
|---|---|---|
| Entidade | exercitada via repositório | implícita |
| Repositório | `FavoritesRepositoryTests.cs` | 20 testes |
| Serviço | `UserServiceTests.cs` | 13 testes |
| HTTP Auth | `MobileContractTests.cs` | 2 testes |
| HTTP Shape | `MobileContractTests.cs` | 1 teste |
| HTTP Semântica | `MobileContractTests.cs` | 8 testes |
| **Total** | | **44 testes** |
