# Mission Clear — Reboot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reestruturar o backend Mission Clear para usar .NET Aspire, MySQL, Repository Pattern, autorização por Roles e um novo projeto ASP.NET Core MVC.

**Architecture:**
- `MissionClear.AppHost` — orquestrador Aspire, provisiona container MySQL, conecta projetos via service discovery
- `MissionClear.ServiceDefaults` — OpenTelemetry, health checks e resilience compartilhados entre Api e Web
- `MissionClear.Api` — REST API com JWT Bearer (serve mobile), autorização por Role via Claims
- `MissionClear.Web` — ASP.NET Core MVC com Cookie+Claims, consome Api via HttpClient (zero acesso direto ao banco)
- `MissionClear.Tests` — xUnit, FluentAssertions, Moq

**Auth Strategy:**
- Mobile → API: JWT Bearer com claim `role` (Researcher | Administrator)
- Browser → Web MVC: Cookie auth — Claims populados a partir da resposta JWT da API
- MVC → API: HttpClient com Bearer token armazenado como Claim no Cookie

**Tech Stack:** .NET 10 (Api/Web), .NET 8 (AppHost/ServiceDefaults), ASP.NET Core, .NET Aspire 9, MySQL 8, Pomelo EF Core 8, BCrypt.Net-Next, xUnit, FluentAssertions, Moq

---

## Fase Index

| Fase | Arquivo | Prioridade |
|---|---|---|
| 0 — Aspire Solution Setup | [reboot/phase-00-aspire-solution.md](reboot/phase-00-aspire-solution.md) | BLOQUEANTE |
| 1 — Database + Repositories | [reboot/phase-01-database-repositories.md](reboot/phase-01-database-repositories.md) | BLOQUEANTE |
| 2 — Models + DTOs | [reboot/phase-02-models-dtos.md](reboot/phase-02-models-dtos.md) | Alta |
| 3 — Orbital Engine | [reboot/phase-03-orbital.md](reboot/phase-03-orbital.md) | Alta |
| 4 — Auth + Roles | [reboot/phase-04-auth-roles.md](reboot/phase-04-auth-roles.md) | Alta |
| 5 — Simulation | [reboot/phase-05-simulation.md](reboot/phase-05-simulation.md) | Média |
| 6 — History + Dashboard | [reboot/phase-06-history-dashboard.md](reboot/phase-06-history-dashboard.md) | Média |
| 7 — API Controllers | [reboot/phase-07-api-controllers.md](reboot/phase-07-api-controllers.md) | Alta |
| 8 — MVC Web Project | [reboot/phase-08-mvc-web.md](reboot/phase-08-mvc-web.md) | Alta |

## Ordem de Execução (estrita)

```
Fase 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8
```

Nenhuma fase pode iniciar antes que a anterior passe `dotnet build` + `dotnet test`.

## Estrutura da Solution (pós-reboot)

```
Fiap-3ESPR-GS-C/
├── MissionClear.AppHost/           ← NOVO
│   ├── MissionClear.AppHost.csproj
│   └── Program.cs
├── MissionClear.ServiceDefaults/   ← NOVO
│   ├── MissionClear.ServiceDefaults.csproj
│   └── Extensions.cs
├── MissionClear.Api/               ← MODIFICA
│   ├── MissionClear.Api.csproj
│   ├── Program.cs
│   ├── Configuration/
│   ├── Controllers/
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   └── Repositories/
│   │       ├── IUserRepository.cs
│   │       ├── IRefreshTokenRepository.cs
│   │       ├── IMissionRepository.cs
│   │       ├── UserRepository.cs
│   │       ├── RefreshTokenRepository.cs
│   │       └── MissionRepository.cs
│   ├── Entities/
│   ├── Exceptions/
│   ├── Helpers/
│   ├── Middleware/
│   ├── Models/
│   ├── Dtos/
│   └── Services/
├── MissionClear.Web/               ← NOVO
│   ├── MissionClear.Web.csproj
│   ├── Program.cs
│   ├── Controllers/
│   ├── Models/
│   ├── Services/
│   └── Views/
├── MissionClear.Tests/             ← MODIFICA
│   └── MissionClear.Tests.csproj
├── docs/
└── MissionClear.sln                ← MODIFICA
```

## O que Existe e NÃO deve ser deletado

| Arquivo | Ação |
|---|---|
| `MissionClear.Api/Program.cs` | Modificar (Aspire + MySQL) |
| `MissionClear.Api/Configuration/*.cs` | Manter todos |
| `MissionClear.Api/Helpers/OrbitalMath.cs` | Manter sem alteração |
| `MissionClear.Api/Helpers/RiskScoring.cs` | Manter sem alteração |
| `MissionClear.Api/Helpers/MissionScoring.cs` | Manter sem alteração |
| `MissionClear.Api/Middleware/GlobalExceptionMiddleware.cs` | Manter sem alteração |
| `MissionClear.Api/Data/AppDbContext.cs` | Modificar (MySQL + Role) |
| `MissionClear.Api/Models/RiskLevel.cs` | Manter sem alteração |
| `MissionClear.Tests/Configuration/AppSettingsTests.cs` | Manter |
| `MissionClear.Tests/Helpers/*.cs` | Manter todos |

## Pré-requisitos Bloqueantes

Antes de iniciar a Fase 0, verificar:

```powershell
# 1. Docker Desktop rodando (Aspire precisa para MySQL container)
docker ps

# 2. .NET Aspire workload instalado
dotnet workload install aspire

# 3. Verificar instalação
dotnet workload list
# deve mostrar: aspire

# 4. MySQL Workbench instalado (para inspecionar o schema)
# Download: https://dev.mysql.com/downloads/workbench/
```

## Regras de Negócio Imutáveis (não alterar)

- Contrato de API: `docs/API_CONTRACT.md` — nenhuma rota ou schema muda
- SGP4: usar stub determinístico (nenhum NuGet de SGP4 disponível/necessário)
- Fórmulas: risk_score, mission_score em `Helpers/` — não modificar
- LEO focus: altitude 200–2000 km apenas
- CelesTrak primary, KeepTrack opcional (timeout 5s, nunca derruba o sistema)

## Tabela de Decisões Arquiteturais

| Decisão | Escolha | Motivo |
|---|---|---|
| MySQL driver | Pomelo.EntityFrameworkCore.MySql 8.0.2 | Melhor suporte EF Core, produção-ready |
| Aspire MySQL | `builder.AddMySql("mysql").AddDatabase("missionclear")` | Provisiona container dev automaticamente |
| Auth mobile | JWT Bearer (mantém) | Mobile não suporta cookies facilmente |
| Auth web MVC | Cookie + Claims | Padrão ASP.NET Core MVC |
| JWT → Cookie bridge | MVC chama POST /api/auth/login, recebe JWT, extrai role, cria Cookie com Claims | Web nunca acessa DB diretamente |
| Role padrão | "Researcher" | Registro público cria Researcher |
| Admin criação | Via seed ou migration manual | Sem endpoint público de admin creation |
| Repository scope | Scoped (um por request) | EF Core DbContext é Scoped |
| OrbitalCache scope | Singleton (cache compartilhado entre requests) | Thread-safe, dados orbitais estáveis |

---

## Checklist Final (pós todas as fases)

- [ ] `dotnet build` sem warnings em todos os 5 projetos
- [ ] `dotnet test` ≥ 80% coverage em Services/
- [ ] Aspire dashboard abre em `http://localhost:15021`
- [ ] MySQL container sobe automaticamente via Aspire
- [ ] `GET /api/status` retorna `"status": "ready"` após ~60s de boot
- [ ] `POST /api/auth/register` cria usuário com role "Researcher"
- [ ] `DELETE /api/missions/{id}` retorna 403 para role "Researcher"
- [ ] MVC login redireciona para dashboard com claims corretos
- [ ] MVC `/users` retorna 403 para Researcher
- [ ] Nenhuma senha em texto limpo no banco MySQL
