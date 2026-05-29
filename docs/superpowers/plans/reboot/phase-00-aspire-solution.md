# Phase 00 — Aspire Solution Setup

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans

**Goal:** Criar os projetos AppHost, ServiceDefaults e Web; montar a solution; conectar todos os projetos via Aspire.

**Architecture:** Aspire AppHost orquestra Api + Web + MySQL container. ServiceDefaults injeta OpenTelemetry em todos.

**Tech Stack:** .NET Aspire 9.1, net8.0 (AppHost/ServiceDefaults), net10.0 (Api/Web)

---

### Task 1: Instalar Aspire Workload + Criar Solution

**Files:**
- Create: `MissionClear.sln`

- [ ] **Step 1: Instalar Aspire workload**

```powershell
dotnet workload install aspire
dotnet workload list
# deve aparecer: aspire
```

- [ ] **Step 2: Criar solution no diretório raiz**

```powershell
# Na raiz do projeto (Fiap-3ESPR-GS-C/)
dotnet new sln -n MissionClear
```

- [ ] **Step 3: Adicionar projeto Api existente à solution**

```powershell
dotnet sln MissionClear.sln add MissionClear.Api/MissionClear.Api.csproj
dotnet sln MissionClear.sln add MissionClear.Tests/MissionClear.Tests.csproj
```

- [ ] **Step 4: Verificar**

```powershell
dotnet sln MissionClear.sln list
# deve mostrar: MissionClear.Api e MissionClear.Tests
```

---

### Task 2: Criar MissionClear.AppHost

**Files:**
- Create: `MissionClear.AppHost/MissionClear.AppHost.csproj`
- Create: `MissionClear.AppHost/Program.cs`

- [ ] **Step 1: Criar projeto AppHost**

```powershell
dotnet new aspire-apphost -n MissionClear.AppHost -o MissionClear.AppHost
```

- [ ] **Step 2: Adicionar à solution**

```powershell
dotnet sln MissionClear.sln add MissionClear.AppHost/MissionClear.AppHost.csproj
```

- [ ] **Step 3: Atualizar csproj para referenciar Api e Web**

Abrir `MissionClear.AppHost/MissionClear.AppHost.csproj` e substituir conteúdo:

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

- [ ] **Step 4: Escrever Program.cs do AppHost**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

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

---

### Task 3: Criar MissionClear.ServiceDefaults

**Files:**
- Create: `MissionClear.ServiceDefaults/MissionClear.ServiceDefaults.csproj`
- Create: `MissionClear.ServiceDefaults/Extensions.cs`

- [ ] **Step 1: Criar projeto ServiceDefaults**

```powershell
dotnet new aspire-servicedefaults -n MissionClear.ServiceDefaults -o MissionClear.ServiceDefaults
```

- [ ] **Step 2: Adicionar à solution**

```powershell
dotnet sln MissionClear.sln add MissionClear.ServiceDefaults/MissionClear.ServiceDefaults.csproj
```

- [ ] **Step 3: Atualizar csproj**

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

- [ ] **Step 4: Escrever Extensions.cs**

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

---

### Task 4: Criar MissionClear.Web

**Files:**
- Create: `MissionClear.Web/MissionClear.Web.csproj`
- Create: `MissionClear.Web/Program.cs` (stub mínimo)

- [ ] **Step 1: Criar projeto MVC**

```powershell
dotnet new mvc -n MissionClear.Web -o MissionClear.Web
```

- [ ] **Step 2: Adicionar à solution**

```powershell
dotnet sln MissionClear.sln add MissionClear.Web/MissionClear.Web.csproj
```

- [ ] **Step 3: Atualizar csproj**

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

- [ ] **Step 4: Program.cs stub (será completado na Fase 8)**

```csharp
using MissionClear.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapDefaultControllerRoute();
app.MapDefaultEndpoints();

app.Run();
```

---

### Task 5: Atualizar MissionClear.Api.csproj para Aspire + MySQL

**Files:**
- Modify: `MissionClear.Api/MissionClear.Api.csproj`

- [ ] **Step 1: Atualizar csproj**

Substituir conteúdo completo:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
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

Nota: `Aspire.Pomelo.EntityFrameworkCore.MySql` habilita `builder.AddMySqlDbContext<AppDbContext>("missionclear")` que lê a connection string injetada pelo AppHost automaticamente.

---

### Task 6: Atualizar MissionClear.Tests.csproj

**Files:**
- Modify: `MissionClear.Tests/MissionClear.Tests.csproj`

- [ ] **Step 1: Verificar csproj atual e garantir referência ao Api**

O arquivo deve ter:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.10" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.10" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
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

---

### Task 7: Verificar build inicial

- [ ] **Step 1: Build de todos os projetos**

```powershell
dotnet build MissionClear.sln
```

Resultado esperado: `Build succeeded. 0 Warning(s). 0 Error(s).`

Se falhar com erro de `net10.0` incompatível com pacotes, verificar se os pacotes têm suporte a net10. Se necessário, atualizar versões ou usar `<TargetFramework>net8.0</TargetFramework>` temporariamente.

- [ ] **Step 2: Testes existentes ainda passam**

```powershell
dotnet test MissionClear.Tests/MissionClear.Tests.csproj --no-build
```

Resultado esperado: todos os testes de Configuration e Helpers passam.

- [ ] **Step 3: Commit**

```powershell
git add MissionClear.sln MissionClear.AppHost/ MissionClear.ServiceDefaults/ MissionClear.Web/ MissionClear.Api/MissionClear.Api.csproj MissionClear.Tests/MissionClear.Tests.csproj
git commit -m "feat: add Aspire AppHost, ServiceDefaults and Web MVC skeleton"
```
