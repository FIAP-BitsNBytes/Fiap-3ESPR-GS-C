# Mission Clear — Motor Orbital para Missões LEO Seguras

> Projeto acadêmico — FIAP 3ESPR · Global Solution 2026

| Integrante                | RM        |
| ------------------------- | --------- |
| Gustavo Bezerra Assumção  | RM 553076 |
| Jó Sales                  | RM 552679 |
| Miguel Garcez de Carvalho | RM 553768 |
| Vinicius Souza e Silva    | RM 552781 |
| Edson Leonardo            | RM 553737 |

---

## O Problema

Existem mais de **27.000 objetos rastreados** em órbita terrestre — detritos de satélites, estágios de foguetes e fragmentos de colisões. Uma missão LEO mal planejada pode cruzar a trajetória de um detrito a 7,5 km/s. Sem análise prévia, o risco de colisão é invisível ao piloto.

**ODS cobertos:** 9 · Indústria e Infraestrutura — 11 · Cidades Sustentáveis — 13 · Ação Climática

---

## A Solução

**Mission Clear** ingere dados reais de detritos da CelesTrak, propaga suas órbitas via SGP4 e calcula janelas de lançamento seguras para o destino escolhido. O piloto vê quais horários têm menor risco de conjunção e simula a missão antes de lançar — com alertas em tempo real via SSE durante o voo simulado.

```
CelesTrak TLEs ──► SGP4 Propagation ──► Conjunction Detection ──► Safe Launch Windows
                                                                  ──► Live Mission Stream (SSE)
```

Este repositório é exclusivamente o **backend**: motor orbital + API REST.
O app mobile (React Native / Expo) está em `Fiap-3ESPR-GS-Mobile`.

---

## Stack Técnico

| Camada               | Tecnologia                              |
| -------------------- | --------------------------------------- |
| Runtime              | .NET 10.0                               |
| Orquestrador         | .NET Aspire 9.1                         |
| API                  | ASP.NET Core (controllers)              |
| Banco de dados       | MySQL 8 · Pomelo EF Core · Migrations   |
| Auth                 | JWT Bearer (1h) + Refresh Token (7d)    |
| Hash de senhas       | BCrypt.Net-Next                         |
| Propagação orbital   | SGP4 via NuGet (nunca reimplementado)   |
| Serialização JSON    | System.Text.Json · `snake_case`         |
| Testes               | xUnit · FluentAssertions · Moq          |
| Observabilidade      | OpenTelemetry · Aspire Dashboard        |

---

## Arquitetura

```
MissionClear.sln
├── MissionClear.AppHost/          .NET Aspire — orquestra Api + Web
├── MissionClear.ServiceDefaults/  OpenTelemetry, health checks compartilhados
├── MissionClear.Api/              Motor orbital + REST API  ← foco deste repo
│   ├── Entities/                  Modelos EF Core (nunca expostos diretamente)
│   ├── Data/Repositories/         Repository Pattern — IUserRepository, IMissionRepository
│   ├── Exceptions/                DomainException com 19 códigos canônicos
│   ├── Models/                    OrbitalObject, LaunchWindow, MissionSession, ...
│   ├── Dtos/                      Todos os DTOs do contrato da API
│   ├── Services/                  Lógica de negócio (Controllers não têm lógica)
│   ├── Controllers/               Roteamento e serialização apenas
│   ├── Middleware/                GlobalExceptionMiddleware → envelope { error, message, timestamp }
│   └── Program.cs                 DI completo com todos os serviços e middlewares
├── MissionClear.Web/              MVC — cookie auth, Razor views, papel Researcher/Administrator
└── MissionClear.Tests/            xUnit · cobertura ≥ 80% nos Services
```

**Regra de DI:**
- `IOrbitalEngineService` → `AddSingleton` (compartilha estado com `IOrbitalCache`)
- Repositories e demais services → `AddScoped`

---

## Como o Sistema Funciona

### 1. Ingestão de TLEs

Na inicialização, `TleIngestionService` faz HTTP GET na CelesTrak sem autenticação:

```
GET https://celestrak.org/NORAD/elements/gp.php?GROUP=debris&FORMAT=json
```

Os TLEs são carregados no `OrbitalCache` (singleton). Enquanto carrega, `GET /api/status` retorna `"status": "loading"`. Quando pronto: `"status": "ready"`.

### 2. Propagação Orbital (SGP4)

`OrbitalEngineService` propaga cada TLE para o instante atual usando a biblioteca NuGet SGP4. Resultado: latitude, longitude, altitude (km) e velocidade (km/s) para cada objeto rastreado.

### 3. Detecção de Conjunções

`ConjunctionDetector` analisa a trajetória de uma missão proposta e identifica detritos que passam a menos de 200 km da rota. Para cada aproximação calcula:
- `closest_approach_km` — distância mínima prevista
- `time_of_closest_approach` — instante de máxima aproximação
- `risk_level` — `low` / `medium` / `high` / `critical`

### 4. Janelas de Lançamento

`LaunchWindowService` divide o período solicitado em **slots de 15 minutos** e calcula `risk_score` para cada slot. Slots com `risk_score < 0.1` recebem `is_recommended: true`.

### 5. Simulação de Missão

| Modo | Endpoint | Comportamento |
|---|---|---|
| Estática | `POST /api/mission/simulate` | Resultado imediato, sem stream |
| Dinâmica | `POST /api/mission/session` + SSE | Sessão com stream em tempo real |

**Eventos SSE emitidos:**

| Evento | Frequência | Conteúdo |
|---|---|---|
| `heartbeat` | 15s | Mantém conexão viva |
| `debris_update` | 30s | Posições atualizadas dos debris próximos |
| `conjunction_alert` | Sob demanda | Debris entrou na zona de risco (<200km) |
| `session_complete` | 1x ao fim | Score final, risk_score, delta-v |

### 6. Autenticação JWT

- Access token: 1 hora · Refresh token: 7 dias
- Roles: `Researcher` (padrão em novos cadastros) e `Administrator`
- `GlobalExceptionMiddleware` captura toda `DomainException` e retorna envelope padrão

---

## API — Endpoints

### Orbital (público)

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/status` | Estado da API e contagem de TLEs |
| `GET` | `/api/destinations` | Destinos de missão disponíveis |
| `GET` | `/api/debris` | Detritos com posição atual propagada via SGP4 |
| `GET` | `/api/debris/stats` | Estatísticas por tipo e faixa de altitude |
| `GET` | `/api/debris/{id}` | Detalhe + TLE + órbita de um objeto (NORAD ID) |
| `GET` | `/api/launch-windows` | Janelas de lançamento em slots de 15 min |
| `GET` | `/api/launch-windows/best` | N melhores janelas por menor risk_score |

### Simulação (público)

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/mission/simulate` | Simulação estática — resultado imediato |
| `POST` | `/api/mission/session` | Cria sessão de simulação dinâmica |
| `GET` | `/api/mission/session/{id}/stream` | Stream SSE da simulação |
| `POST` | `/api/mission/session/{id}/complete` | Finaliza sessão; salva no histórico se autenticado |

### Autenticação

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/auth/register` | Cria conta — role padrão: `Researcher` |
| `POST` | `/api/auth/login` | Autentica · retorna access + refresh token |
| `POST` | `/api/auth/refresh` | Renova access token |
| `POST` | `/api/auth/logout` | Invalida refresh token |

### Usuário e Histórico (autenticado)

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/users/me` | Perfil e stats do usuário |
| `PUT` | `/api/users/me` | Atualiza display_name ou senha |
| `GET` | `/api/missions` | Histórico paginado com filtros |
| `GET` | `/api/missions/{id}` | Detalhe completo com score breakdown |
| `GET` | `/api/missions/stats` | Estatísticas agregadas |
| `DELETE` | `/api/missions/{id}` | Remove missão do histórico |

### Dashboard

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| `GET` | `/api/dashboard/summary` | Opcional | Visão orbital; enriquecida com dados do usuário se autenticado |
| `GET` | `/api/dashboard/alerts` | Público | Alertas de conjunção ativos nas próximas N horas |

Contrato completo com exemplos de request/response: [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md)

---

## Requisitos Atendidos

| Requisito | Implementação |
|---|---|
| API REST funcional | 23 endpoints conforme `docs/API_CONTRACT.md` |
| Dados reais de detritos | CelesTrak via `HttpClient` — sem mock hardcoded |
| SGP4 para propagação | Biblioteca NuGet — `OrbitalEngineService` |
| Auth JWT Bearer com roles | `AuthController` + `JwtService` + `[Authorize(Roles="...")]` |
| Banco relacional + migrations | MySQL 8 · Pomelo EF Core · `dotnet ef migrations` |
| Testes ≥ 80% nos Services | xUnit · `MissionClear.Tests` |
| Documentação da API | `docs/API_CONTRACT.md` v2.2 |
| Evidência de execução | Aspire Dashboard (`http://localhost:15021`) |

---

## Como Rodar

### Pré-requisitos

- .NET 10 SDK
- .NET Aspire workload: `dotnet workload install aspire`
- MySQL 8 local

### Configuração

Crie `MissionClear.Api/appsettings.Development.json` (não versionado — não commitar):

```json
{
  "ConnectionStrings": {
    "mysql": "Server=localhost;Database=missionclear;User=root;Password=SUA_SENHA;"
  },
  "Jwt": {
    "Secret": "chave_com_pelo_menos_32_caracteres_aqui",
    "Issuer": "MissionClear",
    "Audience": "MissionClear"
  }
}
```

### Executar via Aspire (recomendado)

```powershell
dotnet run --project MissionClear.AppHost
```

- **Aspire Dashboard:** `http://localhost:15021` — logs, traces, status de cada serviço
- **API:** `http://localhost:5000` (ou porta dinâmica exibida no dashboard)

### Executar só a API

```powershell
dotnet run --project MissionClear.Api
```

### Migrations

```powershell
dotnet ef migrations add InitialCreate --project MissionClear.Api
dotnet ef database update --project MissionClear.Api
```

### Testes

```powershell
dotnet test
```

---

## Destinos de Missão (MVP)

| ID | Destino | Altitude | Inclinação | Delta-v |
|---|---|---|---|---|
| `ISS` | Estação Espacial Internacional | 408 km | 51.6° | 9,40 km/s |
| `LEO_GENERIC` | Órbita LEO Genérica | 400 km | 28.5° | 9,20 km/s |
| `SSO` | Sun-Synchronous Orbit | 500 km | 97.4° | 10,10 km/s |

---

## Convenções

| Item | Formato |
|---|---|
| IDs | `{prefixo}_{Guid:N}` ex: `usr_a80ca0a15f2b4d3e8c91b2e7f3a4d5c6` |
| Timestamps | ISO 8601 UTC com `Z` |
| JSON | `snake_case` em todos os campos |
| Erros | Envelope único `{ error, message, timestamp }` |
| Commits | `feat \| fix \| refactor \| docs \| test \| chore: descrição` |
