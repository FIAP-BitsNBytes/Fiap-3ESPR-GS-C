# CLAUDE.md — Mission Clear Backend (C#)

> **Leia isto primeiro em toda sessão nova antes de tocar qualquer arquivo.**
> Fonte de verdade: este arquivo + `docs/API_CONTRACT.md` + `docs/superpowers/plans/`.

Projeto acadêmico FIAP (3ESPR Global Solution). Backend C# / ASP.NET Core.
Mobile em repo separado: `Fiap-3ESPR-GS-Mobile`.
**Prazo:** 1 semana a partir de 2026-05-28 (MVP funcional e entregável).

---

## Contexto do Sistema

**Mission Clear** ingere dados reais de detritos espaciais (CelesTrak + KeepTrack), propaga órbitas via SGP4 e calcula janelas de lançamento seguras para missões LEO (200–2.000 km). Este repo é exclusivamente o motor orbital + API REST.

**ODS cobertos:** 9 (Indústria e Infraestrutura), 11 (Cidades sustentáveis), 13 (Ação climática) — detritos espaciais = infraestrutura sustentável.

---

## Stack

| Categoria | Tecnologia | Versão |
|---|---|---|
| Runtime | .NET | 10.0 |
| Orquestrador | .NET Aspire | 9.1.0 |
| Framework web | ASP.NET Core (controllers) | — |
| Banco de dados | MySQL via Pomelo EF Core | 8.0.2 |
| Aspire integration | Aspire.Pomelo.EntityFrameworkCore.MySql | 9.1.0 |
| Auth | JWT Bearer | 8.0.10 |
| Passwords | BCrypt.Net-Next | 4.0.3 |
| SGP4 | NuGet existente — **nunca implementar do zero** | — |
| HTTP client | HttpClient (IHttpClientFactory) | — |
| Serialização | System.Text.Json + SnakeCaseLower | — |
| Testes | xUnit + FluentAssertions + Moq | — |

---

## Estrutura da Solution (5 projetos)

```
MissionClear.sln
├── MissionClear.AppHost/        net8.0 — Aspire: api + web orchestration
├── MissionClear.ServiceDefaults/ net8.0 — OpenTelemetry, health checks
├── MissionClear.Api/            net10.0 — Motor orbital + REST API
├── MissionClear.Web/            net10.0 — MVC web (cookie auth, Researcher/Administrator)
└── MissionClear.Tests/          net10.0 — xUnit (referencia Api)
```

**Referências:**
- AppHost → Api, Web | Api → ServiceDefaults | Web → ServiceDefaults | Tests → Api

**Arquivo de arquitetura completo:** `docs/superpowers/plans/architecture-overview.md`

---

## Planos de Implementação

Todos os planos estão em `docs/superpowers/plans/`. Leia o guia de execução paralela antes de começar:

| Arquivo | Conteúdo |
|---|---|
| `plan-00-scaffolding.md` | Solution, AppHost, ServiceDefaults, Web, Tests |
| `plan-01-database.md` | MySQL, EF Core, Migrations, Repository Pattern |
| `plan-02-models.md` | DomainException, Models, todos os DTOs |
| `plan-03-orbital.md` | SGP4, OrbitalCache, DataAggregator, TleIngestion |
| `plan-04-auth.md` | JWT, BCrypt, AuthService, UserService |
| `plan-05-simulation.md` | ConjunctionDetector, LaunchWindow, Session, SSE |
| `plan-06-history-dashboard.md` | MissionHistory, Dashboard, stats |
| `plan-07-controllers.md` | 9 controllers, GlobalExceptionMiddleware, Program.cs FINAL |
| `plan-08-mvc-web.md` | MVC Web: Cookie auth, ApiClient, Razor views |
| `parallel-execution-guide.md` | Waves de execução (7h paralelo vs 9h sequencial) |
| `architecture-overview.md` | Mapa completo: arquivos, serviços, testes |

**Regra crítica:** `Program.cs` da Api é escrito SOMENTE pelo plan-07. Todos os outros planos pulam esse passo.

---

## Requisitos Obrigatórios do Professor (FIAP)

Estes itens são avaliados diretamente — não omitir, não simplificar:

### Backend (este repo)
- [ ] API REST funcional com os endpoints do contrato (`docs/API_CONTRACT.md`)
- [ ] Dados reais de detritos espaciais (CelesTrak via HTTP — não mock hardcoded)
- [ ] SGP4 para propagação orbital (biblioteca NuGet, nunca reimplementar)
- [ ] Autenticação JWT Bearer com roles (Researcher / Administrator)
- [ ] Banco de dados relacional com migrations (MySQL + EF Core)
- [ ] Testes unitários: mínimo 80% de cobertura nos Services
- [ ] Documentação da API (contrato em `docs/API_CONTRACT.md`)
- [ ] Deploy ou evidência de execução (Aspire dashboard rodando)

### Mobile (repo separado)
- [ ] React Native / Expo
- [ ] **Context API** visível no código (professor avalia por nome — não só Zustand)
- [ ] **AsyncStorage** visível para favoritos/tema (mesma razão)
- [ ] FlatList em todas as listas (não ScrollView + map)
- [ ] Integração real com este backend via HTTP + SSE
- [ ] Gráficos: altitude bars (Home) + score bars (resultado de missão)

---

## Fontes de Dados Externas

### CelesTrak (fonte principal)
- `https://celestrak.org/NORAD/elements/gp.php?GROUP=debris&FORMAT=json`
- Sem auth, HTTP GET direto, formato JSON com TLE

### KeepTrack (fonte secundária)
- Base: `https://keeptrack.space/api`
- API key via `IConfiguration` — **nunca hardcoded**
- Rate limit free: 60 req/hr | Com key: 2.000 req/hr

**Regra:** Mobile nunca fala com CelesTrak ou KeepTrack diretamente. Sempre via esta API.

---

## Contrato de Dados

O contrato completo está em `docs/API_CONTRACT.md` (v2.2 — fonte de verdade absoluta).
Ambos os projetos (backend + mobile) seguem este documento **exatamente**.
Qualquer alteração de campo, rota ou schema → atualizar o contrato primeiro.

**Rotas principais:**
- `GET /api/debris` — detritos com posição propagada (público, Cache-Control: 60s)
- `GET /api/launch-windows` — janelas de lançamento por destino e período
- `POST /api/mission/simulate` — simulação estática
- `POST /api/mission/session` + SSE stream — simulação dinâmica ao vivo
- `GET /api/missions` — histórico do usuário (autenticado)
- `GET /api/dashboard/summary` — visão orbital (público/enriquecido com auth)

---

## Arquitetura Interna (módulos)

```
MissionClear.Api/
├── Entities/         → EF Core. Nunca expostas na API diretamente.
├── Data/
│   ├── AppDbContext  → MySQL provider, índices, cascade deletes
│   └── Repositories/ → IUserRepository, IRefreshTokenRepository, IMissionRepository
├── Exceptions/       → DomainException (ErrorCode + HttpStatus) — 19 códigos canônicos
├── Models/           → OrbitalObject, MissionDestination, ConjunctionResult, LaunchWindow, MissionSession
├── Dtos/             → Todos os DTOs do contrato (Auth, User, Orbital, Mission, History, Dashboard...)
├── Services/         → Toda lógica de negócio. Controllers não têm lógica.
│   └── Interfaces/   → Contratos: IAuthService, IOrbitalEngineService, IDashboardService...
├── Controllers/      → Apenas roteamento e serialização. Zero lógica de negócio.
├── Middleware/        → GlobalExceptionMiddleware → captura DomainException → ApiErrorDto
└── Program.cs        → DI completo (escrito somente pelo plan-07)
```

**Regras de DI:**
- `IOrbitalEngineService` → `AddSingleton` (compartilha estado com `IOrbitalCache`)
- Repositories e demais services → `AddScoped`

---

## Não-Negociáveis

| Anti-padrão | Regra |
|---|---|
| `catch {}` vazio | Sempre re-throw ou `ILogger.LogError` + re-throw |
| Secret/API key hardcoded | Sempre `IConfiguration.GetValue` |
| `Console.WriteLine` em produção | `ILogger<T>` estruturado |
| Arquivo > 300 linhas | Extrai responsabilidade |
| Lógica de negócio no Controller | Move para Service |
| Reimplementar SGP4 do zero | Usar biblioteca NuGet existente |
| Resposta sem tipo definido | Todos os endpoints têm DTO de retorno tipado |
| `new BadRequest(...)` direto no controller | Usar `throw new DomainException(...)` |

---

## Modo de Testes — "Test, Break, Fix"

**Filosofia:** failure cases PRIMEIRO, happy path por último.

```
RED → GREEN → REFACTOR
```

1. Escrever o teste que quebra
2. Rodar — confirmar RED
3. Implementar mínimo para passar
4. Rodar — confirmar GREEN
5. Refatorar (sem quebrar os testes)

**Cobertura mínima:** 80% nos Services.

**Rodar por wave (não tudo de uma vez):**
```powershell
dotnet test --filter "Repository|Config"                           # Wave 2
dotnet test --filter "Orbital|Auth|Jwt"                            # Wave 3
dotnet test --filter "Conjunction|LaunchWindow|Session|Simulation" # Wave 4
dotnet test --filter "History|Dashboard"                           # Wave 5
dotnet test                                                        # Wave 6 — todos
```

**Regra:** Bug encontrado nos testes → corrigir o código, nunca a asserção.

**InternalsVisibleTo:** `MissionClear.Api/Properties/AssemblyInfo.cs` expõe `internal` para `MissionClear.Tests` (necessário para white-box tests do DataAggregatorService).

---

## Segurança (checklist antes de commit)

- [ ] Nenhuma API key ou connection string hardcoded
- [ ] KeepTrack API key lida de `IConfiguration` / env var
- [ ] Todos os inputs de query string validados
- [ ] Mensagens de erro não expõem stack trace em produção
- [ ] CORS configurado (em dev: `*`)
- [ ] `appsettings.Development.json` no `.gitignore` (nunca commitar credenciais)
- [ ] `Jwt:Secret` mínimo 32 chars — startup guard ativo em Program.cs

---

## Como Guss Gosta de Trabalhar

- **IA-First:** IA entrega implementação completa (código + testes). Gustavo revisa e aprova. Não co-escreve linha a linha.
- **Resultado primeiro:** código funcionando, nunca esqueletos com `// TODO`.
- **Português nas respostas**, inglês nos identificadores de código.
- **Sem preâmbulos:** nada de "Vou agora...", "Claro!", "Com prazer!".
- **Comunicação informal** (mas código é formal e correto).
- **Documentação como contrato:** docs são input para IAs futuras, não notas de rodapé.
- **Rastreabilidade total:** banco, logs, interceptors — tudo rastreável.
- **Segurança não é opcional:** XSS, IDOR, injection — tratados desde o design.

---

## Convenções de IDs e Timestamps

| Entidade | Formato | Exemplo |
|---|---|---|
| Usuário | `usr_{Guid:N}` | `usr_a80ca0a15f2b4d3e8c91b2e7f3a4d5c6` |
| Missão | `msn_{Guid:N}` | `msn_b1c2d3e4f5a678901234567890abcdef` |
| Sessão | `sess_{Guid:N}` | `sess_c3d4e5f6a7b890123456789012345678` |
| Timestamps | `DateTime.UtcNow.ToString("O")` | ISO 8601 UTC com Z |
| JSON naming | `JsonNamingPolicy.SnakeCaseLower` | via `AddJsonOptions` em Program.cs |
| Role padrão | `"Researcher"` | hardcoded em `AuthService.RegisterAsync` |

---

## Commits

```
<tipo>: <descrição>
```
Tipos: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`

---

## Bootstrap de Sessão

Em toda sessão nova:
1. `fenix: intelligence → memory_search, query="Mission Clear"` — time: **FIAP**

Após o bootstrap:
- Leia `docs/superpowers/plans/architecture-overview.md` para entender o estado atual
- Leia `docs/API_CONTRACT.md` se for tocar em endpoints ou DTOs
- Verifique qual wave está em execução antes de editar arquivos
