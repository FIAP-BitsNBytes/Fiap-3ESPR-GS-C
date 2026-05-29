# Implementation Plan 00: Solution Structure + Aspire Setup

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Criar a estrutura completa da solution com .NET Aspire como orquestrador central: 5 projetos (AppHost, ServiceDefaults, Api, Web, Tests), container MySQL provisionado automaticamente, OpenTelemetry configurado e todos os projetos adicionados à solution.

**Architecture:**
- `MissionClear.AppHost` — orquestrador Aspire, provisiona container MySQL, conecta projetos via service discovery
- `MissionClear.ServiceDefaults` — OpenTelemetry, health checks e resilience compartilhados entre Api e Web
- `MissionClear.Api` — REST API com JWT Bearer (net10.0), referencia ServiceDefaults
- `MissionClear.Web` — ASP.NET Core MVC (net10.0), referencia ServiceDefaults
- `MissionClear.Tests` — xUnit, FluentAssertions, Moq (net10.0), referencia Api

**Execution order:** First. Everything else depends on this.

**Pre-requisites (BLOQUEANTES — verificar ANTES de iniciar):**

```powershell
# 1. Docker Desktop deve estar rodando (Aspire provisiona MySQL como container)
docker ps
# deve retornar a lista sem erro

# 2. Instalar Aspire workload (uma vez por máquina)
dotnet workload install aspire

# 3. Confirmar instalação
dotnet workload list
# deve aparecer: aspire   9.x.x   ...
```

---

## Task 1: Criar Solution + Adicionar Projetos Existentes (commit 1)

**Files:**
- Create: `MissionClear.sln`

**Contexto:** O repo já contém `MissionClear.Api/` e `MissionClear.Tests/` com código existente. A solution ainda não existe — criar e adicionar os projetos existentes sem deletar nada.

- [ ] **Step 1: Criar solution no diretório raiz**

```powershell
# Executar na raiz: C:\Users\Gustavo\Documents\Repositorios\FIAP\3ESPR-GS\Fiap-3ESPR-GS-C
dotnet new sln -n MissionClear
```

Resultado esperado: `The template "Solution File" was created successfully.`

- [ ] **Step 2: Adicionar projetos existentes**

```powershell
dotnet sln MissionClear.sln add MissionClear.Api/MissionClear.Api.csproj
dotnet sln MissionClear.sln add MissionClear.Tests/MissionClear.Tests.csproj
```

- [ ] **Step 3: Verificar**

```powershell
dotnet sln MissionClear.sln list
```

Resultado esperado:
```
Project(s)
----------
MissionClear.Api/MissionClear.Api.csproj
MissionClear.Tests/MissionClear.Tests.csproj
```

- [ ] **Step 4: Commit**

```powershell
git add MissionClear.sln
git commit -m "chore: create solution file and add existing Api + Tests projects"
```

---

## Task 2: Criar MissionClear.AppHost (commit 2)

**Files:**
- Create: `MissionClear.AppHost/MissionClear.AppHost.csproj`
- Create: `MissionClear.AppHost/Program.cs`

**Nota:** AppHost e ServiceDefaults usam `net8.0` por compatibilidade com o workload Aspire 9.x. Api e Web usam `net10.0`.

- [ ] **Step 1: Criar projeto AppHost via template Aspire**

```powershell
dotnet new aspire-apphost -n MissionClear.AppHost -o MissionClear.AppHost
```

Resultado esperado: `The template "Aspire App Host" was created successfully.`

- [ ] **Step 2: Substituir csproj pelo conteúdo definitivo**

Abrir `MissionClear.AppHost/MissionClear.AppHost.csproj` e substituir conteúdo completo:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsAspireHost>true</IsAspireHost>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="9.1.0" />
    <PackageReference Include="Aspire.Hosting.MySql" Version="9.1.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MissionClear.Api\MissionClear.Api.csproj" />
    <ProjectReference Include="..\MissionClear.Web\MissionClear.Web.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Escrever Program.cs do AppHost**

Substituir `MissionClear.AppHost/Program.cs` pelo conteúdo definitivo:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Provisiona container MySQL automaticamente via Docker Desktop
// Connection string injetada via service discovery nos projetos que referenciam "missionclear"
var mysql = builder.AddMySql("mysql")
    .WithEnvironment("MYSQL_ROOT_PASSWORD", "MissionClear_Dev_2025!")
    .AddDatabase("missionclear");

// Api aguarda MySQL estar healthy antes de iniciar
var api = builder.AddProject<Projects.MissionClear_Api>("api")
    .WithReference(mysql)
    .WaitFor(mysql);

// Web MVC aguarda Api estar healthy antes de iniciar
builder.AddProject<Projects.MissionClear_Web>("web")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
```

- [ ] **Step 4: Adicionar AppHost à solution**

```powershell
dotnet sln MissionClear.sln add MissionClear.AppHost/MissionClear.AppHost.csproj
```

- [ ] **Step 5: Commit**

```powershell
git add MissionClear.AppHost/
git add MissionClear.sln
git commit -m "feat: add Aspire AppHost with MySQL container and project orchestration"
```

---

## Task 3: Criar MissionClear.ServiceDefaults (commit 3)

**Files:**
- Create: `MissionClear.ServiceDefaults/MissionClear.ServiceDefaults.csproj`
- Create: `MissionClear.ServiceDefaults/Extensions.cs`

- [ ] **Step 1: Criar projeto ServiceDefaults via template Aspire**

```powershell
dotnet new aspire-servicedefaults -n MissionClear.ServiceDefaults -o MissionClear.ServiceDefaults
```

Resultado esperado: `The template "Aspire Service Defaults" was created successfully.`

- [ ] **Step 2: Substituir csproj pelo conteúdo definitivo**

Abrir `MissionClear.ServiceDefaults/MissionClear.ServiceDefaults.csproj` e substituir conteúdo completo:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.10.0" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" Version="9.1.0" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.10.0" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.10.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.10.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.10.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Escrever Extensions.cs**

Substituir `MissionClear.ServiceDefaults/Extensions.cs` pelo conteúdo definitivo:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MissionClear.ServiceDefaults;

public static class Extensions
{
    /// <summary>
    /// Registra OpenTelemetry, service discovery, resilience e health checks.
    /// Chamado em Program.cs de Api e Web antes de qualquer outro serviço.
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    /// <summary>
    /// Mapeia /health e /alive. Chamar após app.UseRouting() em Api e Web.
    /// Endpoints só ficam ativos em Development para evitar exposição em produção.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
            app.MapHealthChecks("/alive", new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }
        return app;
    }
}
```

- [ ] **Step 4: Adicionar ServiceDefaults à solution**

```powershell
dotnet sln MissionClear.sln add MissionClear.ServiceDefaults/MissionClear.ServiceDefaults.csproj
```

- [ ] **Step 5: Commit**

```powershell
git add MissionClear.ServiceDefaults/
git add MissionClear.sln
git commit -m "feat: add ServiceDefaults with OpenTelemetry, health checks and service discovery"
```

---

## Task 4: Criar MissionClear.Web (commit 4)

**Files:**
- Create: `MissionClear.Web/MissionClear.Web.csproj`
- Create: `MissionClear.Web/Program.cs`

- [ ] **Step 1: Criar projeto MVC**

```powershell
dotnet new mvc -n MissionClear.Web -o MissionClear.Web
```

Resultado esperado: `The template "ASP.NET Core Web App (Model-View-Controller)" was created successfully.`

- [ ] **Step 2: Substituir csproj pelo conteúdo definitivo**

Abrir `MissionClear.Web/MissionClear.Web.csproj` e substituir conteúdo completo:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MissionClear.ServiceDefaults\MissionClear.ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Substituir Program.cs pelo stub mínimo com Aspire**

```csharp
using MissionClear.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Aspire: OpenTelemetry, health checks, service discovery
builder.AddServiceDefaults();

builder.Services.AddControllersWithViews();

// Cookie auth será adicionado na Fase 8 (MVC Web)
// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)...

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultControllerRoute();
app.MapDefaultEndpoints(); // /health + /alive (dev only)

app.Run();
```

- [ ] **Step 4: Adicionar Web à solution**

```powershell
dotnet sln MissionClear.sln add MissionClear.Web/MissionClear.Web.csproj
```

- [ ] **Step 5: Commit**

```powershell
git add MissionClear.Web/
git add MissionClear.sln
git commit -m "feat: add Web MVC project skeleton with Aspire ServiceDefaults"
```

---

## Task 5: Atualizar MissionClear.Api para Aspire + MySQL (commit 5)

**Files:**
- Modify: `MissionClear.Api/MissionClear.Api.csproj`
- Modify: `MissionClear.Api/Program.cs`
- Modify: `MissionClear.Api/appsettings.json`
- Modify: `MissionClear.Api/appsettings.Development.json`

**Contexto:** O Api existente usa SQLite. Precisa migrar para MySQL via Aspire (connection string injetada automaticamente pelo AppHost) e adicionar referência ao ServiceDefaults.

### Step 1: Substituir MissionClear.Api.csproj

Abrir `MissionClear.Api/MissionClear.Api.csproj` e substituir conteúdo completo:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Aspire Pomelo: builder.AddMySqlDbContext<AppDbContext>("missionclear")
         lê a connection string injetada pelo AppHost automaticamente -->
    <PackageReference Include="Aspire.Pomelo.EntityFrameworkCore.MySql" Version="9.1.0" />
    <PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.10" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.10">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="8.0.10">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MissionClear.ServiceDefaults\MissionClear.ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

**Nota de compatibilidade:** `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.10` é compatível com net10.0 (multi-target). Se o build reclamar, atualizar para versão `10.0.0` quando disponível.

### Step 2: Substituir Program.cs

Substituir `MissionClear.Api/Program.cs` pelo conteúdo definitivo:

```csharp
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
```

### Step 3: Atualizar appsettings.json

Substituir `MissionClear.Api/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Jwt": {
    "Secret": "",
    "Issuer": "mission-clear-api",
    "Audience": "mission-clear-mobile",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 7
  },
  "Orbital": {
    "TleFetchIntervalMinutes": 60,
    "PropagationIntervalSeconds": 60,
    "MaxDebrisCount": 30000,
    "TleMaxAgeDays": 7
  },
  "ExternalApi": {
    "CelesTrakBaseUrl": "https://celestrak.org/NORAD/elements/gp.php?GROUP=debris&FORMAT=json",
    "KeepTrackBaseUrl": "https://keeptrack.space/api",
    "KeepTrackApiKey": "",
    "KeepTrackTimeoutSeconds": 5
  },
  "Cors": {
    "AllowedOrigins": []
  },
  "Sessions": {
    "TtlMinutes": 30
  }
}
```

**Nota:** `ConnectionStrings:missionclear` NÃO fica no appsettings.json — é injetada pelo AppHost via service discovery. Em stand-alone dev sem Aspire, adicionar no appsettings.Development.json.

### Step 4: Atualizar appsettings.Development.json

Substituir `MissionClear.Api/appsettings.Development.json`:

```json
{
  "Jwt": {
    "Secret": "dev-only-secret-change-me-please-32chars-minimum-xxxxx"
  },
  "Cors": {
    "AllowedOrigins": ["*"]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "ConnectionStrings": {
    "missionclear": "Server=localhost;Port=3306;Database=missionclear;User=root;Password=MissionClear_Dev_2025!;"
  }
}
```

**Security note:** `appsettings.Production.json` está no `.gitignore`. A `Jwt__Secret` de produção deve ser fornecida via variável de ambiente.

- [ ] **Step 5: Commit**

```powershell
git add MissionClear.Api/MissionClear.Api.csproj
git add MissionClear.Api/Program.cs
git add MissionClear.Api/appsettings.json
git add MissionClear.Api/appsettings.Development.json
git commit -m "feat(api): migrate to MySQL via Aspire, add ServiceDefaults, update Program.cs"
```

---

## Task 6: Atualizar MissionClear.Tests.csproj (commit 6)

**Files:**
- Modify: `MissionClear.Tests/MissionClear.Tests.csproj`

- [ ] **Step 1: Substituir csproj pelo conteúdo definitivo**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="6.12.1" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.10" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.10" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MissionClear.Api\MissionClear.Api.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Commit**

```powershell
git add MissionClear.Tests/MissionClear.Tests.csproj
git commit -m "chore(tests): update Tests csproj to net10.0 with Moq and Mvc.Testing"
```

---

## Task 7: Build + Testes de Verificação (commit 7)

**Contexto:** Verificar que todos os 5 projetos compilam e os testes existentes continuam passando.

- [ ] **Step 1: Build de todos os projetos**

```powershell
dotnet build MissionClear.sln
```

Resultado esperado:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Troubleshooting de build:**

| Erro | Solução |
|------|---------|
| `Projects.MissionClear_Web` not found in AppHost | Verificar que `MissionClear.Web.csproj` tem `<ProjectReference>` no AppHost csproj |
| `net10.0` incompatível com pacote X | Verificar versão do pacote; algumas 8.x suportam net10 via multi-targeting |
| `AddMySqlDbContext` not found | Confirmar `Aspire.Pomelo.EntityFrameworkCore.MySql` está no csproj da Api |
| `AddServiceDefaults` not found | Confirmar `ProjectReference` para `ServiceDefaults` no csproj da Api e Web |

- [ ] **Step 2: Rodar testes existentes**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --verbosity normal
```

Resultado esperado: todos os testes de `Configuration/AppSettingsTests.cs` e `Helpers/` passam.

```
Passed!  - Failed: 0, Passed: 17, Skipped: 0, Total: 17
```

- [ ] **Step 3: Verificar estrutura da solution**

```powershell
dotnet sln MissionClear.sln list
```

Resultado esperado (5 projetos):
```
Project(s)
----------
MissionClear.Api/MissionClear.Api.csproj
MissionClear.Tests/MissionClear.Tests.csproj
MissionClear.AppHost/MissionClear.AppHost.csproj
MissionClear.ServiceDefaults/MissionClear.ServiceDefaults.csproj
MissionClear.Web/MissionClear.Web.csproj
```

- [ ] **Step 4: Commit**

```powershell
git add .
git commit -m "chore: verify full solution build and all tests pass post-Aspire setup"
```

---

## Task 8: Smoke Test do Aspire Dashboard (manual)

**Pre-requisite:** Docker Desktop rodando.

- [ ] **Step 1: Iniciar o AppHost**

```powershell
dotnet run --project MissionClear.AppHost/MissionClear.AppHost.csproj
```

Resultado esperado no terminal:
```
Login to the dashboard at: http://localhost:15021/login?t=<token>
```

- [ ] **Step 2: Verificar containers**

```powershell
docker ps
```

Deve aparecer um container MySQL rodando (imagem `mysql:latest` ou similar).

- [ ] **Step 3: Abrir dashboard**

Abrir `http://localhost:15021` no browser. Verificar:
- Recursos `api`, `web` e `mysql` aparecem na lista
- Status do MySQL fica `Running` após ~30s
- Status da Api fica `Running` após MySQL estar healthy

- [ ] **Step 4: Parar o AppHost**

```powershell
Ctrl+C
```

---

## Estrutura Final da Solution

```
Fiap-3ESPR-GS-C/
├── MissionClear.sln
├── MissionClear.AppHost/
│   ├── MissionClear.AppHost.csproj     ← net8.0, IsAspireHost=true
│   └── Program.cs                       ← MySQL container + api + web
├── MissionClear.ServiceDefaults/
│   ├── MissionClear.ServiceDefaults.csproj  ← net8.0
│   └── Extensions.cs                        ← AddServiceDefaults(), MapDefaultEndpoints()
├── MissionClear.Api/
│   ├── MissionClear.Api.csproj          ← net10.0, refs ServiceDefaults + Aspire.Pomelo
│   ├── Program.cs                        ← builder.AddServiceDefaults() + AddMySqlDbContext
│   ├── appsettings.json
│   ├── appsettings.Development.json      ← ConnectionStrings:missionclear para stand-alone
│   ├── Configuration/                    ← JwtSettings, OrbitalSettings, ExternalApiSettings, CorsSettings
│   ├── Helpers/                          ← OrbitalMath, RiskScoring, MissionScoring
│   ├── Middleware/                        ← GlobalExceptionMiddleware
│   └── (demais pastas existentes)
├── MissionClear.Web/
│   ├── MissionClear.Web.csproj           ← net10.0, refs ServiceDefaults
│   └── Program.cs                         ← stub MVC com AddServiceDefaults()
└── MissionClear.Tests/
    ├── MissionClear.Tests.csproj          ← net10.0, refs Api
    ├── Configuration/AppSettingsTests.cs
    └── Helpers/                            ← OrbitalMathTests, RiskScoringTests, MissionScoringTests
```

### Grafo de dependências

```
AppHost ──refs──► Api
AppHost ──refs──► Web
Api     ──refs──► ServiceDefaults
Web     ──refs──► ServiceDefaults
Tests   ──refs──► Api
```

`ServiceDefaults` não referencia nenhum projeto da solution (biblioteca pura).

### Portas e endpoints em desenvolvimento

| Recurso | URL |
|---------|-----|
| Aspire Dashboard | http://localhost:15021 |
| Api (via Aspire) | http://localhost:porta-aleatória (ver dashboard) |
| Web MVC (via Aspire) | http://localhost:porta-aleatória (ver dashboard) |
| MySQL (via Aspire) | localhost:porta-aleatória → nome: `missionclear` |

---

## Referência de Compatibilidade de Versões

| Projeto | TargetFramework | Motivo |
|---------|----------------|--------|
| AppHost | net8.0 | Aspire workload 9.x requer net8.0 no host |
| ServiceDefaults | net8.0 | Mesmo motivo; biblioteca compartilhada |
| Api | net10.0 | Runtime mais recente para lógica orbital |
| Web | net10.0 | Idem |
| Tests | net10.0 | Deve referenciar Api — mesmo TFM |

---

## Testing Strategy

- Testes de configuração (`AppSettingsTests`) validam binding de POCOs — rodam sem banco
- Testes de helpers (`OrbitalMath`, `RiskScoring`, `MissionScoring`) — funções puras, sem dependências externas
- Nenhum teste de integração nesta fase (banco ainda não tem schema — aguarda Fase 1)
- WebApplicationFactory usará `public partial class Program { }` já presente no `Program.cs` da Api

---

## Risks & Mitigations

| Risco | Mitigação |
|-------|-----------|
| Aspire workload não instalado | `dotnet workload install aspire` — pré-requisito verificado antes de iniciar |
| Docker Desktop não rodando | `docker ps` falha → iniciar Docker Desktop antes de rodar AppHost |
| `net10.0` não disponível na máquina | `dotnet --list-sdks` → instalar .NET 10 SDK de https://dot.net |
| Pacotes Aspire não encontrados | `dotnet nuget locals all --clear` + rebuild |
| `Projects.MissionClear_Web` não compilado | AppHost deve ter `<ProjectReference>` para Web; adicionar ao csproj antes do build |
| Jwt:Secret vazio em stand-alone | `appsettings.Development.json` tem secret de 50+ chars para dev |
| Connection string MySQL ausente em stand-alone | `appsettings.Development.json` tem `ConnectionStrings:missionclear` para dev sem Aspire |

---

## Success Criteria

- [ ] `dotnet sln MissionClear.sln list` mostra exatamente 5 projetos
- [ ] `dotnet build MissionClear.sln` — 0 warnings, 0 errors
- [ ] `dotnet test MissionClear.Tests/...` — 0 failures (todos os testes existentes passam)
- [ ] `MissionClear.AppHost/Program.cs` provisiona MySQL via `builder.AddMySql("mysql").AddDatabase("missionclear")`
- [ ] `MissionClear.ServiceDefaults/Extensions.cs` expõe `AddServiceDefaults()` e `MapDefaultEndpoints()`
- [ ] `MissionClear.Api/Program.cs` chama `builder.AddServiceDefaults()` e `builder.AddMySqlDbContext<AppDbContext>("missionclear")`
- [ ] `MissionClear.Api/Program.cs` contém `public partial class Program { }` no final
- [ ] `MissionClear.Web/Program.cs` chama `builder.AddServiceDefaults()` e `app.MapDefaultEndpoints()`
- [ ] Aspire dashboard acessível em `http://localhost:15021` ao rodar AppHost
- [ ] MySQL container sobe automaticamente ao rodar AppHost
- [ ] AppHost aguarda MySQL healthy antes de iniciar Api (`WaitFor(mysql)`)
- [ ] Api aguarda estar healthy antes de Web iniciar (`WaitFor(api)`)
- [ ] 7 commits atômicos com mensagens conventional-commit

---

## Relevant Files

- `MissionClear.sln`
- `MissionClear.AppHost/MissionClear.AppHost.csproj`
- `MissionClear.AppHost/Program.cs`
- `MissionClear.ServiceDefaults/MissionClear.ServiceDefaults.csproj`
- `MissionClear.ServiceDefaults/Extensions.cs`
- `MissionClear.Api/MissionClear.Api.csproj`
- `MissionClear.Api/Program.cs`
- `MissionClear.Api/appsettings.json`
- `MissionClear.Api/appsettings.Development.json`
- `MissionClear.Web/MissionClear.Web.csproj`
- `MissionClear.Web/Program.cs`
- `MissionClear.Tests/MissionClear.Tests.csproj`
