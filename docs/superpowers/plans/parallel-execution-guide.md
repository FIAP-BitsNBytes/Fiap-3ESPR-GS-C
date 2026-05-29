# Guia de Execução Paralela — Mission Clear Backend

**Para:** Múltiplos agentes ou desenvolvedores executando planos simultaneamente.
**Pré-requisito:** Todos os planos `plan-00` a `plan-08` estão prontos e revisados.

---

## Por que não dá pra paralelizar tudo

Os planos dependem uns dos outros em cadeia. Um plano que cria uma interface
(`IUserRepository`, por exemplo) precisa existir antes que outro plano consuma essa
interface. Se dois agentes tentarem criar arquivos que o outro espera, um deles vai falhar
em `dotnet build`.

---

## Grafo de dependências

```
plan-00 (scaffolding)
    │
    ├──► plan-01 (database + repositories)
    │         │
    │         └──────────────────────┐
    │                                │
    ├──► plan-02 (models + DTOs)     │
    │         │                      │
    │         ├──► plan-03 (orbital) │
    │         │         │            │
    │         └──► plan-04 (auth) ───┤
    │                   │            │
    │                   └────────────┴──► plan-05 (simulation)
    │                                          │
    │                                          └──► plan-06 (history + dashboard)
    │                                                    │
    │                                                    └──► plan-07 (controllers)
    │                                                               │
    │                                                               └──► plan-08 (MVC web)
```

---

## Waves de execução

### Wave 1 — Fundação (sequencial, obrigatório)

> **1 terminal / 1 agente. Bloqueia todo o resto.**

| Plan | O que cria |
|------|-----------|
| `plan-00` | Solução .sln, 5 projetos, AppHost, ServiceDefaults, csproj base, Tests.csproj |

**Critério de saída:** `dotnet build MissionClear.sln` verde.

---

### Wave 2 — Dados e Contratos (paralelo após Wave 1)

> **2 terminais / 2 agentes simultâneos. Sem conflito de arquivos.**

| Agente | Plan | Diretórios exclusivos |
|--------|------|----------------------|
| Agente A | `plan-01` | `MissionClear.Api/Data/` (entities, AppDbContext, repositories, migrations) |
| Agente B | `plan-02` | `MissionClear.Api/Exceptions/`, `MissionClear.Api/Models/`, `MissionClear.Api/Dtos/` |

**⚠️ REGRA:** Nenhum dos dois deve editar `Program.cs` nem `appsettings.Development.json` nesta wave. Plan-07 sobrescreve o Program.cs final. Plans intermediários que mencionam editar Program.cs devem **pular** esse passo.

**Critério de saída:** Ambos com `dotnet build MissionClear.Api/MissionClear.Api.csproj` verde + testes passando.

---

### Wave 3 — Serviços de Domínio (paralelo após Wave 2)

> **2 terminais / 2 agentes simultâneos. Sem conflito de arquivos.**

| Agente | Plan | Diretórios exclusivos |
|--------|------|----------------------|
| Agente C | `plan-03` | `MissionClear.Api/Services/` → `OrbitalCache`, `OrbitalEngineService`, `DataAggregatorService`, `TleIngestionService` + interfaces em `Services/Interfaces/` |
| Agente D | `plan-04` | `MissionClear.Api/Services/` → `JwtService`, `AuthService`, `UserService` + interfaces em `Services/Interfaces/` |

**⚠️ REGRA:** Ambos criam arquivos em `Services/` mas em arquivos **distintos** — sem sobreposição. Confirmar antes de iniciar:

```
plan-03 cria:           plan-04 cria:
IOrbitalCache.cs        IJwtService.cs
IOrbitalEngineService.cs  IAuthService.cs
IDataAggregatorService.cs IUserService.cs
OrbitalCache.cs         JwtService.cs
OrbitalEngineService.cs AuthService.cs
DataAggregatorService.cs UserService.cs
TleIngestionService.cs  (+ testes de auth)
(+ testes orbitais)
```

**⚠️ REGRA:** Nenhum dos dois edita `Program.cs`, `appsettings.Development.json`, nem `MissionClear.Api.csproj`. Pular esses passos dos planos — plan-07 cuida de tudo.

**Critério de saída:** Ambos com build verde + testes passando.

---

### Wave 4 — Simulação (sequencial após Wave 3)

> **1 terminal / 1 agente. Depende de plan-03 + plan-04.**

| Plan | O que cria |
|------|-----------|
| `plan-05` | `ConjunctionDetector`, `LaunchWindowCalculator`, `SessionStore`, `MissionSimulationService`, `MissionSseService` + interfaces |

**Critério de saída:** `dotnet build` verde + testes de simulação passando.

---

### Wave 5 — Histórico e Dashboard (sequencial após Wave 4)

> **1 terminal / 1 agente. Depende de plan-05.**

| Plan | O que cria |
|------|-----------|
| `plan-06` | `MissionHistoryService`, `DashboardService` + interfaces |

**Critério de saída:** `dotnet build` verde + testes de history/dashboard passando.

---

### Wave 6 — Controllers e Program.cs Final (sequencial após Wave 5)

> **1 terminal / 1 agente. Depende de plan-00 a plan-06.**

| Plan | O que cria |
|------|-----------|
| `plan-07` | 9 controllers, `GlobalExceptionMiddleware`, **Program.cs final completo**, testes de integração |

> Este é o único plano que **deve** escrever o `Program.cs` com DI completo.
> Qualquer DI parcial escrita nas waves anteriores será substituída aqui.

**Critério de saída:** `dotnet test MissionClear.Tests/` verde com 80%+ cobertura.

---

### Wave 7 — MVC Web (sequencial após Wave 6)

> **1 terminal / 1 agente. Depende de plan-07 (API rodando via Aspire).**

| Plan | O que cria |
|------|-----------|
| `plan-08` | `MissionClear.Web`: Program.cs, ApiClient, controllers MVC, ViewModels, Razor views |

**Critério de saída:** `dotnet build MissionClear.sln` verde. Aspire Dashboard mostra Api + Web "Running".

---

## Cronograma estimado

```
Tempo  │ Terminal 1         │ Terminal 2
───────┼────────────────────┼────────────────────
0:00   │ plan-00 (30 min)   │ —
0:30   │ plan-01 (60 min)   │ plan-02 (60 min)
1:30   │ plan-03 (60 min)   │ plan-04 (60 min)
2:30   │ plan-05 (60 min)   │ —
3:30   │ plan-06 (60 min)   │ —
4:30   │ plan-07 (90 min)   │ —
6:00   │ plan-08 (60 min)   │ —
7:00   │ DONE               │
```

**Total com paralelismo:** ~7 horas vs ~9 horas sequencial.

---

## Regras de ouro para todos os agentes

1. **Nunca editar `Program.cs`** exceto em plan-07. Pular qualquer passo que mencione isso em outros planos.
2. **Nunca editar `appsettings.Development.json`** exceto em plan-01 (que configura ConnectionStrings e JWT settings).
3. **Nunca editar `.csproj`** exceto em plan-00 (que configura pacotes base). Planos que mencionam adicionar pacotes devem verificar se já estão no csproj antes de rodar `dotnet add package`.
4. **Commit por plan:** cada agente commita ao final do seu plan antes de passar para o próximo.
5. **Build antes de avançar:** `dotnet build MissionClear.sln` deve passar antes de iniciar a próxima wave.
6. **Testes devem passar:** `dotnet test` verde antes de liberar a próxima wave.

---

## Checklist de gates entre waves

```
[ ] Wave 1 completa: dotnet build MissionClear.sln → verde
[ ] Wave 2 completa: dotnet test --filter "Repository|Config" → verde
[ ] Wave 3 completa: dotnet test --filter "Orbital|Auth|Jwt" → verde
[ ] Wave 4 completa: dotnet test --filter "Conjunction|LaunchWindow|Session|Simulation" → verde
[ ] Wave 5 completa: dotnet test --filter "History|Dashboard" → verde
[ ] Wave 6 completa: dotnet test (todos) → verde, cobertura ≥ 80%
[ ] Wave 7 completa: dotnet run --project MissionClear.AppHost → Aspire Dashboard verde
```

---

## Arquivos compartilhados — nunca editar em paralelo

| Arquivo | Dono exclusivo |
|---------|---------------|
| `MissionClear.Api/Program.cs` | plan-07 (Wave 6) |
| `MissionClear.Api/appsettings.Development.json` | plan-01 (Wave 2, Agente A) |
| `MissionClear.Api/MissionClear.Api.csproj` | plan-00 (Wave 1) |
| `MissionClear.AppHost/Program.cs` | plan-00 + plan-08 (sequenciais) |
| `MissionClear.Tests/MissionClear.Tests.csproj` | plan-00 (Wave 1) |
| `MissionClear.sln` | plan-00 (Wave 1) |
