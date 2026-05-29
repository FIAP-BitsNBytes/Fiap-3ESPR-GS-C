# Phase 08 — MVC Web Project (Cookie + Claims)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Implementar projeto ASP.NET Core MVC com autenticação Cookie+Claims, consumindo a API via HttpClient. Zero acesso direto ao banco.

**Regras:**
- MVC nunca usa DbContext, Entity Framework, nem repositórios
- Toda comunicação com dados via `ApiClient` (IHttpClientFactory)
- Login chama `POST /api/auth/login`, recebe JWT, cria Cookie com Claims (incluindo `role`)
- `[Authorize(Roles = "Administrator")]` protege `/Users` controller
- Researcher: vê só suas missões. Administrator: vê dashboard completo e gerencia usuários

**Scope do MVC:**
| Rota | Auth | Role |
|---|---|---|
| `/Auth/Login` | Público | — |
| `/Auth/Register` | Público | — |
| `/Auth/Logout` | Autenticado | — |
| `/Dashboard` | Opcional | — |
| `/Debris` | Público | — |
| `/Debris/Details/{id}` | Público | — |
| `/LaunchWindows` | Público | — |
| `/Missions` | Autenticado | Any |
| `/Missions/Details/{id}` | Autenticado | Any |
| `/Users` | Autenticado | Administrator |

---

### Task 1: Program.cs do MVC (Cookie Auth + HttpClient)

**Files:**
- Modify: `MissionClear.Web/Program.cs`

- [ ] **Step 1: Substituir Program.cs**

```csharp
using MissionClear.ServiceDefaults;
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

// Cookie Authentication with Claims
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

// ApiClient — usa service discovery do Aspire para resolver "http://api"
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri("http://api"); // Resolvido pelo Aspire service discovery
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

---

### Task 2: ApiClient (HttpClient wrapper)

**Files:**
- Create: `MissionClear.Web/Services/ApiClient.cs`

- [ ] **Step 1: Criar diretório**

```powershell
mkdir MissionClear.Web/Services
```

- [ ] **Step 2: Escrever ApiClient.cs**

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
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

    // Auth-specific: login returns JWT, MVC creates Cookie
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

// Minimal response types for API consumption (MVC-side)
public sealed record LoginApiResponse(
    LoginUserDto User,
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

public sealed record RegisterApiResponse(
    LoginUserDto User,
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

public sealed record LoginUserDto(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    string CreatedAt,
    int? TotalMissions,
    int? BestScore);
```

---

### Task 3: ViewModels

**Files:**
- Create: `MissionClear.Web/Models/LoginViewModel.cs`
- Create: `MissionClear.Web/Models/RegisterViewModel.cs`
- Create: `MissionClear.Web/Models/DashboardViewModel.cs`

- [ ] **Step 1: Criar diretório e ViewModels**

```powershell
mkdir MissionClear.Web/Models
```

`LoginViewModel.cs`:
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

`RegisterViewModel.cs`:
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

`DashboardViewModel.cs`:
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

    // User data (if authenticated)
    public string? UserDisplayName { get; set; }
    public int? UserTotalMissions { get; set; }
    public int? UserBestScore { get; set; }
}
```

---

### Task 4: AuthController (MVC) — Cookie+Claims

**Files:**
- Create: `MissionClear.Web/Controllers/AuthController.cs`

- [ ] **Step 1: Criar diretório e AuthController.cs**

```powershell
mkdir MissionClear.Web/Controllers
```

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
    public IActionResult AccessDenied()
        => View();

    private async Task SignInWithCookieAsync(LoginUserDto user, string accessToken, string refreshToken)
    {
        // Injetar Claims incluindo role — role vem da resposta da API
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role),        // "Researcher" ou "Administrator"
            new("access_token", accessToken),       // Para repassar à API em chamadas futuras
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

---

### Task 5: DashboardController (MVC)

**Files:**
- Create: `MissionClear.Web/Controllers/DashboardController.cs`

- [ ] **Step 1: Criar DashboardController.cs**

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

---

### Task 6: MissionsController (MVC) + UsersController (Admin)

**Files:**
- Create: `MissionClear.Web/Controllers/MissionsController.cs`
- Create: `MissionClear.Web/Controllers/UsersController.cs`

- [ ] **Step 1: Criar MissionsController.cs**

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
        var json = await apiClient.GetAsync<JsonElement>($"/api/missions/msn_{id}");
        if (json.ValueKind == JsonValueKind.Undefined)
            return NotFound();
        ViewBag.MissionJson = json;
        return View();
    }
}
```

- [ ] **Step 2: Criar UsersController.cs (Administrator only)**

```csharp
using MissionClear.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MissionClear.Web.Controllers;

[Authorize(Roles = "Administrator")]
public sealed class UsersController(ApiClient apiClient) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // Nota: não existe endpoint /api/users (lista todos) no contrato atual.
        // Este controller é preparado para quando esse endpoint for adicionado.
        // Por ora, mostra mensagem informativa para o Administrator.
        ViewBag.Message = "Área administrativa de usuários.";
        return View();
    }
}
```

---

### Task 7: Views (Razor)

**Files:**
- Create: `MissionClear.Web/Views/Shared/_Layout.cshtml`
- Create: `MissionClear.Web/Views/Auth/Login.cshtml`
- Create: `MissionClear.Web/Views/Auth/Register.cshtml`
- Create: `MissionClear.Web/Views/Auth/AccessDenied.cshtml`
- Create: `MissionClear.Web/Views/Dashboard/Index.cshtml`
- Create: `MissionClear.Web/Views/Missions/Index.cshtml`
- Create: `MissionClear.Web/Views/Users/Index.cshtml`
- Create: `MissionClear.Web/Views/_ViewImports.cshtml`
- Create: `MissionClear.Web/Views/_ViewStart.cshtml`

- [ ] **Step 1: Criar estrutura de Views**

```powershell
mkdir MissionClear.Web/Views
mkdir MissionClear.Web/Views/Shared
mkdir MissionClear.Web/Views/Auth
mkdir MissionClear.Web/Views/Dashboard
mkdir MissionClear.Web/Views/Missions
mkdir MissionClear.Web/Views/Users
```

- [ ] **Step 2: _ViewImports.cshtml**

```html
@using MissionClear.Web.Models
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

- [ ] **Step 3: _ViewStart.cshtml**

```html
@{
    Layout = "_Layout";
}
```

- [ ] **Step 4: _Layout.cshtml**

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
        .badge-critical { background: #441111; color: #ff4444; }
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
            <span style="color:#aaa">@User.Identity.Name</span>
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

- [ ] **Step 5: Login.cshtml**

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

- [ ] **Step 6: Register.cshtml**

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

- [ ] **Step 7: AccessDenied.cshtml**

```html
@{ ViewData["Title"] = "Acesso Negado"; }
<div class="card" style="max-width:500px;margin:2rem auto;text-align:center">
    <h2>🚫 Acesso Negado</h2>
    <p>Você não tem permissão para acessar esta área.</p>
    <p style="color:#aaa">Esta página requer o perfil <strong>Administrator</strong>.</p>
    <a asp-controller="Dashboard" asp-action="Index" class="btn btn-primary">Voltar ao Dashboard</a>
</div>
```

- [ ] **Step 8: Dashboard/Index.cshtml**

```html
@model DashboardViewModel
@{ ViewData["Title"] = "Dashboard"; }

<h1>Dashboard Orbital</h1>

<div class="card">
    <h3>Detritos Rastreados</h3>
    <div>
        <span class="stat"><span class="stat-value">@Model.TotalTrackedObjects.ToString("N0")</span><br/><span class="stat-label">Total</span></span>
        <span class="stat"><span class="stat-value" style="color:#ff8888">@Model.Debris.ToString("N0")</span><br/><span class="stat-label">Detritos</span></span>
        <span class="stat"><span class="stat-value" style="color:#88ff88">@Model.Satellites.ToString("N0")</span><br/><span class="stat-label">Satélites</span></span>
        <span class="stat"><span class="stat-value" style="color:#ffaa88">@Model.RocketBodies.ToString("N0")</span><br/><span class="stat-label">Estágios</span></span>
        <span class="stat"><span class="stat-value" style="color:@(Model.ActiveAlerts > 0 ? "#ff4444" : "#44ff88")">@Model.ActiveAlerts</span><br/><span class="stat-label">Alertas Ativos</span></span>
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
            <span class="stat"><span class="stat-value">@Model.UserTotalMissions</span><br/><span class="stat-label">Missões</span></span>
            <span class="stat"><span class="stat-value" style="color:#ffd700">@Model.UserBestScore</span><br/><span class="stat-label">Melhor Score</span></span>
        </div>
    </div>
}
else
{
    <div class="card">
        <p><a asp-controller="Auth" asp-action="Login">Faça login</a> para ver suas estatísticas de missão.</p>
    </div>
}
```

- [ ] **Step 9: Missions/Index.cshtml**

```html
@{ ViewData["Title"] = "Minhas Missões"; }

<h1>Histórico de Missões</h1>

<div class="card">
    <form method="get" style="display:flex;gap:1rem;align-items:flex-end">
        <div>
            <label>Filtrar por status</label>
            <select name="status">
                <option value="">Todos</option>
                <option value="success" selected="@(ViewBag.StatusFilter == "success")">Sucesso</option>
                <option value="failure" selected="@(ViewBag.StatusFilter == "failure")">Falha</option>
                <option value="aborted" selected="@(ViewBag.StatusFilter == "aborted")">Abortado</option>
            </select>
        </div>
        <button type="submit" class="btn btn-primary">Filtrar</button>
    </form>
</div>

@if (ViewBag.MissionsJson.ValueKind.ToString() != "Undefined")
{
    @* Rendering via raw JSON — in production prefer typed ViewModels *@
    <p style="color:#aaa;font-size:.85rem">Dados carregados via API. @ViewBag.MissionsJson.GetProperty("pagination").GetProperty("total").GetInt32() missões no total.</p>
}
```

- [ ] **Step 10: Users/Index.cshtml (Administrator)**

```html
@{ ViewData["Title"] = "Gerenciar Usuários"; }

<h1>Gerenciar Usuários <span class="badge badge-critical" style="font-size:.6rem;vertical-align:middle">ADMIN</span></h1>
<div class="card">
    <p>@ViewBag.Message</p>
    <p style="color:#aaa">Esta área reservada para Administradores. Recursos de gerenciamento de usuários serão adicionados quando o endpoint <code>/api/admin/users</code> for implementado.</p>
</div>
```

---

### Task 8: Build e Verificação Final

- [ ] **Step 1: Build de toda a solution**

```powershell
dotnet build MissionClear.sln
```

Resultado esperado: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Subir via Aspire**

```powershell
cd MissionClear.AppHost
dotnet run
```

Abre o Aspire Dashboard em `http://localhost:15021`.
Verificar: MySQL container subindo, Api e Web em status "Running".

- [ ] **Step 3: Testar fluxo completo**

1. Abrir `http://localhost:XXXX` (porta do Web — verificar no Aspire dashboard)
2. Registrar usuário com role "Researcher"
3. Verificar que `/Users` retorna 403
4. Fazer login — verificar claims no cookie
5. Acessar `/Dashboard` — verificar dados orbitais (pode levar ~60s para cache ficar pronto)
6. Acessar `/Missions` — lista vazia mas 200 OK

- [ ] **Step 4: Rodar todos os testes**

```powershell
cd ..  # volta para raiz
dotnet test MissionClear.Tests/MissionClear.Tests.csproj -v normal
```

- [ ] **Step 5: Commit final**

```powershell
git add MissionClear.Web/
git commit -m "feat(web): ASP.NET Core MVC with Cookie+Claims auth, ApiClient, Razor views"
```

---

## Checklist Final da Fase 8

- [ ] `dotnet build` sem erros
- [ ] Login via MVC cria cookie com claim `role` correto
- [ ] Researcher acessa `/Dashboard` e `/Missions`
- [ ] Researcher recebe 403 ao acessar `/Users`
- [ ] Administrator acessa `/Users`
- [ ] MVC não tem nenhuma referência a DbContext, Entity Framework ou repositórios
- [ ] Aspire dashboard mostra Api e Web como "Running" com MySQL
- [ ] OpenTelemetry traces visíveis no Aspire dashboard
