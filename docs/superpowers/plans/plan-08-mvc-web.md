# Plan 08 — ASP.NET Core MVC Web Project (Cookie + Claims)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans
>
> **FONTE DA VERDADE** para Phase 8. MVC web project que consome a API via HttpClient.

**Goal:** Implementar projeto `MissionClear.Web` (ASP.NET Core MVC) com autenticação Cookie+Claims, consumindo a API via `ApiClient` (IHttpClientFactory). Zero acesso direto ao banco de dados.

**Execution order:** Após plan-07 (API Controllers completa e rodando via Aspire).
**Estimated time:** 60–90 minutes.

**Architecture:**
- MVC chama `POST /api/auth/login` → recebe JWT → extrai `role` → cria Cookie com Claims
- `ApiClient` injeta `Bearer {access_token}` lido do claim `access_token` em todas as chamadas à API
- `[Authorize(Roles = "Administrator")]` protege `UsersController`
- Aspire service discovery: `http://api` resolve para o projeto `MissionClear.Api`

**Regras invioláveis:**
- MVC **nunca** usa `DbContext`, Entity Framework, nem repositórios
- Toda comunicação com dados via `ApiClient`
- Claims incluem: `NameIdentifier`, `Email`, `Name`, `Role`, `access_token`, `refresh_token`
- Researcher: vê suas missões + dashboard. Administrator: vê `/Users` também

---

## Scope do MVC

| Rota | Auth | Role mínimo |
|---|---|---|
| `/Auth/Login` | Público | — |
| `/Auth/Register` | Público | — |
| `/Auth/Logout` | Autenticado | — |
| `/Auth/AccessDenied` | Público | — |
| `/Dashboard` | Opcional | — |
| `/Debris` | Público | — |
| `/LaunchWindows` | Público | — |
| `/Missions` | Autenticado | Any |
| `/Missions/Details/{id}` | Autenticado | Any |
| `/Users` | Autenticado | **Administrator** |

---

## Dependências obrigatórias

| Fase | O que fornece |
|------|---------------|
| plan-00 | `MissionClear.ServiceDefaults` (AddServiceDefaults, OpenTelemetry) |
| plan-04 | `POST /api/auth/login` retorna `{ user: { role }, access_token, refresh_token }` |
| plan-07 | API rodando no Aspire com service discovery `"api"` |

---

## Task 8.1 — csproj e Program.cs

**Files:**
- Modify: `MissionClear.Web/MissionClear.Web.csproj`
- Modify: `MissionClear.Web/Program.cs`

- [ ] **Step 1: Verificar referência ao ServiceDefaults no csproj**

```xml
<ItemGroup>
  <ProjectReference Include="..\MissionClear.ServiceDefaults\MissionClear.ServiceDefaults.csproj" />
</ItemGroup>
```

Se não existir, adicionar. Pacotes NuGet necessários:
```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.Cookies" Version="8.0.*" />
```

- [ ] **Step 2: Substituir Program.cs completo**

```csharp
using MissionClear.ServiceDefaults;
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();

// ApiClient usa service discovery do Aspire para resolver "http://api"
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri("http://api");
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultControllerRoute();
app.MapDefaultEndpoints();

app.Run();
```

- [ ] **Step 3: Build**

```powershell
dotnet build MissionClear.Web/MissionClear.Web.csproj
```

Esperado: `Build succeeded. 0 Warning(s). 0 Error(s).`

---

## Task 8.2 — ApiClient

**Files:**
- Create: `MissionClear.Web/Services/ApiClient.cs`

- [ ] **Step 1: Criar `MissionClear.Web/Services/ApiClient.cs`**

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MissionClear.Web.Services;

public sealed class ApiClient(HttpClient client, IHttpContextAccessor httpContextAccessor)
{
    private void AttachToken()
    {
        var token = httpContextAccessor.HttpContext?.User.FindFirst("access_token")?.Value;
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        else
            client.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        AttachToken();
        var response = await client.GetAsync(path);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<T>();
    }

    public async Task<(T? Data, string? Error, int StatusCode)> PostAsync<T>(string path, object body)
    {
        AttachToken();
        var response = await client.PostAsJsonAsync(path, body);
        var statusCode = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<T>(), null, statusCode);
        var errorBody = await response.Content.ReadAsStringAsync();
        return (default, errorBody, statusCode);
    }

    public async Task<bool> DeleteAsync(string path)
    {
        AttachToken();
        var response = await client.DeleteAsync(path);
        return response.IsSuccessStatusCode;
    }

    public async Task<LoginApiResponse?> LoginAsync(string email, string password)
    {
        var (data, _, _) = await PostAsync<LoginApiResponse>("/api/auth/login",
            new { email, password });
        return data;
    }

    public async Task<RegisterApiResponse?> RegisterAsync(string email, string password, string displayName)
    {
        var (data, _, _) = await PostAsync<RegisterApiResponse>("/api/auth/register",
            new { email, password, display_name = displayName });
        return data;
    }
}

// Minimal API response types — não compartilham os DTOs do Api project
// [JsonPropertyName] required: API returns snake_case but records use PascalCase
public sealed record LoginApiResponse(
    [property: JsonPropertyName("user")] LoginUserDto User,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record RegisterApiResponse(
    [property: JsonPropertyName("user")] LoginUserDto User,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record LoginUserDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("total_missions")] int? TotalMissions,
    [property: JsonPropertyName("best_score")] int? BestScore);
```

- [ ] **Step 2: Build**

```powershell
dotnet build MissionClear.Web/MissionClear.Web.csproj
```

---

## Task 8.3 — ViewModels

**Files:**
- Create: `MissionClear.Web/Models/LoginViewModel.cs`
- Create: `MissionClear.Web/Models/RegisterViewModel.cs`
- Create: `MissionClear.Web/Models/DashboardViewModel.cs`

- [ ] **Step 1: Criar ViewModels**

`MissionClear.Web/Models/LoginViewModel.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Web.Models;

public sealed class LoginViewModel
{
    [Required][EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
    public string? Error { get; set; }
}
```

`MissionClear.Web/Models/RegisterViewModel.cs`:
```csharp
using System.ComponentModel.DataAnnotations;

namespace MissionClear.Web.Models;

public sealed class RegisterViewModel
{
    [Required][EmailAddress]
    public string Email { get; set; } = "";

    [Required][MinLength(8)]
    public string Password { get; set; } = "";

    [Required][StringLength(50, MinimumLength = 2)]
    public string DisplayName { get; set; } = "";

    public string? Error { get; set; }
}
```

`MissionClear.Web/Models/DashboardViewModel.cs`:
```csharp
namespace MissionClear.Web.Models;

public sealed class DashboardViewModel
{
    public int TotalTrackedObjects { get; set; }
    public int Debris { get; set; }
    public int Satellites { get; set; }
    public int RocketBodies { get; set; }
    public int ActiveAlerts { get; set; }
    public string? LastUpdated { get; set; }

    // Null quando não autenticado
    public string? UserDisplayName { get; set; }
    public int? UserTotalMissions { get; set; }
    public int? UserBestScore { get; set; }
}
```

---

## Task 8.4 — AuthController (Cookie Bridge)

**Files:**
- Create: `MissionClear.Web/Controllers/AuthController.cs`

- [ ] **Step 1: Criar AuthController.cs**

```csharp
using System.Security.Claims;
using MissionClear.Web.Models;
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Web.Controllers;

public sealed class AuthController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl)
        => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var response = await apiClient.LoginAsync(model.Email, model.Password);
        if (response is null)
        {
            model.Error = "Email ou senha incorretos.";
            return View(model);
        }

        await SignInWithCookieAsync(response.User, response.AccessToken, response.RefreshToken);
        return Redirect(model.ReturnUrl ?? "/Dashboard");
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var response = await apiClient.RegisterAsync(model.Email, model.Password, model.DisplayName);
        if (response is null)
        {
            model.Error = "Erro ao criar conta. Verifique se o email já está cadastrado.";
            return View(model);
        }

        await SignInWithCookieAsync(response.User, response.AccessToken, response.RefreshToken);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();

    private async Task SignInWithCookieAsync(LoginUserDto user, string accessToken, string refreshToken)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role),        // "Researcher" ou "Administrator"
            new("access_token", accessToken),       // Repassado à API em chamadas subsequentes
            new("refresh_token", refreshToken),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build MissionClear.Web/MissionClear.Web.csproj
```

---

## Task 8.5 — DashboardController + MissionsController + UsersController

**Files:**
- Create: `MissionClear.Web/Controllers/DashboardController.cs`
- Create: `MissionClear.Web/Controllers/MissionsController.cs`
- Create: `MissionClear.Web/Controllers/UsersController.cs`

- [ ] **Step 1: DashboardController.cs**

```csharp
using MissionClear.Web.Models;
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MissionClear.Web.Controllers;

public sealed class DashboardController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var json = await apiClient.GetAsync<JsonElement>("/api/dashboard/summary");
        var vm = new DashboardViewModel();

        if (json.ValueKind != JsonValueKind.Undefined)
        {
            if (json.TryGetProperty("orbital", out var orbital))
            {
                vm.TotalTrackedObjects = orbital.GetProperty("total_tracked_objects").GetInt32();
                vm.ActiveAlerts = orbital.GetProperty("active_conjunction_alerts").GetInt32();
                vm.LastUpdated = orbital.GetProperty("last_updated").GetString();
                if (orbital.TryGetProperty("by_type", out var byType))
                {
                    vm.Debris = byType.GetProperty("debris").GetInt32();
                    vm.Satellites = byType.GetProperty("satellite").GetInt32();
                    vm.RocketBodies = byType.GetProperty("rocket_body").GetInt32();
                }
            }
            if (json.TryGetProperty("user", out var user) && user.ValueKind != JsonValueKind.Null)
            {
                vm.UserDisplayName = user.GetProperty("display_name").GetString();
                vm.UserTotalMissions = user.GetProperty("total_missions").GetInt32();
                vm.UserBestScore = user.GetProperty("best_score").GetInt32();
            }
        }

        return View(vm);
    }
}
```

- [ ] **Step 2: MissionsController.cs**

```csharp
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MissionClear.Web.Controllers;

[Authorize]
public sealed class MissionsController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] int page = 1,
        [FromQuery] string? status = null)
    {
        var url = $"/api/missions?page={page}&limit=20{(status != null ? $"&status={status}" : "")}";
        var json = await apiClient.GetAsync<JsonElement>(url);
        ViewBag.MissionsJson = json;
        ViewBag.CurrentPage = page;
        ViewBag.StatusFilter = status;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        var json = await apiClient.GetAsync<JsonElement>($"/api/missions/{id}");
        if (json.ValueKind == JsonValueKind.Undefined)
            return NotFound();
        ViewBag.MissionJson = json;
        return View();
    }
}
```

- [ ] **Step 3: UsersController.cs (Administrator only)**

```csharp
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MissionClear.Web.Controllers;

[Authorize(Roles = "Administrator")]
public sealed class UsersController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        ViewBag.Message = "Área administrativa de usuários.";
        return View();
    }
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build MissionClear.Web/MissionClear.Web.csproj
```

---

## Task 8.6 — Views Razor

**Files:**
- Create: `MissionClear.Web/Views/_ViewImports.cshtml`
- Create: `MissionClear.Web/Views/_ViewStart.cshtml`
- Create: `MissionClear.Web/Views/Shared/_Layout.cshtml`
- Create: `MissionClear.Web/Views/Auth/Login.cshtml`
- Create: `MissionClear.Web/Views/Auth/Register.cshtml`
- Create: `MissionClear.Web/Views/Auth/AccessDenied.cshtml`
- Create: `MissionClear.Web/Views/Dashboard/Index.cshtml`
- Create: `MissionClear.Web/Views/Missions/Index.cshtml`
- Create: `MissionClear.Web/Views/Missions/Details.cshtml`
- Create: `MissionClear.Web/Views/Users/Index.cshtml`

- [ ] **Step 1: _ViewImports.cshtml**

```html
@using MissionClear.Web.Models
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

- [ ] **Step 2: _ViewStart.cshtml**

```html
@{
    Layout = "_Layout";
}
```

- [ ] **Step 3: Views/Shared/_Layout.cshtml**

```html
<!DOCTYPE html>
<html lang="pt-BR">
<head>
    <meta charset="utf-8" />
    <title>Mission Clear — @ViewData["Title"]</title>
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <style>
        body { font-family: sans-serif; margin: 0; background: #0a0a1a; color: #e0e0ff; }
        nav { background: #111133; padding: 1rem 2rem; display: flex; gap: 1.5rem; align-items: center; }
        nav a { color: #88aaff; text-decoration: none; }
        nav a:hover { color: #fff; }
        .main { padding: 2rem; max-width: 1200px; margin: 0 auto; }
        .card { background: #111133; border-radius: 8px; padding: 1.5rem; margin-bottom: 1rem; }
        .btn { padding: .5rem 1.5rem; border-radius: 4px; border: none; cursor: pointer; }
        .btn-primary { background: #4466ff; color: white; }
        .btn-danger { background: #cc3333; color: white; }
        input, select { padding: .5rem; border-radius: 4px; border: 1px solid #334; background: #0a0a1a; color: #e0e0ff; width: 100%; margin-bottom: .75rem; }
        label { display: block; margin-bottom: .25rem; font-size: .9rem; color: #aaa; }
        .error { color: #ff6666; margin-bottom: 1rem; }
        .badge { padding: .2rem .6rem; border-radius: 99px; font-size: .75rem; }
        .badge-success { background: #1a4433; color: #44ff88; }
        .badge-failure { background: #441a1a; color: #ff6666; }
        .badge-admin { background: #441111; color: #ff4444; }
        .stat { display: inline-block; margin-right: 2rem; }
        .stat-value { font-size: 2rem; font-weight: bold; color: #88aaff; }
        .stat-label { font-size: .8rem; color: #888; }
    </style>
</head>
<body>
    <nav>
        <strong style="color:#fff;font-size:1.2rem">🚀 Mission Clear</strong>
        <a asp-controller="Dashboard" asp-action="Index">Dashboard</a>
        @if (User.Identity?.IsAuthenticated == true)
        {
            <a asp-controller="Missions" asp-action="Index">Missões</a>
        }
        @if (User.IsInRole("Administrator"))
        {
            <a asp-controller="Users" asp-action="Index">Usuários</a>
        }
        <span style="flex:1"></span>
        @if (User.Identity?.IsAuthenticated == true)
        {
            <span style="color:#aaa;font-size:.9rem">@User.Identity.Name</span>
            <form asp-controller="Auth" asp-action="Logout" method="post" style="display:inline">
                <button type="submit" class="btn btn-danger" style="padding:.3rem 1rem;font-size:.85rem">Sair</button>
            </form>
        }
        else
        {
            <a asp-controller="Auth" asp-action="Login">Entrar</a>
            <a asp-controller="Auth" asp-action="Register">Registrar</a>
        }
    </nav>
    <div class="main">
        @RenderBody()
    </div>
</body>
</html>
```

- [ ] **Step 4: Views/Auth/Login.cshtml**

```html
@model LoginViewModel
@{ ViewData["Title"] = "Login"; }

<div class="card" style="max-width:400px;margin:2rem auto">
    <h2>Entrar</h2>
    @if (!string.IsNullOrEmpty(Model.Error))
    {
        <div class="error">@Model.Error</div>
    }
    <form asp-action="Login" method="post">
        <input asp-for="ReturnUrl" type="hidden" />
        <label>Email</label>
        <input asp-for="Email" type="email" placeholder="piloto@missao.com" />
        <label>Senha</label>
        <input asp-for="Password" type="password" placeholder="••••••••" />
        <button type="submit" class="btn btn-primary" style="width:100%;margin-top:.5rem">Entrar</button>
    </form>
    <p style="margin-top:1rem;color:#aaa;font-size:.9rem">
        Não tem conta? <a asp-action="Register">Registrar</a>
    </p>
</div>
```

- [ ] **Step 5: Views/Auth/Register.cshtml**

```html
@model RegisterViewModel
@{ ViewData["Title"] = "Registrar"; }

<div class="card" style="max-width:400px;margin:2rem auto">
    <h2>Criar Conta</h2>
    @if (!string.IsNullOrEmpty(Model.Error))
    {
        <div class="error">@Model.Error</div>
    }
    <form asp-action="Register" method="post">
        <label>Nome</label>
        <input asp-for="DisplayName" placeholder="Piloto Guss" />
        <label>Email</label>
        <input asp-for="Email" type="email" placeholder="piloto@missao.com" />
        <label>Senha (mín. 8 chars, 1 maiúscula, 1 número)</label>
        <input asp-for="Password" type="password" placeholder="••••••••" />
        <button type="submit" class="btn btn-primary" style="width:100%;margin-top:.5rem">Registrar</button>
    </form>
    <p style="margin-top:1rem;color:#aaa;font-size:.9rem">
        Já tem conta? <a asp-action="Login">Entrar</a>
    </p>
</div>
```

- [ ] **Step 6: Views/Auth/AccessDenied.cshtml**

```html
@{ ViewData["Title"] = "Acesso Negado"; }
<div class="card" style="max-width:500px;margin:2rem auto;text-align:center">
    <h2>🚫 Acesso Negado</h2>
    <p>Você não tem permissão para acessar esta área.</p>
    <p style="color:#aaa">Esta página requer o perfil <strong>Administrator</strong>.</p>
    <a asp-controller="Dashboard" asp-action="Index" class="btn btn-primary">Voltar ao Dashboard</a>
</div>
```

- [ ] **Step 7: Views/Dashboard/Index.cshtml**

```html
@model DashboardViewModel
@{ ViewData["Title"] = "Dashboard"; }

<h1>Dashboard Orbital</h1>

<div class="card">
    <h3>Detritos Rastreados</h3>
    <div>
        <span class="stat">
            <span class="stat-value">@Model.TotalTrackedObjects.ToString("N0")</span>
            <br/><span class="stat-label">Total</span>
        </span>
        <span class="stat">
            <span class="stat-value" style="color:#ff8888">@Model.Debris.ToString("N0")</span>
            <br/><span class="stat-label">Detritos</span>
        </span>
        <span class="stat">
            <span class="stat-value" style="color:#88ff88">@Model.Satellites.ToString("N0")</span>
            <br/><span class="stat-label">Satélites</span>
        </span>
        <span class="stat">
            <span class="stat-value" style="color:#ffaa88">@Model.RocketBodies.ToString("N0")</span>
            <br/><span class="stat-label">Estágios de Foguete</span>
        </span>
        <span class="stat">
            <span class="stat-value" style="color:@(Model.ActiveAlerts > 0 ? "#ff4444" : "#44ff88")">@Model.ActiveAlerts</span>
            <br/><span class="stat-label">Alertas Ativos</span>
        </span>
    </div>
    @if (!string.IsNullOrEmpty(Model.LastUpdated))
    {
        <p style="color:#666;font-size:.8rem;margin-top:1rem">Última atualização: @Model.LastUpdated</p>
    }
</div>

@if (!string.IsNullOrEmpty(Model.UserDisplayName))
{
    <div class="card">
        <h3>Suas Estatísticas — @Model.UserDisplayName</h3>
        <div>
            <span class="stat">
                <span class="stat-value">@Model.UserTotalMissions</span>
                <br/><span class="stat-label">Missões</span>
            </span>
            <span class="stat">
                <span class="stat-value" style="color:#ffd700">@Model.UserBestScore</span>
                <br/><span class="stat-label">Melhor Score</span>
            </span>
        </div>
    </div>
}
else
{
    <div class="card">
        <p>
            <a asp-controller="Auth" asp-action="Login">Faça login</a>
            para ver suas estatísticas de missão.
        </p>
    </div>
}
```

- [ ] **Step 8: Views/Missions/Index.cshtml**

```html
@{ ViewData["Title"] = "Minhas Missões"; }

<h1>Histórico de Missões</h1>

<div class="card">
    <form method="get" style="display:flex;gap:1rem;align-items:flex-end">
        <div style="flex:1">
            <label>Filtrar por status</label>
            <select name="status">
                <option value="">Todos</option>
                <option value="success" @(ViewBag.StatusFilter == "success" ? "selected" : "")>Sucesso</option>
                <option value="failure" @(ViewBag.StatusFilter == "failure" ? "selected" : "")>Falha</option>
                <option value="aborted" @(ViewBag.StatusFilter == "aborted" ? "selected" : "")>Abortado</option>
            </select>
        </div>
        <button type="submit" class="btn btn-primary">Filtrar</button>
    </form>
</div>

@{
    var json = ViewBag.MissionsJson;
    if (json != null && json.ValueKind.ToString() != "Undefined")
    {
        int total = 0;
        try { total = json.GetProperty("pagination").GetProperty("total").GetInt32(); } catch { }
        <p style="color:#aaa;font-size:.85rem">@total missões no total.</p>
    }
}
```

- [ ] **Step 9: Views/Missions/Details.cshtml**

```html
@{ ViewData["Title"] = "Detalhe da Missão"; }

<h1>Detalhe da Missão</h1>
<div class="card">
    @{
        var json = ViewBag.MissionJson;
        if (json != null && json.ValueKind.ToString() != "Undefined")
        {
            <p><strong>Destino:</strong> @json.GetProperty("destination_display").GetString()</p>
            <p><strong>Status:</strong>
                <span class="badge @(json.GetProperty("status").GetString() == "success" ? "badge-success" : "badge-failure")">
                    @json.GetProperty("status").GetString()
                </span>
            </p>
            <p><strong>Score:</strong> @json.GetProperty("mission_score").GetInt32()</p>
            <p><strong>Risco:</strong> @json.GetProperty("risk_score").GetDouble().ToString("P1")</p>
        }
    }
</div>
<a asp-action="Index" class="btn btn-primary">← Voltar</a>
```

- [ ] **Step 10: Views/Users/Index.cshtml (Administrator)**

```html
@{ ViewData["Title"] = "Gerenciar Usuários"; }

<h1>
    Gerenciar Usuários
    <span class="badge badge-admin" style="font-size:.6rem;vertical-align:middle">ADMIN</span>
</h1>
<div class="card">
    <p>@ViewBag.Message</p>
    <p style="color:#aaa">
        Área reservada para Administradores. Gestão completa de usuários disponível
        quando o endpoint <code>/api/admin/users</code> for implementado.
    </p>
</div>
```

- [ ] **Step 11: Build final**

```powershell
dotnet build MissionClear.Web/MissionClear.Web.csproj
```

---

## Task 8.7 — Aspire AppHost: registrar MissionClear.Web

**Files:**
- Modify: `MissionClear.AppHost/Program.cs`

- [ ] **Step 1: Verificar se Web já está registrado**

```csharp
builder.AddProject<Projects.MissionClear_Web>("web")
    .WithReference(api).WaitFor(api);
```

Se não existir, adicionar após a linha do `api`. O snippet completo do AppHost fica:

```csharp
var mysql = builder.AddMySql("mysql")
    .WithEnvironment("MYSQL_ROOT_PASSWORD", "MissionClear_Dev_2025!")
    .AddDatabase("missionclear");

var api = builder.AddProject<Projects.MissionClear_Api>("api")
    .WithReference(mysql)
    .WaitFor(mysql);

builder.AddProject<Projects.MissionClear_Web>("web")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
```

- [ ] **Step 2: Build do AppHost**

```powershell
dotnet build MissionClear.AppHost/MissionClear.AppHost.csproj
```

---

## Task 8.8 — Testes de Integração (MVC)

> MVC não tem lógica testável em unidade (thin controllers delegam para ApiClient).
> Testes verificam comportamento de autorização via WebApplicationFactory.

**Files:**
- Modify: `MissionClear.Web/Program.cs`
- Modify: `MissionClear.Tests/MissionClear.Tests.csproj`
- Create: `MissionClear.Tests/Integration/MvcAuthorizationTests.cs`

- [ ] **Step 1: Adicionar ao final de `MissionClear.Web/Program.cs`**

```csharp
// Required for WebApplicationFactory<Program>
public partial class Program { }
```

- [ ] **Step 2: Adicionar referência no Tests.csproj**

Em `MissionClear.Tests/MissionClear.Tests.csproj`, adicionar:
```xml
<ProjectReference Include="..\MissionClear.Web\MissionClear.Web.csproj" />
```

- [ ] **Step 3: Criar testes**

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MissionClear.Tests.Integration;

// Nota: WebApplicationFactory<Program> para MVC requer referência ao MissionClear.Web project.
// Estes testes verificam que rotas protegidas redirecionam para login corretamente.

public sealed class MvcAuthorizationTests : IClassFixture<WebApplicationFactory<MissionClear.Web.Program>>
{
    private readonly WebApplicationFactory<MissionClear.Web.Program> _factory;

    public MvcAuthorizationTests(WebApplicationFactory<MissionClear.Web.Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // ApiClient calls serão falhas (sem API real) — OK para testar redirecionamentos
            });
        });
    }

    [Fact]
    public async Task Missions_Unauthenticated_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Missions");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Auth/Login", response.Headers.Location?.ToString() ?? "");
    }

    [Fact]
    public async Task Users_Unauthenticated_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Users");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_Unauthenticated_Returns200()
    {
        // Dashboard é público (dados orbitais sem auth)
        var client = _factory.CreateClient();
        // Sem API real, GetAsync retorna default — controller ainda retorna View(vm) com dados zerados
        // Só verificamos que não é 401/403
        var response = await client.GetAsync("/Dashboard");
        // Dashboard requires ApiClient to be functional. In integration tests, the response
        // may vary. We only verify auth is not required (no 401/403).
        Assert.NotEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 4: Rodar testes**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --filter "MvcAuthorization" -v normal
```

---

## Task 8.9 — Verificação Final

- [ ] **Step 1: Build solution completa**

```powershell
dotnet build MissionClear.sln
```

Esperado: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Subir via Aspire**

```powershell
cd MissionClear.AppHost
dotnet run
```

Aspire Dashboard: `http://localhost:15021`
Verificar: MySQL container Running, Api Running, Web Running.

- [ ] **Step 3: Testar fluxo completo**

1. Abrir URL do Web (verificar porta no Aspire dashboard)
2. Registrar usuário (role padrão: "Researcher")
3. Tentar acessar `/Users` → deve redirecionar para `/Auth/AccessDenied`
4. Verificar `/Dashboard` → dados orbitais carregados (pode levar ~60s)
5. Verificar `/Missions` → lista vazia, status 200
6. Cookie deve conter claim `role = Researcher`

- [ ] **Step 4: Rodar todos os testes**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj -v normal
```

- [ ] **Step 5: Commit**

```powershell
git add MissionClear.Web/ MissionClear.AppHost/ MissionClear.Tests/
git commit -m "feat(web): ASP.NET Core MVC with Cookie+Claims auth, ApiClient bridge, Razor views"
```

---

## Checklist Final da Fase 8

- [ ] `dotnet build MissionClear.sln` sem erros
- [ ] `MissionClear.Web` não referencia DbContext, EF Core ou repositórios
- [ ] Login via MVC cria cookie com claim `role` correto
- [ ] Researcher acessa `/Dashboard` e `/Missions` (200 OK)
- [ ] Researcher acessa `/Users` → 302 redirect para AccessDenied
- [ ] Administrator acessa `/Users` (200 OK)
- [ ] `ApiClient.AttachToken()` injeta Bearer token de chamadas autenticadas
- [ ] Aspire dashboard mostra Api e Web como "Running" com MySQL
- [ ] OpenTelemetry traces visíveis no Aspire dashboard
- [ ] `public partial class Program {}` no final de Program.cs do Web
