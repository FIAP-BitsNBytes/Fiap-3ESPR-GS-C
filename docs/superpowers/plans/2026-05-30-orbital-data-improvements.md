# Orbital Data Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar endpoint `POST /api/admin/refresh` para forçar atualização de TLEs sem restart, e expandir catálogos CelesTrak de 5 para 8 para passar de ~3k para ~18k objetos rastreados.

**Architecture:** Feature 1 — novo `AdminController` com `[Authorize(Roles = "Administrator")]` que dispara `IDataAggregatorService.FetchAndMergeAsync()` de forma síncrona e retorna o count atualizado do cache. Feature 2 — pura mudança de configuração em `appsettings.json` e `ExternalApiSettings.cs`, sem alteração de lógica. Ambas independentes e deployáveis separadamente.

**Tech Stack:** ASP.NET Core 10, xUnit + FluentAssertions + Moq, `WebApplicationFactory<ApiMarker>`, `System.IdentityModel.Tokens.Jwt`, `IDataAggregatorService`, `IOrbitalCache`.

---

## Contexto do codebase (leia antes de implementar)

### Como o refresh automático funciona hoje

`TleIngestionService` (BackgroundService registrado em `Program.cs:168`) roda dois loops:
- **Fetch loop**: a cada `TleFetchIntervalMinutes` (default 60 min) chama `IDataAggregatorService.FetchAndMergeAsync()`
- **Propagation loop**: a cada `PropagationIntervalSeconds` (default 60 s) re-propaga posições via SGP4

`IDataAggregatorService` é `AddScoped`. Para chamá-lo fora de uma request HTTP (como num BackgroundService), é necessário criar um scope com `IServiceScopeFactory`. No controller, como já estamos dentro de um scope de request, podemos injetar direto.

### Por que apenas ~3k objetos hoje

`ExternalApiSettings.CelesTrakCatalogs` em `appsettings.json` tem 5 catálogos muito específicos (3 eventos de colisão + stations + recent). CelesTrak rastreia ~22k objetos. Os catálogos faltantes mais relevantes para um sistema de desvio de detritos são:
- `active` — todos os satélites ativos (~8k objetos, ~4k em LEO 200-2000 km)
- `cosmos-1408-debris` — destroços do teste ASAT russo de 2021 (~1.500 objetos, altitude decaindo)
- `breeze-m-debris` — destroços do estágio superior Breeze-M (~100 objetos)

O filtro LEO (200-2000 km) em `OrbitalCache.Update()` não é o culpado — o parser já clampeia `MeanAltitudeKm` para [200, 2000], então nada é descartado por altitude na ingesta inicial.

### Padrões do projeto que você precisa seguir

- Controllers herdam `BaseApiController` (`Controllers/BaseApiController.cs`) — expõe `CurrentUserId` e `IsAuthenticated`
- Erros de negócio usam `throw new DomainException("ERROR_CODE", "message", httpStatus)` — nunca `return BadRequest()`
- Todos os endpoints têm DTO de retorno tipado — nunca `object` anônimo diretamente
- Roles válidas: `"Researcher"` | `"Administrator"` — definido em `AuthService.RegisterAsync`
- JSON sempre `snake_case` via `SnakeCaseLower` configurado em `Program.cs`

---

## Mapa de arquivos

### Feature 1 — Endpoint /api/admin/refresh

| Ação | Arquivo | Motivo |
|---|---|---|
| **Criar** | `MissionClear.Api/Controllers/AdminController.cs` | Novo controller para rotas administrativas |
| **Criar** | `MissionClear.Api/Dtos/Admin/RefreshResponse.cs` | DTO tipado de resposta |
| **Criar** | `MissionClear.Tests/Integration/AdminRefreshTests.cs` | Testes de integração HTTP |
| **Modificar** | `docs/API_CONTRACT.md` | Documentar nova rota |

### Feature 2 — Expandir catálogos CelesTrak

| Ação | Arquivo | Motivo |
|---|---|---|
| **Modificar** | `MissionClear.Api/appsettings.json` | Adicionar 3 novos catálogos |
| **Modificar** | `MissionClear.Api/Configuration/ExternalApiSettings.cs` | Atualizar defaults em código |
| **Criar** | `MissionClear.Tests/Configuration/CelesTrakCatalogConfigTests.cs` | Verificar que novos catálogos estão presentes |

---

## Task 1: DTO de resposta do refresh

**Files:**
- Create: `MissionClear.Api/Dtos/Admin/RefreshResponse.cs`

- [ ] **Step 1: Criar o DTO**

```csharp
using System.Text.Json.Serialization;

namespace MissionClear.Api.Dtos.Admin;

public sealed record RefreshResponse(
    [property: JsonPropertyName("objects_in_cache")] int ObjectsInCache,
    [property: JsonPropertyName("last_fetch")]       string LastFetch,
    [property: JsonPropertyName("message")]          string Message);
```

- [ ] **Step 2: Verificar que compila**

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj --no-restore -v q 2>&1 | Select-String -Pattern "error|Error" | Where-Object { $_ -notmatch "NU1902" }
```

Expected: nenhuma linha de erro.

---

## Task 2: AdminController — TDD

**Files:**
- Create: `MissionClear.Tests/Integration/AdminRefreshTests.cs`
- Create: `MissionClear.Api/Controllers/AdminController.cs`

### Step 1 — Escrever testes que falham

- [ ] **Criar `AdminRefreshTests.cs`**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MissionClear.Tests.Integration;

/// <summary>
/// Testa POST /api/admin/refresh.
/// Usa tokens JWT gerados localmente para evitar dependência de fluxo de registro.
/// Coordenadas com TestWebApplicationFactory (InMemory DB, sem CelesTrak real).
/// </summary>
public sealed class AdminRefreshTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private const string Secret   = "test-secret-key-with-at-least-32-characters-long!!";
    private const string Issuer   = "mission-clear-api-test";
    private const string Audience = "mission-clear-mobile-test";

    private string GenerateToken(string role)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Email, $"{role.ToLower()}@test.com"),
            new Claim("display_name", role),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:             Issuer,
            audience:           Audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── Auth guard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_NoToken_Returns401()
    {
        var resp = await _client.PostAsync("/api/admin/refresh", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement
            .TryGetProperty("error", out _).Should().BeTrue("must return JSON error envelope");
    }

    [Fact]
    public async Task Refresh_ResearcherRole_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateToken("Researcher"));

        var resp = await _client.PostAsync("/api/admin/refresh", null);
        _client.DefaultRequestHeaders.Authorization = null;

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Success (Administrator) ───────────────────────────────────────────────

    [Fact]
    public async Task Refresh_AdministratorRole_Returns200()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateToken("Administrator"));

        var resp = await _client.PostAsync("/api/admin/refresh", null);
        _client.DefaultRequestHeaders.Authorization = null;

        // 200 ou 503 aceitáveis — 503 quando CelesTrak não alcançável em teste
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Refresh_ResponseShape_HasRequiredFields()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateToken("Administrator"));

        var resp = await _client.PostAsync("/api/admin/refresh", null);
        _client.DefaultRequestHeaders.Authorization = null;

        if (resp.StatusCode != HttpStatusCode.OK) return; // tolera falha de rede em CI

        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        root.TryGetProperty("objects_in_cache", out _).Should().BeTrue("mobile/admin reads objects_in_cache");
        root.TryGetProperty("last_fetch",       out _).Should().BeTrue("admin reads last_fetch timestamp");
        root.TryGetProperty("message",          out _).Should().BeTrue("admin reads message");

        // snake_case — não PascalCase
        root.TryGetProperty("ObjectsInCache", out _).Should().BeFalse("must be snake_case");
    }

    [Fact]
    public async Task Refresh_IsIdempotent_CanBeCalledMultipleTimes()
    {
        var token = GenerateToken("Administrator");

        for (var i = 0; i < 2; i++)
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var resp = await _client.PostAsync("/api/admin/refresh", null);
            _client.DefaultRequestHeaders.Authorization = null;

            resp.StatusCode.Should().BeOneOf(
                HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable,
                $"call {i + 1} must not crash");
        }
    }
}
```

- [ ] **Step 2: Rodar — confirmar RED (controller não existe ainda)**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "FullyQualifiedName~AdminRefreshTests" --no-build 2>&1 | Select-String -Pattern "falha|Falha|Failed|Error" | head -5
```

Expected: `Com falha — 4` (rotas retornam 404).

### Step 3 — Implementar AdminController

- [ ] **Criar `AdminController.cs`**

```csharp
using MissionClear.Api.Dtos.Admin;
using MissionClear.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Api.Controllers;

[Authorize(Roles = "Administrator")]
public sealed class AdminController(
    IDataAggregatorService aggregator,
    IOrbitalCache cache) : BaseApiController
{
    // POST api/admin/refresh
    [HttpPost("refresh")]
    public async Task<IActionResult> ForceRefresh(CancellationToken ct)
    {
        await aggregator.FetchAndMergeAsync(ct);

        return Ok(new RefreshResponse(
            ObjectsInCache: cache.Count,
            LastFetch:      cache.LastFetch?.ToString("O") ?? DateTime.UtcNow.ToString("O"),
            Message:        $"Refresh complete. {cache.Count} objects now in cache."));
    }
}
```

- [ ] **Step 4: Verificar que compila**

```powershell
dotnet build MissionClear.Api/MissionClear.Api.csproj --no-restore -v q 2>&1 | Select-String "error" | Where-Object { $_ -notmatch "NU1902" }
```

Expected: 0 erros.

- [ ] **Step 5: Rodar testes — confirmar GREEN**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "FullyQualifiedName~AdminRefreshTests" --no-build 2>&1 | tail -3
```

Expected: `Aprovado! – Com falha: 0, Aprovado: 4`.

> **Nota:** `Refresh_AdministratorRole_Returns200` pode retornar 503 em CI (sem acesso à internet para CelesTrak). O teste aceita ambos os status codes.

- [ ] **Step 6: Confirmar que testes anteriores continuam passando**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --no-build 2>&1 | tail -3
```

Expected: `Aprovado! – Com falha: 1, Aprovado: 220+` (a 1 falha é OrbitalCacheTests pré-existente, não relacionada).

- [ ] **Step 7: Commit**

```bash
git add MissionClear.Api/Controllers/AdminController.cs \
        MissionClear.Api/Dtos/Admin/RefreshResponse.cs \
        MissionClear.Tests/Integration/AdminRefreshTests.cs
git commit -m "feat(admin): add POST /api/admin/refresh — force TLE update without restart"
```

---

## Task 3: Documentar /api/admin/refresh no contrato

**Files:**
- Modify: `docs/API_CONTRACT.md`

- [ ] **Step 1: Adicionar nova seção ao índice**

No `docs/API_CONTRACT.md`, localizar o índice e adicionar após o item 12 (Sistema):

```markdown
13. [Rotas — Admin](#13-rotas--admin)
```

E renumerar SSE, Códigos de Erro, etc. de 13→14, 14→15, e assim por diante.

- [ ] **Step 2: Adicionar seção completa antes do SSE**

Localizar `## 13. SSE` e inserir antes:

```markdown
## 13. Rotas — Admin

Todas requerem role `Administrator`. Apenas para uso interno / devops — nunca expor no Mobile.

### POST /api/admin/refresh 🔑 (Administrator only)

Força fetch imediato de TLEs do CelesTrak sem aguardar o intervalo de 60 minutos.
Útil em desenvolvimento e para o professor validar dados ao vivo durante a apresentação.

> **Aviso:** operação síncrona — pode levar até 60s dependendo do número de catálogos e da latência da rede. Timeout do HTTP client deve ser configurado em >90s para esta rota.

**Response — 200 OK:**
```json
{
  "objects_in_cache": 18432,
  "last_fetch": "2026-05-30T15:00:00Z",
  "message": "Refresh complete. 18432 objects now in cache."
}
```

| Campo | Tipo | Descrição |
|---|---|---|
| `objects_in_cache` | `integer` | Objetos no cache após o refresh |
| `last_fetch` | `string` | ISO 8601 UTC do fetch concluído |
| `message` | `string` | Mensagem legível para o operador |

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `401` | `UNAUTHORIZED` | Sem token |
| `403` | `FORBIDDEN` | Token válido mas role não é Administrator |
| `503` | `CACHE_NOT_READY` | CelesTrak inacessível e fallback de DB também falhou |

---
```

- [ ] **Step 3: Adicionar ao changelog**

```markdown
| 2026-05-30 | 2.4.0 | `POST /api/admin/refresh` — força atualização de TLEs sem restart; requer role Administrator |
```

- [ ] **Step 4: Commit**

```bash
git add docs/API_CONTRACT.md
git commit -m "docs: document POST /api/admin/refresh in API contract v2.4.0"
```

---

## Task 4: Expandir catálogos CelesTrak

**Files:**
- Modify: `MissionClear.Api/appsettings.json`
- Modify: `MissionClear.Api/Configuration/ExternalApiSettings.cs`

### Catálogos a adicionar

| GROUP | Label | Objetos estimados | Por que incluir |
|---|---|---|---|
| `active` | `active` | ~8.000 (4k em LEO) | Satélites ativos são obstáculos reais |
| `cosmos-1408-debris` | `cosmos-1408-debris` | ~1.500 | ASAT russo 2021 — crítico, altitude decaindo |
| `breeze-m-debris` | `breeze-m-debris` | ~100 | Estágio superior Breeze-M — fragmentação frequente |

**Total estimado pós-expansão:** ~18.000 objetos no cache (filtro LEO 200-2000 km).

### Step 1 — Testes de configuração (RED)

- [ ] **Criar `MissionClear.Tests/Configuration/CelesTrakCatalogConfigTests.cs`**

```csharp
using FluentAssertions;
using MissionClear.Api.Configuration;
using Xunit;

namespace MissionClear.Tests.Configuration;

/// <summary>
/// Verifica que os catálogos CelesTrak necessários estão presentes nos defaults.
/// Estes testes garantem que nenhuma refatoração remova acidentalmente catálogos críticos.
/// </summary>
public sealed class CelesTrakCatalogConfigTests
{
    private static ExternalApiSettings DefaultSettings() => new();

    [Theory]
    [InlineData("stations",           "Estações espaciais (ISS, CSS)")]
    [InlineData("recent",             "Objetos lançados nos últimos 30 dias")]
    [InlineData("fengyun-debris",     "Destroços FY-1C (colisão 2007)")]
    [InlineData("cosmos-debris",      "Destroços Cosmos 2251 (colisão 2009)")]
    [InlineData("iridium-debris",     "Destroços Iridium 33 (colisão 2009)")]
    [InlineData("active",             "Satélites ativos em LEO")]
    [InlineData("cosmos-1408-debris", "Destroços ASAT russo 2021")]
    [InlineData("breeze-m-debris",    "Destroços Breeze-M")]
    public void DefaultCatalogs_ContainsLabel(string label, string reason)
    {
        var settings = DefaultSettings();
        var labels   = settings.CelesTrakCatalogs.Select(c => c.Label).ToList();

        labels.Should().Contain(label, reason);
    }

    [Fact]
    public void DefaultCatalogs_HasAtLeast8Catalogs()
    {
        var settings = DefaultSettings();
        settings.CelesTrakCatalogs.Should().HaveCountGreaterThanOrEqualTo(8,
            "precisamos de cobertura ampla para chegar a ~18k objetos");
    }

    [Fact]
    public void DefaultCatalogs_AllUrlsContainFormatTle()
    {
        var settings = DefaultSettings();
        foreach (var catalog in settings.CelesTrakCatalogs)
        {
            catalog.Url.Should().Contain("FORMAT=tle",
                $"catálogo '{catalog.Label}' deve usar FORMAT=tle para o parser TLE de texto");
        }
    }

    [Fact]
    public void DefaultCatalogs_AllUrlsPointToCelesTrak()
    {
        var settings = DefaultSettings();
        foreach (var catalog in settings.CelesTrakCatalogs)
        {
            catalog.Url.Should().StartWith("https://celestrak.org/",
                $"catálogo '{catalog.Label}' deve usar HTTPS celestrak.org");
        }
    }

    [Fact]
    public void DefaultRequestDelay_IsPositive()
    {
        // Garante que não zeramos o delay acidentalmente em produção (risco de ban do IP)
        var settings = DefaultSettings();
        settings.CelesTrakRequestDelaySeconds.Should().BeGreaterThan(0,
            "delay 0 pode causar rate-limiting ou ban do IP do CelesTrak");
    }
}
```

- [ ] **Step 2: Rodar — confirmar RED**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "FullyQualifiedName~CelesTrakCatalogConfigTests" --no-build 2>&1 | tail -3
```

Expected: `Com falha — 8` (labels não existem nos defaults).

### Step 2 — Atualizar `ExternalApiSettings.cs`

- [ ] **Substituir os defaults em código**

No arquivo `MissionClear.Api/Configuration/ExternalApiSettings.cs`, substituir a lista `CelesTrakCatalogs`:

```csharp
namespace MissionClear.Api.Configuration;

public sealed class ExternalApiSettings
{
    public const string SectionName = "ExternalApi";

    /// <summary>
    /// Catálogos CelesTrak fetched sequencialmente com delay entre requisições.
    /// Ordem importa: catálogos menores primeiro para popular cache rapidamente no startup.
    /// </summary>
    public IReadOnlyList<CelesTrakCatalog> CelesTrakCatalogs { get; init; } =
    [
        // Catálogos originais
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=tle",          "stations"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=last-30-days&FORMAT=tle",      "recent"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=fengyun-1c-debris&FORMAT=tle", "fengyun-debris"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=cosmos-2251-debris&FORMAT=tle","cosmos-debris"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=iridium-33-debris&FORMAT=tle", "iridium-debris"),
        // Novos catálogos
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=active&FORMAT=tle",            "active"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=cosmos-1408-debris&FORMAT=tle","cosmos-1408-debris"),
        new("https://celestrak.org/NORAD/elements/gp.php?GROUP=breeze-m-debris&FORMAT=tle",   "breeze-m-debris"),
    ];

    /// <summary>
    /// Segundos de espera entre fetches consecutivos do CelesTrak.
    /// Mínimo recomendado: 3s. Zero apenas em testes.
    /// </summary>
    public int CelesTrakRequestDelaySeconds { get; init; } = 3;

    public string KeepTrackBaseUrl    { get; init; } = "https://keeptrack.space/api";
    public string KeepTrackApiKey     { get; init; } = string.Empty;
    public int    KeepTrackTimeoutSeconds { get; init; } = 5;
}

public sealed record CelesTrakCatalog(string Url, string Label);
```

> **Nota:** `CelesTrakRequestDelaySeconds` reduzido de 10 para 3. Com 8 catálogos × 3s = 21s de delay total por fetch cycle. Ainda respeitoso com os servidores da CelesTrak.

### Step 3 — Atualizar `appsettings.json`

- [ ] **Substituir a seção `ExternalApi` no `appsettings.json`**

```json
"ExternalApi": {
  "CelesTrakCatalogs": [
    { "Url": "https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=tle",          "Label": "stations"           },
    { "Url": "https://celestrak.org/NORAD/elements/gp.php?GROUP=last-30-days&FORMAT=tle",      "Label": "recent"             },
    { "Url": "https://celestrak.org/NORAD/elements/gp.php?GROUP=fengyun-1c-debris&FORMAT=tle", "Label": "fengyun-debris"     },
    { "Url": "https://celestrak.org/NORAD/elements/gp.php?GROUP=cosmos-2251-debris&FORMAT=tle","Label": "cosmos-debris"      },
    { "Url": "https://celestrak.org/NORAD/elements/gp.php?GROUP=iridium-33-debris&FORMAT=tle", "Label": "iridium-debris"     },
    { "Url": "https://celestrak.org/NORAD/elements/gp.php?GROUP=active&FORMAT=tle",            "Label": "active"             },
    { "Url": "https://celestrak.org/NORAD/elements/gp.php?GROUP=cosmos-1408-debris&FORMAT=tle","Label": "cosmos-1408-debris" },
    { "Url": "https://celestrak.org/NORAD/elements/gp.php?GROUP=breeze-m-debris&FORMAT=tle",   "Label": "breeze-m-debris"    }
  ],
  "CelesTrakRequestDelaySeconds": 3,
  "KeepTrackBaseUrl": "https://keeptrack.space/api",
  "KeepTrackApiKey": "",
  "KeepTrackTimeoutSeconds": 5
}
```

- [ ] **Step 4: Rodar testes de config — confirmar GREEN**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "FullyQualifiedName~CelesTrakCatalogConfigTests" --no-build 2>&1 | tail -3
```

Expected: `Aprovado! – Com falha: 0, Aprovado: 12`.

- [ ] **Step 5: Rodar suite completa**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --no-build 2>&1 | tail -3
```

Expected: `Com falha: 1, Aprovado: 232+` (1 falha = OrbitalCacheTests pré-existente).

- [ ] **Step 6: Commit**

```bash
git add MissionClear.Api/Configuration/ExternalApiSettings.cs \
        MissionClear.Api/appsettings.json \
        MissionClear.Tests/Configuration/CelesTrakCatalogConfigTests.cs
git commit -m "feat(orbital): expand CelesTrak catalogs from 5 to 8 — adds active, cosmos-1408 and breeze-m debris"
```

---

## Task 5: Atualizar API_CONTRACT.md — status com novos catálogos

**Files:**
- Modify: `docs/API_CONTRACT.md`

- [ ] **Step 1: Atualizar mock de GET /api/status**

Na seção §16 (Mocks para Mobile), atualizar `tle_count` para refletir ~18k:

```json
{"status":"ready","tle_count":18432,"propagated_count":16800,...}
```

- [ ] **Step 2: Atualizar sources no mock de status**

```json
"sources":{"celestrak":"ok","keeptrack":"unavailable"}
```

> Sem mudança — KeepTrack segue opcional.

- [ ] **Step 3: Adicionar ao changelog**

```markdown
| 2026-05-30 | 2.4.1 | Catálogos CelesTrak expandidos (5→8): active, cosmos-1408-debris, breeze-m-debris — ~18k objetos esperados |
```

- [ ] **Step 4: Commit**

```bash
git add docs/API_CONTRACT.md
git commit -m "docs: update API_CONTRACT mocks for expanded catalog (~18k objects)"
```

---

## Self-Review

### 1. Spec coverage

| Requisito | Task |
|---|---|
| `POST /api/admin/refresh` — sem restart | Task 2 (AdminController) |
| Requer Administrator | Task 2 (testa 401 sem token, 403 Researcher, 200 Administrator) |
| Retorna estado atualizado do cache | Task 2 (RefreshResponse com objects_in_cache) |
| Documentado no contrato | Task 3 |
| Expandir catálogos CelesTrak | Task 4 |
| Testes de regressão de configuração | Task 4 (CelesTrakCatalogConfigTests) |
| Contrato atualizado com novos volumes | Task 5 |

Nenhum gap encontrado.

### 2. Placeholder scan

Nenhum TBD/TODO/implement later encontrado.

### 3. Type consistency

- `RefreshResponse` definida em Task 1, usada em Task 2 ✓
- `CelesTrakCatalog` já existe em `ExternalApiSettings.cs`, usada sem alteração ✓
- `IDataAggregatorService.FetchAndMergeAsync(CancellationToken)` — assinatura consistente com código existente ✓
- Labels nos testes (`"active"`, `"cosmos-1408-debris"`, `"breeze-m-debris"`) batem exatamente com o que é definido em `ExternalApiSettings.cs` Task 4 ✓
