# Mission Clear — API Contract v2.0

> **Fonte da verdade.** Backend e Mobile devem seguir este documento exatamente.
> Qualquer alteração de campo, rota ou schema deve ser atualizada aqui primeiro.

**Base URL (dev local):** `http://localhost:5000`
> **Aspire:** quando rodando via `MissionClear.AppHost`, a porta da API é atribuída dinamicamente. Para fixar em dev, adicionar `"applicationUrl": "http://localhost:5000"` no `MissionClear.Api/Properties/launchSettings.json` perfil `http`. Sem isso, consultar o Aspire Dashboard (`http://localhost:15021`) para descobrir a porta. O Mobile deve usar `.env` `API_URL` configurado para a URL correta.

**Base URL (produção):** a definir
**Protocolo:** HTTP/1.1 + SSE para streaming
**Formato:** JSON (`Content-Type: application/json`)
**Encoding:** UTF-8
**Timestamps:** ISO 8601 UTC — `2025-05-27T14:32:00Z`
**Auth:** JWT Bearer — `Authorization: Bearer <access_token>`

---

## Índice

1. [Convenções Gerais](#1-convenções-gerais)
2. [Autenticação](#2-autenticação)
3. [Envelope de Erro](#3-envelope-de-erro)
4. [Destinos Válidos](#4-destinos-válidos)
5. [Rotas — Auth](#5-rotas--auth)
6. [Rotas — Usuário](#6-rotas--usuário) (inclui favoritos)
7. [Rotas — Orbital (Público)](#7-rotas--orbital-público)
8. [Rotas — Janelas de Lançamento](#8-rotas--janelas-de-lançamento)
9. [Rotas — Simulação de Missão](#9-rotas--simulação-de-missão)
10. [Rotas — Histórico de Missões](#10-rotas--histórico-de-missões)
11. [Rotas — Dashboard](#11-rotas--dashboard)
12. [Rotas — Sistema](#12-rotas--sistema)
13. [Rotas — Admin](#13-rotas--admin)
14. [SSE — Protocolo de Streaming](#14-sse--protocolo-de-streaming)
15. [Códigos de Erro](#15-códigos-de-erro)
16. [Referência de Campos](#16-referência-de-campos)
17. [Mocks para Mobile](#17-mocks-para-mobile)

---

## 1. Convenções Gerais

### Nomes de campo
- Todos os campos em `snake_case`
- Sem abreviações ambíguas: `altitude_km` não `alt`, `velocity_km_s` não `vel`
- Booleanos com prefixo `is_` ou `has_`

### Formato de IDs

IDs gerados pelo backend usam `{prefixo}_{Guid:N}` — prefixo + 32 caracteres hex minúsculos:

| Entidade | Prefixo | Exemplo |
|---|---|---|
| Usuário | `usr_` | `usr_a80ca0a15f2b4d3e8c91b2e7f3a4d5c6` |
| Missão | `msn_` | `msn_b1c2d3e4f5a678901234567890abcdef` |
| Sessão | `sess_` | `sess_c3d4e5f6a7b890123456789012345678` |
| Alerta | `alrt_` | `alrt_d4e5f6a7b8c9012345678901234567ab` |

> Os exemplos neste documento usam IDs encurtados por legibilidade. O formato real sempre tem 32 hex chars após o prefixo.

### Números
| Dado | Casas decimais | Exemplo |
|---|---|---|
| Coordenadas | 4 | `-23.5412` |
| Altitude / distância (km) | 2 | `408.50` |
| Velocidade (km/s) | 3 | `7.660` |
| risk_score | 4 | `0.0312` |
| delta_v_km_s | 2 | `9.40` |
| mission_score | inteiro | `87` |

### Timestamps
- Sempre UTC, sempre com `Z`
- Formato: `YYYY-MM-DDTHH:mm:ssZ`

### Paginação
- Parâmetros: `page` (default `1`) e `limit` (default e máximo por endpoint)
- Resposta inclui `pagination` object em toda lista paginada:
```json
{
  "data": [...],
  "pagination": {
    "page": 1,
    "limit": 20,
    "total": 143,
    "total_pages": 8
  }
}
```

### Autenticação por rota
- 🔓 **Público** — sem token
- 🔑 **Autenticado** — requer `Authorization: Bearer <token>`
- 🔑? **Opcional** — funciona sem token, mas retorna dados extras com token

---

## 2. Autenticação

Tokens JWT. Fluxo padrão com access + refresh token.

| Token | TTL | Uso |
|---|---|---|
| `access_token` | 1 hora | Enviado no header `Authorization: Bearer` em toda request autenticada |
| `refresh_token` | 7 dias | Enviado no body para renovar o `access_token` |

**Header padrão para rotas autenticadas:**
```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

**Quando o access_token expirar:** API retorna `401` com `error: "TOKEN_EXPIRED"`. Mobile deve chamar `POST /api/auth/refresh` e repetir a request.

---

## 3. Envelope de Erro

Todos os erros retornam o mesmo shape.

**HTTP Status:** 400, 401, 403, 404, 409, 503, 500

```json
{
  "error": "ERROR_CODE",
  "message": "Descrição para o desenvolvedor — não exibir ao usuário.",
  "timestamp": "2025-05-27T14:32:00Z"
}
```

---

## 4. Destinos Válidos

Retornados dinamicamente por `GET /api/destinations`. Valores fixos no MVP:

| `id` | `display_name` | `altitude_km` | `inclination_deg` | `description` |
|---|---|---|---|---|
| `ISS` | Estação Espacial Internacional | 408 | 51.6 | Órbita da ISS |
| `LEO_GENERIC` | Órbita LEO Genérica | 400 | 28.5 | LEO padrão |
| `SSO` | Sun-Synchronous Orbit | 500 | 97.4 | Órbita heliosíncrona |

---

## 5. Rotas — Auth

### POST /api/auth/register 🔓

Cria nova conta de usuário.

**Request Body:**
```json
{
  "email": "piloto@missionclear.app",
  "password": "MinhaSenh@123",
  "display_name": "Piloto Guss"
}
```

| Campo | Tipo | Regras |
|---|---|---|
| `email` | `string` | Email válido, único no sistema |
| `password` | `string` | Mínimo 8 chars, 1 maiúscula, 1 número |
| `display_name` | `string` | 2–50 chars |

**Response — 201 Created:**
```json
{
  "user": {
    "id": "usr_a80ca0a15f2b4d3e8c91b2e7f3a4d5c6",
    "email": "piloto@missionclear.app",
    "display_name": "Piloto Guss",
    "role": "Researcher",
    "created_at": "2025-05-27T14:32:00Z"
  },
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refresh_token": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
  "expires_in": 3600
}
```

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `409` | `EMAIL_ALREADY_EXISTS` | Email já cadastrado |
| `400` | `INVALID_PASSWORD_FORMAT` | Senha não atende os requisitos |
| `400` | `MISSING_PARAMETER` | Campo obrigatório ausente |

---

### POST /api/auth/login 🔓

Autentica usuário existente.

**Request Body:**
```json
{
  "email": "piloto@missionclear.app",
  "password": "MinhaSenh@123"
}
```

**Response — 200 OK:**
```json
{
  "user": {
    "id": "usr_a80ca0a15f2b4d3e8c91b2e7f3a4d5c6",
    "email": "piloto@missionclear.app",
    "display_name": "Piloto Guss",
    "role": "Researcher",
    "created_at": "2025-05-27T14:32:00Z",
    "total_missions": 12,
    "best_score": 97
  },
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refresh_token": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
  "expires_in": 3600
}
```

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `401` | `INVALID_CREDENTIALS` | Email ou senha incorretos |
| `400` | `MISSING_PARAMETER` | Campo ausente |

---

### POST /api/auth/refresh 🔓

Renova access_token usando refresh_token.

**Request Body:**
```json
{
  "refresh_token": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4..."
}
```

**Response — 200 OK:**
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expires_in": 3600
}
```

> **Token rotation:** o backend NÃO rotaciona o `refresh_token`. A response retorna apenas `access_token` novo. O `refresh_token` original permanece válido até seu TTL de 7 dias — mantenha-o em SecureStore sem modificar.

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `401` | `INVALID_REFRESH_TOKEN` | Token inválido ou expirado |

---

### POST /api/auth/logout 🔑

Invalida o refresh_token atual.

**Request Body:**
```json
{
  "refresh_token": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4..."
}
```

**Response — 204 No Content**

---

## 6. Rotas — Usuário

### GET /api/users/me 🔑

Retorna perfil do usuário autenticado.

**Response — 200 OK:**
```json
{
  "id": "usr_a80ca0a15f2b4d3e8c91b2e7f3a4d5c6",
  "email": "piloto@missionclear.app",
  "display_name": "Piloto Guss",
  "role": "Researcher",
  "created_at": "2025-05-27T14:32:00Z",
  "stats": {
    "total_missions": 12,
    "successful_missions": 9,
    "failed_missions": 2,
    "aborted_missions": 1,
    "success_rate": 0.75,
    "best_score": 97,
    "average_score": 81,
    "favorite_destination": "ISS",
    "total_delta_v_km_s": 112.8
  }
}
```

---

### PUT /api/users/me 🔑

Atualiza perfil do usuário.

**Request Body (todos os campos opcionais):**
```json
{
  "display_name": "Novo Nome",
  "password": "NovaSenha@456",
  "current_password": "MinhaSenh@123"
}
```

> `current_password` obrigatório somente se `password` estiver no body.

**Response — 200 OK:** mesmo shape de `GET /api/users/me`

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `401` | `INVALID_CURRENT_PASSWORD` | current_password incorreta |
| `400` | `INVALID_PASSWORD_FORMAT` | Nova senha não atende requisitos |

---

### GET /api/users/me/favorites 🔑

Retorna detritos favoritados e janelas de lançamento salvas do usuário autenticado.

**Response — 200 OK:**
```json
{
  "debris_ids": ["25544", "37820"],
  "windows": [
    {
      "id": "ISS_2026-06-01T08:00:00Z",
      "destination": "ISS",
      "saved_at": "2026-05-30T12:00:00Z"
    }
  ],
  "updated_at": "2026-05-30T12:00:00Z"
}
```

| Campo | Tipo | Descrição |
|---|---|---|
| `debris_ids` | `string[]` | NORAD IDs dos detritos favoritados (max 500) |
| `windows` | `object[]` | Janelas de lançamento salvas — round-trip do JSON enviado pelo Mobile |
| `updated_at` | `string` | ISO 8601 UTC do item mais recentemente salvo |

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `401` | `UNAUTHORIZED` | Sem token |
| `404` | `USER_NOT_FOUND` | Token válido mas conta não existe |

---

### PUT /api/users/me/favorites 🔑

Substitui atomicamente os favoritos do usuário. Campos `null` são ignorados (patch parcial).

**Request Body:**
```json
{
  "debris_ids": ["25544", "37820"],
  "windows": [
    {
      "id": "ISS_2026-06-01T08:00:00Z",
      "destination": "ISS",
      "saved_at": "2026-05-30T12:00:00Z"
    }
  ]
}
```

| Campo | Tipo | Regras |
|---|---|---|
| `debris_ids` | `string[]?` | Se presente: substitui todos os debris favoritados (max 500). `null` = sem alteração. |
| `windows` | `object[]?` | Se presente: substitui todas as janelas salvas (max 200). `null` = sem alteração. Cada objeto deve ter `id` e `destination`. |

**Response — 200 OK:** mesmo shape de `GET /api/users/me/favorites` (estado após a substituição)

> **Comportamento de replace:** cada campo substituído atomicamente (remove todos + insere novos em uma única transação). Enviar `debris_ids: []` limpa todos os debris favoritados.

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `401` | `UNAUTHORIZED` | Sem token |
| `404` | `USER_NOT_FOUND` | Token válido mas conta não existe |

---

## 7. Rotas — Orbital (Público)

### GET /api/destinations 🔓

Lista destinos de missão disponíveis.

**Response — 200 OK:**
```json
{
  "destinations": [
    {
      "id": "ISS",
      "display_name": "Estação Espacial Internacional",
      "altitude_km": 408,
      "inclination_deg": 51.6,
      "description": "Órbita da ISS — destino mais popular para missões LEO",
      "delta_v_km_s": 9.40,
      "mission_duration_hours": 6.2,
      "icon": "iss"
    },
    {
      "id": "LEO_GENERIC",
      "display_name": "Órbita LEO Genérica",
      "altitude_km": 400,
      "inclination_deg": 28.5,
      "description": "Órbita baixa padrão para satélites de observação",
      "delta_v_km_s": 9.20,
      "mission_duration_hours": 5.8,
      "icon": "leo"
    },
    {
      "id": "SSO",
      "display_name": "Sun-Synchronous Orbit",
      "altitude_km": 500,
      "inclination_deg": 97.4,
      "description": "Órbita heliosíncrona — usada por satélites de imageamento",
      "delta_v_km_s": 10.10,
      "mission_duration_hours": 7.0,
      "icon": "sso"
    }
  ]
}
```

---

### GET /api/debris 🔓

Retorna detritos com posição orbital atual propagada via SGP4.

**Query Parameters:**

| Parâmetro | Tipo | Default | Máx | Descrição |
|---|---|---|---|---|
| `altitude_min_km` | `number` | `200` | — | Altitude mínima |
| `altitude_max_km` | `number` | `2000` | — | Altitude máxima |
| `type` | `string` | todos | — | `debris`, `satellite`, `rocket_body` |
| `limit` | `integer` | `500` | `2000` | Máx objetos retornados |

**Response — 200 OK:**
```json
[
  {
    "id": "25544",
    "name": "ISS (ZARYA)",
    "type": "satellite",
    "latitude": -23.5412,
    "longitude": -46.6324,
    "altitude_km": 408.50,
    "velocity_km_s": 7.660,
    "source": "celestrak",
    "updated_at": "2025-05-27T14:32:00Z"
  },
  {
    "id": "37820",
    "name": "COSMOS 2251 DEB",
    "type": "debris",
    "latitude": 62.1234,
    "longitude": 120.5678,
    "altitude_km": 780.30,
    "velocity_km_s": 7.412,
    "source": "celestrak",
    "updated_at": "2025-05-27T14:32:00Z"
  }
]
```

**Cache-Control:** `max-age=60` — Mobile deve cachear por 60s antes de chamar novamente.

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `503` | `CACHE_NOT_READY` | API ainda inicializando |

---

### GET /api/debris/stats 🔓

Estatísticas agregadas da população de detritos.

**Response — 200 OK:**
```json
{
  "total_tracked": 21432,
  "by_type": {
    "debris": 15234,
    "satellite": 4823,
    "rocket_body": 1375
  },
  "by_altitude_band": {
    "low_200_500km": 8234,
    "mid_500_1000km": 7123,
    "high_1000_2000km": 6075
  },
  "sources": {
    "celestrak": 21432,
    "keeptrack": 0
  },
  "last_updated": "2025-05-27T14:32:00Z"
}
```

---

### GET /api/debris/{id} 🔓

Detalhe de um objeto específico.

**Path Parameter:** `id` — NORAD Catalog Number

**Response — 200 OK:**
```json
{
  "id": "37820",
  "name": "COSMOS 2251 DEB",
  "type": "debris",
  "latitude": 62.1234,
  "longitude": 120.5678,
  "altitude_km": 780.30,
  "velocity_km_s": 7.412,
  "source": "celestrak",
  "updated_at": "2025-05-27T14:32:00Z",
  "tle": {
    "epoch": "2025-05-27T00:00:00Z",
    "line1": "1 37820U 93036PQ  25147.00000000  .00000082  00000-0  99999-4 0  9999",
    "line2": "2 37820  74.0615 123.4567 0048123  12.3456 347.8901 14.68123456123456"
  },
  "orbit": {
    "inclination_deg": 74.06,
    "eccentricity": 0.0048,
    "period_minutes": 97.9,
    "apogee_km": 815.2,
    "perigee_km": 745.1
  }
}
```

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `404` | `DEBRIS_NOT_FOUND` | ID não encontrado no cache |
| `503` | `CACHE_NOT_READY` | API ainda inicializando |

---

## 8. Rotas — Janelas de Lançamento

### GET /api/launch-windows 🔓

Todas as janelas de lançamento no período, em slots de 15 minutos.

**Query Parameters:**

| Parâmetro | Tipo | Obrigatório | Limite | Descrição |
|---|---|---|---|---|
| `destination` | `string` | **sim** | ver §4 | ID do destino |
| `from` | `string` | **sim** | — | ISO 8601 UTC |
| `to` | `string` | **sim** | max 48h após `from` | ISO 8601 UTC |

**Response — 200 OK:**
```json
{
  "destination": "Estação Espacial Internacional",
  "from": "2025-05-27T00:00:00Z",
  "to": "2025-05-27T12:00:00Z",
  "total_windows": 48,
  "safe_windows": 41,
  "windows": [
    {
      "start": "2025-05-27T00:00:00Z",
      "end": "2025-05-27T00:15:00Z",
      "risk_score": 0.0000,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
      "is_recommended": true,
      "conjunctions": []
    },
    {
      "start": "2025-05-27T00:15:00Z",
      "end": "2025-05-27T00:30:00Z",
      "risk_score": 0.8740,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
      "is_recommended": false,
      "conjunctions": [
        {
          "debris_id": "37820",
          "debris_name": "COSMOS 2251 DEB",
          "closest_approach_km": 18.50,
          "time_of_closest_approach": "2025-05-27T00:22:00Z",
          "risk_level": "critical"
        }
      ]
    }
  ]
}
```

> `is_recommended: true` quando `risk_score < 0.1`

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `400` | `INVALID_DESTINATION` | Destino desconhecido |
| `400` | `TIME_RANGE_EXCEEDED` | Período > 48h |
| `400` | `MISSING_PARAMETER` | Parâmetro obrigatório ausente |
| `400` | `INVALID_DATE_FORMAT` | Data mal formatada |
| `503` | `CACHE_NOT_READY` | API inicializando |

---

### GET /api/launch-windows/best 🔓

Retorna as N melhores janelas (menor risk_score) no período.

**Query Parameters:**

| Parâmetro | Tipo | Default | Descrição |
|---|---|---|---|
| `destination` | `string` | — | Obrigatório. ID do destino |
| `from` | `string` | — | Obrigatório. ISO 8601 UTC |
| `to` | `string` | — | Obrigatório. ISO 8601 UTC (max 48h) |
| `count` | `integer` | `5` | Quantas janelas retornar (max 20) |
| `max_risk` | `number` | `0.3` | Filtro: só janelas com risk_score ≤ max_risk |

**Response — 200 OK:**
```json
{
  "destination": "Estação Espacial Internacional",
  "from": "2025-05-27T00:00:00Z",
  "to": "2025-05-27T12:00:00Z",
  "best_windows": [
    {
      "rank": 1,
      "start": "2025-05-27T04:15:00Z",
      "end": "2025-05-27T04:30:00Z",
      "risk_score": 0.0000,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
      "conjunctions": []
    },
    {
      "rank": 2,
      "start": "2025-05-27T07:00:00Z",
      "end": "2025-05-27T07:15:00Z",
      "risk_score": 0.0041,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
      "conjunctions": []
    }
  ]
}
```

---

## 9. Rotas — Simulação de Missão

### POST /api/mission/simulate 🔓

Simulação estática — calcula resultado da missão sem stream.

**Request Body:**
```json
{
  "destination": "ISS",
  "departure_time": "2025-05-27T14:32:00Z",
  "arrival_time": "2025-05-27T20:45:00Z"
}
```

**Response — 200 OK:**
```json
{
  "destination": "ISS",
  "departure_time": "2025-05-27T14:32:00Z",
  "arrival_time": "2025-05-27T20:45:00Z",
  "trajectory": [],
  "obstacles": [
    {
      "debris_id": "37820",
      "debris_name": "COSMOS 2251 DEB",
      "closest_approach_km": 4.20,
      "time_of_closest_approach": "2025-05-27T15:10:00Z",
      "risk_level": "critical"
    }
  ],
  "mission_score": 87,
  "risk_score": 0.1240,
  "delta_v_km_s": 9.40
}
```

---

### POST /api/mission/session 🔓

Cria uma sessão de simulação dinâmica (SSE). Retorna `session_id` para abrir o stream.

**Request Body:**
```json
{
  "destination": "ISS",
  "departure_time": "2025-05-27T14:32:00Z",
  "arrival_time": "2025-05-27T20:45:00Z"
}
```

**Response — 201 Created:**
```json
{
  "session_id": "sess_01JWK2M3X4Y5Z6A7B8C9",
  "destination": "ISS",
  "departure_time": "2025-05-27T14:32:00Z",
  "arrival_time": "2025-05-27T20:45:00Z",
  "stream_url": "/api/mission/session/sess_01JWK2M3X4Y5Z6A7B8C9/stream",
  "expires_at": "2025-05-27T22:45:00Z"
}
```

---

### GET /api/mission/session/{sessionId}/stream 🔓

Abre stream SSE da simulação dinâmica. Ver §14 para formato detalhado dos eventos.

**Headers obrigatórios no Mobile:**
```
Accept: text/event-stream
Cache-Control: no-cache
```

**Eventos emitidos:** `debris_update`, `conjunction_alert`, `session_complete`, `heartbeat`

---

### POST /api/mission/session/{sessionId}/complete 🔓🔑

Finaliza a sessão e opcionalmente salva no histórico (requer autenticação).

**Request Body:**
```json
{
  "status": "success",
  "save_to_history": true
}
```

| Campo | Tipo | Valores | Descrição |
|---|---|---|---|
| `status` | `string` | `"success"`, `"failure"`, `"aborted"` | Resultado da missão |
| `save_to_history` | `boolean` | — | `true` salva no histórico — requer Bearer token |

**Response — 200 OK:**
```json
{
  "session_id": "sess_01JWK2M3X4Y5Z6A7B8C9",
  "status": "success",
  "mission_score": 87,
  "risk_score": 0.1240,
  "delta_v_km_s": 9.40,
  "obstacles_encountered": 2,
  "duration_seconds": 22380.0,
  "saved_to_history": true,
  "mission_id": "msn_b1c2d3e4f5a678901234567890abcdef"
}
```

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `404` | `SESSION_NOT_FOUND` | session_id inválido ou expirado |
| `400` | `SESSION_ALREADY_COMPLETED` | Sessão já finalizada |
| `401` | `UNAUTHORIZED` | `save_to_history: true` sem Bearer token |

---

## 10. Rotas — Histórico de Missões

Todas requerem autenticação.

### GET /api/missions 🔑

Lista missões do usuário autenticado.

**Query Parameters:**

| Parâmetro | Tipo | Default | Descrição |
|---|---|---|---|
| `page` | `integer` | `1` | Página |
| `limit` | `integer` | `20` | Resultados por página (max 50) |
| `status` | `string` | todos | `success`, `failure`, `aborted` |
| `destination` | `string` | todos | Filtrar por destino |
| `sort` | `string` | `created_at_desc` | `created_at_desc`, `score_desc`, `risk_score_asc` |

**Response — 200 OK:**
```json
{
  "data": [
    {
      "id": "msn_01JWK2M3X4Y5Z6A7B8C9D0",
      "destination": "ISS",
      "destination_display": "Estação Espacial Internacional",
      "status": "success",
      "mission_score": 87,
      "risk_score": 0.1240,
      "delta_v_km_s": 9.40,
      "obstacles_encountered": 2,
      "departure_time": "2025-05-27T14:32:00Z",
      "arrival_time": "2025-05-27T20:45:00Z",
      "created_at": "2025-05-27T14:32:00Z"
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 20,
    "total": 12,
    "total_pages": 1
  }
}
```

---

### GET /api/missions/{id} 🔑

Detalhe completo de uma missão.

**Response — 200 OK:**
```json
{
  "id": "msn_01JWK2M3X4Y5Z6A7B8C9D0",
  "destination": "ISS",
  "destination_display": "Estação Espacial Internacional",
  "status": "success",
  "mission_score": 87,
  "risk_score": 0.1240,
  "delta_v_km_s": 9.40,
  "departure_time": "2025-05-27T14:32:00Z",
  "arrival_time": "2025-05-27T20:45:00Z",
  "created_at": "2025-05-27T14:32:00Z",
  "obstacles": [
    {
      "debris_id": "37820",
      "debris_name": "COSMOS 2251 DEB",
      "closest_approach_km": 4.20,
      "time_of_closest_approach": "2025-05-27T15:10:00Z",
      "risk_level": "critical"
    }
  ],
  "score_breakdown": {
    "efficiency_score": 42,
    "safety_score": 45,
    "total": 87
  }
}
```

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `404` | `MISSION_NOT_FOUND` | ID não encontrado |
| `403` | `FORBIDDEN` | Missão pertence a outro usuário |

---

### GET /api/missions/stats 🔑

Estatísticas agregadas das missões do usuário.

**Response — 200 OK:**
```json
{
  "total_missions": 12,
  "successful_missions": 9,
  "failed_missions": 2,
  "aborted_missions": 1,
  "success_rate": 0.75,
  "best_score": 97,
  "worst_score": 23,
  "average_score": 81,
  "total_delta_v_km_s": 112.80,
  "total_obstacles_encountered": 18,
  "favorite_destination": "ISS",
  "missions_by_destination": {
    "ISS": 8,
    "LEO_GENERIC": 3,
    "SSO": 1
  }
}
```

---

### DELETE /api/missions/{id} 🔑

Remove missão do histórico.

**Response — 204 No Content**

**Erros:**
| HTTP | `error` | Quando |
|---|---|---|
| `404` | `MISSION_NOT_FOUND` | ID não encontrado |
| `403` | `FORBIDDEN` | Missão pertence a outro usuário |

---

## 11. Rotas — Dashboard

### GET /api/dashboard/summary 🔓🔑

Visão geral orbital. Com token retorna dados do usuário junto.

**Response — 200 OK (sem auth):**
```json
{
  "orbital": {
    "total_tracked_objects": 21432,
    "by_type": {
      "debris": 15234,
      "satellite": 4823,
      "rocket_body": 1375
    },
    "by_altitude_band": {
      "low_200_500km": 8234,
      "mid_500_1000km": 7123,
      "high_1000_2000km": 6075
    },
    "active_conjunction_alerts": 3,
    "last_updated": "2025-05-27T14:32:00Z"
  },
  "user": null
}
```

**Response — 200 OK (com auth):**
```json
{
  "orbital": {
    "total_tracked_objects": 21432,
    "by_type": {
      "debris": 15234,
      "satellite": 4823,
      "rocket_body": 1375
    },
    "by_altitude_band": {
      "low_200_500km": 8234,
      "mid_500_1000km": 7123,
      "high_1000_2000km": 6075
    },
    "active_conjunction_alerts": 3,
    "last_updated": "2025-05-27T14:32:00Z"
  },
  "user": {
    "display_name": "Piloto Guss",
    "total_missions": 12,
    "best_score": 97,
    "last_mission": {
      "destination": "ISS",
      "status": "success",
      "score": 87,
      "created_at": "2025-05-27T14:32:00Z"
    }
  }
}
```

---

### GET /api/dashboard/alerts 🔓

Alertas de conjunção ativos nas próximas N horas para todos os destinos.

**Query Parameters:**

| Parâmetro | Tipo | Default | Descrição |
|---|---|---|---|
| `window_hours` | `integer` | `6` | Janela de tempo (max 24h) |
| `min_risk` | `string` | `medium` | Nível mínimo: `low`, `medium`, `high`, `critical` |

**Response — 200 OK:**
```json
{
  "alerts": [
    {
      "id": "alrt_01JWK2M3",
      "debris_id": "37820",
      "debris_name": "COSMOS 2251 DEB",
      "affected_destination": "ISS",
      "closest_approach_km": 8.20,
      "time_of_closest_approach": "2025-05-27T18:30:00Z",
      "risk_level": "critical",
      "minutes_until_conjunction": 238,
      "detected_at": "2025-05-27T14:32:00Z"
    },
    {
      "id": "alrt_01JWK2M4",
      "debris_id": "28884",
      "debris_name": "FENGYUN 1C DEB",
      "affected_destination": "SSO",
      "closest_approach_km": 45.10,
      "time_of_closest_approach": "2025-05-27T17:00:00Z",
      "risk_level": "high",
      "minutes_until_conjunction": 148,
      "detected_at": "2025-05-27T14:32:00Z"
    }
  ],
  "window_hours": 6,
  "generated_at": "2025-05-27T14:32:00Z"
}
```

---

## 12. Rotas — Sistema

### GET /api/status 🔓

Estado da API. Mobile deve verificar antes de iniciar qualquer request orbital.

**Response — 200 OK:**
```json
{
  "status": "ready",
  "tle_count": 21432,
  "propagated_count": 18901,
  "last_tle_fetch": "2025-05-27T14:00:00Z",
  "last_propagation": "2025-05-27T14:32:00Z",
  "uptime_seconds": 3720,
  "sources": {
    "celestrak": "ok",
    "keeptrack": "unavailable"
  }
}
```

| `status` | Significado |
|---|---|
| `"loading"` | Cache ainda inicializando — aguardar antes de usar outros endpoints |
| `"ready"` | Pronto para uso |

---

## 13. Rotas — Admin

Todas requerem role `Administrator`. Uso interno / devops — nunca chamar do Mobile.

### POST /api/admin/refresh 🔑 (Administrator only)

Força fetch imediato de TLEs do CelesTrak sem aguardar o intervalo automático de 60 minutos.
Útil em desenvolvimento e para validar dados ao vivo durante apresentação.

> **Aviso:** operação **síncrona** — pode levar até 60 s dependendo do número de catálogos e latência de rede. Configure o timeout do HTTP client em > 90 s para esta rota.

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
| `403` | `FORBIDDEN` | Token válido mas role ≠ Administrator |
| `503` | `CACHE_NOT_READY` | CelesTrak inacessível e fallback de DB falhou |

---

## 14. SSE — Protocolo de Streaming

O stream SSE da rota `GET /api/mission/session/{sessionId}/stream` emite eventos no formato padrão:

```
event: <event_name>\n
data: <json_payload>\n\n
```

### Evento: `debris_update`

Emitido a cada 30 segundos com posições atualizadas dos debris relevantes (dentro de 500km da trajetória).

```
event: debris_update
data: {"timestamp":"2025-05-27T14:33:00Z","objects":[{"id":"37820","name":"COSMOS 2251 DEB","latitude":63.1234,"longitude":121.5678,"altitude_km":780.30,"velocity_km_s":7.412,"distance_from_trajectory_km":342.5},{"id":"28884","name":"FENGYUN 1C DEB","latitude":-13.3456,"longitude":99.7654,"altitude_km":850.20,"velocity_km_s":7.380,"distance_from_trajectory_km":87.3}]}
```

Campos de cada objeto em `objects`:

| Campo | Tipo | Descrição |
|---|---|---|
| `id` | `string` | NORAD ID |
| `name` | `string` | Nome do objeto |
| `latitude` | `number` | Latitude atual |
| `longitude` | `number` | Longitude atual |
| `altitude_km` | `number` | Altitude atual |
| `velocity_km_s` | `number` | Velocidade |
| `distance_from_trajectory_km` | `number` | Distância da trajetória da missão |

---

### Evento: `conjunction_alert`

Emitido quando um debris entra na zona de atenção (< 200km da trajetória).

```
event: conjunction_alert
data: {"debris_id":"37820","debris_name":"COSMOS 2251 DEB","closest_approach_km":18.50,"time_of_closest_approach":"2025-05-27T15:10:00Z","risk_level":"critical","seconds_until_conjunction":2280}
```

| Campo | Tipo | Descrição |
|---|---|---|
| `debris_id` | `string` | NORAD ID |
| `debris_name` | `string` | Nome |
| `closest_approach_km` | `number` | Distância mínima prevista |
| `time_of_closest_approach` | `string` | ISO 8601 UTC |
| `risk_level` | `string` | `low`, `medium`, `high`, `critical` |
| `seconds_until_conjunction` | `integer` | Segundos até a aproximação |

---

### Evento: `session_complete`

Emitido quando a missão chega ao destino (tempo simulado).

```
event: session_complete
data: {"status":"success","mission_score":87,"risk_score":0.1240,"delta_v_km_s":9.40,"obstacles_encountered":2}
```

---

### Evento: `heartbeat`

Emitido a cada 15 segundos para manter a conexão viva.

```
event: heartbeat
data: {"timestamp":"2025-05-27T14:33:15Z"}
```

---

### Formato completo de um evento SSE

```
id: <sequencial>\n
event: <event_name>\n
data: <json_payload>\n\n
```

O backend emite linha `id:` em cada evento. Isso popula `e.lastEventId` no cliente, permitindo que `Last-Event-ID` funcione na reconexão. Eventos sem `id:` (ex: `heartbeat`) não atualizam `lastEventId`.

### Reconexão SSE (Mobile)

Se a conexão cair, o Mobile deve:
1. Aguardar 3 segundos
2. Reconectar com header `Last-Event-ID: <id_do_último_evento_recebido>`
3. Backend retomará o stream a partir desse ponto

---

## 15. Códigos de Erro

| Código | HTTP | Descrição |
|---|---|---|
| `INVALID_DESTINATION` | 400 | Valor de `destination` não é um ID válido |
| `TIME_RANGE_EXCEEDED` | 400 | Período > 48h |
| `INVALID_TIME_RANGE` | 400 | `arrival_time` ≤ `departure_time` |
| `MISSING_PARAMETER` | 400 | Campo obrigatório ausente |
| `INVALID_DATE_FORMAT` | 400 | Data não está em ISO 8601 UTC |
| `INVALID_PASSWORD_FORMAT` | 400 | Senha não atende requisitos |
| `INVALID_CURRENT_PASSWORD` | 401 | Senha atual incorreta ao alterar senha |
| `INVALID_CREDENTIALS` | 401 | Email ou senha incorretos no login |
| `TOKEN_EXPIRED` | 401 | access_token expirado — chamar refresh |
| `INVALID_REFRESH_TOKEN` | 401 | refresh_token inválido ou expirado |
| `UNAUTHORIZED` | 401 | Rota requer autenticação |
| `FORBIDDEN` | 403 | Recurso pertence a outro usuário |
| `DEBRIS_NOT_FOUND` | 404 | NORAD ID não encontrado |
| `MISSION_NOT_FOUND` | 404 | Mission ID não encontrado |
| `SESSION_NOT_FOUND` | 404 | Session ID inválido ou expirado |
| `EMAIL_ALREADY_EXISTS` | 409 | Email já cadastrado |
| `SESSION_ALREADY_COMPLETED` | 409 | Sessão já finalizada |
| `CACHE_NOT_READY` | 503 | API ainda inicializando TLEs |
| `INTERNAL_ERROR` | 500 | Erro interno não previsto |
| `USER_NOT_FOUND` | 404 | Token válido mas conta não existe mais |

> **Interceptor de erro (backend):** todos os erros acima são emitidos pelo `GlobalExceptionMiddleware` via `DomainException` e sempre chegam no envelope `{ error, message, timestamp }`. Código `INVALID_ID` não existe — IDs malformados retornam `MISSION_NOT_FOUND` (404) ou `DEBRIS_NOT_FOUND` (404).

---

## 16. Referência de Campos

Todos os campos usados na API — referência rápida para evitar inconsistências.

### Auth
| Campo | Tipo | Usado em |
|---|---|---|
| `email` | `string` | register, login request |
| `password` | `string` | register, login, update request |
| `current_password` | `string` | update user request |
| `display_name` | `string` | register, user response |
| `access_token` | `string` | auth responses |
| `refresh_token` | `string` | auth responses, refresh request, logout request |
| `expires_in` | `integer` | auth responses (segundos) |

### Usuário
| Campo | Tipo | Usado em |
|---|---|---|
| `id` | `string` | user response (prefixo `usr_` + 32 hex) |
| `role` | `string` | user response — `"Researcher"` \| `"Administrator"` |
| `email` | `string` | user response |
| `display_name` | `string` | user response |
| `created_at` | `string` | user response |
| `total_missions` | `integer` | user stats |
| `successful_missions` | `integer` | user stats |
| `failed_missions` | `integer` | user stats |
| `aborted_missions` | `integer` | user stats |
| `success_rate` | `number` | user stats (0.0–1.0) |
| `best_score` | `integer` | user stats |
| `average_score` | `integer` | user stats |
| `favorite_destination` | `string` | user stats |
| `total_delta_v_km_s` | `number` | user stats |

### Debris
| Campo | Tipo | Usado em |
|---|---|---|
| `id` | `string` | DebrisDto (NORAD ID) |
| `name` | `string` | DebrisDto |
| `type` | `string` | DebrisDto (`debris`, `satellite`, `rocket_body`) |
| `latitude` | `number` | DebrisDto |
| `longitude` | `number` | DebrisDto |
| `altitude_km` | `number` | DebrisDto |
| `velocity_km_s` | `number` | DebrisDto |
| `source` | `string` | DebrisDto (`celestrak`, `keeptrack`) |
| `updated_at` | `string` | DebrisDto |
| `distance_from_trajectory_km` | `number` | SSE debris_update |

### Missão
| Campo | Tipo | Usado em |
|---|---|---|
| `destination` | `string` | simulate request/response, session, mission |
| `destination_display` | `string` | mission response |
| `departure_time` | `string` | simulate/session request/response |
| `arrival_time` | `string` | simulate/session request/response |
| `trajectory` | `array` | simulate response (vazio no MVP) |
| `obstacles` | `array` | simulate response, mission detail |
| `mission_score` | `integer` | simulate response, session complete, mission |
| `risk_score` | `number` | simulate response, window, session complete, mission |
| `delta_v_km_s` | `number` | simulate response, window, session complete, mission |
| `status` | `string` | session complete request, mission (`success`, `failure`, `aborted`) |
| `save_to_history` | `boolean` | session complete request |
| `obstacles_encountered` | `integer` | session complete, mission stats |
| `duration_seconds` | `number` | session complete (double — ex: `22380.0`) |
| `saved_to_history` | `boolean` | session complete response |
| `mission_id` | `string` | session complete response (prefixo `msn_`) |

### Conjunção / Obstáculo
| Campo | Tipo | Usado em |
|---|---|---|
| `debris_id` | `string` | ConjunctionDto, ObstacleDto, alert |
| `debris_name` | `string` | ConjunctionDto, ObstacleDto, alert |
| `closest_approach_km` | `number` | ConjunctionDto, ObstacleDto, alert |
| `time_of_closest_approach` | `string` | ConjunctionDto, ObstacleDto, alert |
| `risk_level` | `string` | ConjunctionDto, ObstacleDto, alert (`low`, `medium`, `high`, `critical`) |
| `seconds_until_conjunction` | `integer` | SSE conjunction_alert |
| `minutes_until_conjunction` | `integer` | dashboard alerts |

### Janela de Lançamento
| Campo | Tipo | Usado em |
|---|---|---|
| `start` | `string` | LaunchWindowDto |
| `end` | `string` | LaunchWindowDto |
| `risk_score` | `number` | LaunchWindowDto |
| `delta_v_km_s` | `number` | LaunchWindowDto |
| `duration_hours` | `number` | LaunchWindowDto |
| `is_recommended` | `boolean` | LaunchWindowDto |
| `conjunctions` | `array` | LaunchWindowDto |
| `rank` | `integer` | BestWindowDto |
| `total_windows` | `integer` | launch-windows response |
| `safe_windows` | `integer` | launch-windows response |

### Destino
| Campo | Tipo | Usado em |
|---|---|---|
| `id` | `string` | DestinationDto |
| `display_name` | `string` | DestinationDto |
| `altitude_km` | `number` | DestinationDto |
| `inclination_deg` | `number` | DestinationDto |
| `description` | `string` | DestinationDto |
| `delta_v_km_s` | `number` | DestinationDto |
| `mission_duration_hours` | `number` | DestinationDto |
| `icon` | `string` | DestinationDto (`iss`, `leo`, `sso`) |

### Sistema
| Campo | Tipo | Usado em |
|---|---|---|
| `status` | `string` | StatusResponse (`loading`, `ready`) |
| `tle_count` | `integer` | StatusResponse |
| `propagated_count` | `integer` | StatusResponse |
| `last_tle_fetch` | `string` | StatusResponse |
| `last_propagation` | `string` | StatusResponse |
| `uptime_seconds` | `integer` | StatusResponse |

### Erro
| Campo | Tipo | Usado em |
|---|---|---|
| `error` | `string` | ApiErrorDto |
| `message` | `string` | ApiErrorDto |
| `timestamp` | `string` | ApiErrorDto |

---

## 17. Mocks para Mobile

O Mobile pode desenvolver com estes JSONs estáticos antes do backend estar pronto.

### /api/status
```json
{"status":"ready","tle_count":21432,"propagated_count":18901,"last_tle_fetch":"2025-05-27T14:00:00Z","last_propagation":"2025-05-27T14:32:00Z","uptime_seconds":3720,"sources":{"celestrak":"ok","keeptrack":"unavailable"}}
```

### /api/destinations
```json
{"destinations":[{"id":"ISS","display_name":"Estação Espacial Internacional","altitude_km":408,"inclination_deg":51.6,"description":"Órbita da ISS — destino mais popular para missões LEO","delta_v_km_s":9.40,"mission_duration_hours":6.2,"icon":"iss"},{"id":"LEO_GENERIC","display_name":"Órbita LEO Genérica","altitude_km":400,"inclination_deg":28.5,"description":"Órbita baixa padrão para satélites de observação","delta_v_km_s":9.20,"mission_duration_hours":5.8,"icon":"leo"},{"id":"SSO","display_name":"Sun-Synchronous Orbit","altitude_km":500,"inclination_deg":97.4,"description":"Órbita heliosíncrona — usada por satélites de imageamento","delta_v_km_s":10.10,"mission_duration_hours":7.0,"icon":"sso"}]}
```

### /api/debris (primeiros 4 objetos)
```json
[{"id":"25544","name":"ISS (ZARYA)","type":"satellite","latitude":-23.5412,"longitude":-46.6324,"altitude_km":408.50,"velocity_km_s":7.660,"source":"celestrak","updated_at":"2025-05-27T14:32:00Z"},{"id":"37820","name":"COSMOS 2251 DEB","type":"debris","latitude":62.1234,"longitude":120.5678,"altitude_km":780.30,"velocity_km_s":7.412,"source":"celestrak","updated_at":"2025-05-27T14:32:00Z"},{"id":"28884","name":"FENGYUN 1C DEB","type":"debris","latitude":-12.3456,"longitude":98.7654,"altitude_km":850.20,"velocity_km_s":7.380,"source":"celestrak","updated_at":"2025-05-27T14:32:00Z"},{"id":"22675","name":"ARIANE 44L R/B","type":"rocket_body","latitude":35.6789,"longitude":-10.2345,"altitude_km":620.80,"velocity_km_s":7.520,"source":"celestrak","updated_at":"2025-05-27T14:32:00Z"}]
```

### /api/debris/stats
```json
{"total_tracked":21432,"by_type":{"debris":15234,"satellite":4823,"rocket_body":1375},"by_altitude_band":{"low_200_500km":8234,"mid_500_1000km":7123,"high_1000_2000km":6075},"sources":{"celestrak":21432,"keeptrack":0},"last_updated":"2025-05-27T14:32:00Z"}
```

### /api/launch-windows/best
```json
{"destination":"Estação Espacial Internacional","from":"2025-05-27T00:00:00Z","to":"2025-05-27T12:00:00Z","best_windows":[{"rank":1,"start":"2025-05-27T04:15:00Z","end":"2025-05-27T04:30:00Z","risk_score":0.0000,"delta_v_km_s":9.40,"duration_hours":6.2,"conjunctions":[]},{"rank":2,"start":"2025-05-27T07:00:00Z","end":"2025-05-27T07:15:00Z","risk_score":0.0041,"delta_v_km_s":9.40,"duration_hours":6.2,"conjunctions":[]}]}
```

### POST /api/auth/login (response)
```json
{"user":{"id":"usr_01JWK2M3X4Y5Z6A7B8C9D0E1F2","email":"piloto@missionclear.app","display_name":"Piloto Guss","created_at":"2025-05-27T14:32:00Z","total_missions":12,"best_score":97},"access_token":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1c3JfMDEiLCJleHAiOjE3NDg0MDgzMjB9.mock","refresh_token":"bW9ja19yZWZyZXNoX3Rva2VuX2Zvcl9kZXY","expires_in":3600}
```

### /api/missions
```json
{"data":[{"id":"msn_01JWK2M3X4Y5Z6A7B8C9D0","destination":"ISS","destination_display":"Estação Espacial Internacional","status":"success","mission_score":87,"risk_score":0.1240,"delta_v_km_s":9.40,"obstacles_encountered":2,"departure_time":"2025-05-27T14:32:00Z","arrival_time":"2025-05-27T20:45:00Z","created_at":"2025-05-27T14:32:00Z"},{"id":"msn_01JWK2M3X4Y5Z6A7B8C9D1","destination":"SSO","destination_display":"Sun-Synchronous Orbit","status":"failure","mission_score":23,"risk_score":0.9100,"delta_v_km_s":10.10,"obstacles_encountered":5,"departure_time":"2025-05-26T10:00:00Z","arrival_time":"2025-05-26T17:00:00Z","created_at":"2025-05-26T10:00:00Z"}],"pagination":{"page":1,"limit":20,"total":12,"total_pages":1}}
```

### SSE stream (texto para simular no Mobile)
```
event: heartbeat
data: {"timestamp":"2025-05-27T14:32:00Z"}

event: debris_update
data: {"timestamp":"2025-05-27T14:32:30Z","objects":[{"id":"37820","name":"COSMOS 2251 DEB","latitude":63.1234,"longitude":121.5678,"altitude_km":780.30,"velocity_km_s":7.412,"distance_from_trajectory_km":342.5}]}

event: conjunction_alert
data: {"debris_id":"37820","debris_name":"COSMOS 2251 DEB","closest_approach_km":18.50,"time_of_closest_approach":"2025-05-27T15:10:00Z","risk_level":"critical","seconds_until_conjunction":2280}

event: session_complete
data: {"status":"success","mission_score":87,"risk_score":0.1240,"delta_v_km_s":9.40,"obstacles_encountered":2}
```

### POST /api/mission/simulate (response)
```json
{"destination":"ISS","departure_time":"2025-05-27T14:32:00Z","arrival_time":"2025-05-27T20:45:00Z","trajectory":[],"obstacles":[{"debris_id":"37820","debris_name":"COSMOS 2251 DEB","closest_approach_km":4.20,"time_of_closest_approach":"2025-05-27T15:10:00Z","risk_level":"critical"}],"mission_score":87,"risk_score":0.1240,"delta_v_km_s":9.40}
```

### GET /api/users/me (response)
```json
{"id":"usr_a80ca0a15f2b4d3e8c91b2e7f3a4d5c6","email":"piloto@missionclear.app","display_name":"Piloto Guss","role":"Researcher","created_at":"2025-05-27T14:32:00Z","stats":{"total_missions":12,"successful_missions":9,"failed_missions":2,"aborted_missions":1,"success_rate":0.75,"best_score":97,"average_score":81,"favorite_destination":"ISS","total_delta_v_km_s":112.8}}
```

### GET /api/missions/{id} (response)
```json
{"id":"msn_b1c2d3e4f5a678901234567890abcdef","destination":"ISS","destination_display":"Estação Espacial Internacional","status":"success","mission_score":87,"risk_score":0.1240,"delta_v_km_s":9.40,"departure_time":"2025-05-27T14:32:00Z","arrival_time":"2025-05-27T20:45:00Z","created_at":"2025-05-27T14:32:00Z","obstacles":[{"debris_id":"37820","debris_name":"COSMOS 2251 DEB","closest_approach_km":4.20,"time_of_closest_approach":"2025-05-27T15:10:00Z","risk_level":"critical"}],"score_breakdown":{"efficiency_score":42,"safety_score":45,"total":87}}
```

### GET /api/dashboard/summary (sem auth)
```json
{"orbital":{"total_tracked_objects":21432,"by_type":{"debris":15234,"satellite":4823,"rocket_body":1375},"by_altitude_band":{"low_200_500km":8234,"mid_500_1000km":7123,"high_1000_2000km":6075},"active_conjunction_alerts":3,"last_updated":"2025-05-27T14:32:00Z"},"user":null}
```

### GET /api/dashboard/summary (com auth)
```json
{"orbital":{"total_tracked_objects":21432,"by_type":{"debris":15234,"satellite":4823,"rocket_body":1375},"by_altitude_band":{"low_200_500km":8234,"mid_500_1000km":7123,"high_1000_2000km":6075},"active_conjunction_alerts":3,"last_updated":"2025-05-27T14:32:00Z"},"user":{"display_name":"Piloto Guss","total_missions":12,"best_score":97,"last_mission":{"destination":"ISS","status":"success","score":87,"created_at":"2025-05-27T14:32:00Z"}}}
```

### GET /api/dashboard/alerts (response)
```json
{"alerts":[{"id":"alrt_d4e5f6a7b8c9012345678901234567ab","debris_id":"37820","debris_name":"COSMOS 2251 DEB","affected_destination":"ISS","closest_approach_km":8.20,"time_of_closest_approach":"2025-05-27T18:30:00Z","risk_level":"critical","minutes_until_conjunction":238,"detected_at":"2025-05-27T14:32:00Z"}],"window_hours":6,"generated_at":"2025-05-27T14:32:00Z"}
```

### POST /api/mission/session/{id}/complete (response, save_to_history=true)
```json
{"session_id":"sess_c3d4e5f6a7b890123456789012345678","status":"success","mission_score":87,"risk_score":0.1240,"delta_v_km_s":9.40,"obstacles_encountered":2,"duration_seconds":22380.0,"saved_to_history":true,"mission_id":"msn_b1c2d3e4f5a678901234567890abcdef"}
```

### Erro CACHE_NOT_READY
```json
{"error":"CACHE_NOT_READY","message":"Orbital data is still loading. Retry in 30 seconds.","timestamp":"2025-05-27T14:32:00Z"}
```

---

---

## 18. Mobile — Variáveis de Ambiente

> React Native não tem `.env` nativo. Use `react-native-config` (bare) ou `expo-constants` + `app.config.ts` (Expo).

### Variáveis necessárias

| Variável | Obrigatória | Exemplo | Descrição |
|---|---|---|---|
| `API_URL` | sim | `http://localhost:5000` | Base URL da API. Sem barra no final. |
| `API_TIMEOUT_MS` | não | `15000` | Timeout das requests REST (não aplica a SSE). |
| `ENV` | sim | `dev` | `dev` \| `staging` \| `prod`. |
| `SSE_RECONNECT_DELAY_MS` | não | `3000` | Delay base de reconexão SSE (§14 = 3s). |
| `CACHE_RETRY_DELAY_MS` | não | `30000` | Delay de retry para `CACHE_NOT_READY`. |
| `TOKEN_REFRESH_SKEW_S` | não | `60` | Margem para refresh proativo antes de `expires_in`. |
| `ENABLE_DEBUG_LOGS` | não | `true` | Liga logs de request/response. Sempre `false` em prod. |

### `.env.example`

```dotenv
# Base da API (sem barra final)
# Android emulator: http://10.0.2.2:5000 | iOS simulator: http://localhost:5000
API_URL=http://localhost:5000

# Ambiente: dev | staging | prod
ENV=dev

# Timeouts e retries
API_TIMEOUT_MS=15000
SSE_RECONNECT_DELAY_MS=3000
CACHE_RETRY_DELAY_MS=30000
TOKEN_REFRESH_SKEW_S=60

# Logs (sempre false em prod)
ENABLE_DEBUG_LOGS=true
```

### Por ambiente

| Arquivo | Git | Uso |
|---|---|---|
| `.env` | **gitignored** | Dev local — contém URL real |
| `.env.staging` | versionado | Staging |
| `.env.production` | versionado | Prod (sem secrets) |
| `.env.example` | versionado | Template para onboarding |

> **Nunca** guardar tokens JWT em `.env` — eles vivem em `SecureStore` em runtime.

---

## 19. Mobile — Interceptor de Request

### Armazenamento de token

| Item | Storage | Razão |
|---|---|---|
| `access_token` | **SecureStore** | Credencial sensível (Keychain/Keystore). |
| `refresh_token` | **SecureStore** | TTL 7 dias — proteger igual. |
| `user` (objeto) | AsyncStorage | Não sensível, leitura frequente para UI. |
| `expires_at` (epoch ms) | AsyncStorage | `now + expires_in * 1000` — para refresh proativo. |

Bibliotecas: `expo-secure-store` (Expo) ou `react-native-keychain` (bare). **Nunca** AsyncStorage para tokens.

### Lógica do interceptor

```ts
// Rotas de auth que NUNCA recebem Bearer (token vai no body ou não existe)
const NO_AUTH_PATHS = [
  '/api/auth/register',
  '/api/auth/login',
  '/api/auth/refresh',
];

httpClient.interceptors.request.use(async (config) => {
  config.headers['Content-Type'] = 'application/json';
  config.headers['Accept'] = 'application/json';

  const isNoAuth = NO_AUTH_PATHS.some((p) => config.url?.startsWith(p));
  if (isNoAuth) return config;

  const token = await SecureStore.getItemAsync('access_token');
  if (token) {
    config.headers['Authorization'] = `Bearer ${token}`;
  }
  // Sem token + rota pública (🔓/🔑?): segue sem header → backend responde dados públicos.
  // Sem token + rota 🔑 estrita: backend devolve 401 UNAUTHORIZED.
  return config;
});
```

### Regras por tipo de rota

| Tipo | Rotas | Comportamento |
|---|---|---|
| 🔓 público | `/api/debris*`, `/api/destinations`, `/api/launch-windows*`, `/api/status`, `/api/dashboard/alerts`, `POST /api/mission/simulate`, `POST /api/mission/session` | Injeta Bearer **se existir**, nunca obriga. |
| 🔑? opcional | `GET /api/dashboard/summary`, `POST .../complete` | Injeta se existir. Com token a resposta é enriquecida. |
| 🔑 estrita | `/api/users/me`, `/api/missions*`, `/api/auth/logout` | Injeta se existir. Sem token → backend retorna 401. |
| Auth | `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh` | **Nunca injeta** — token no body ou inexistente. |

---

## 20. Mobile — Interceptor de Response

Todo erro chega no envelope `{ error, message, timestamp }` (§3). O app exibe mensagens próprias por código `error`, **nunca** o campo `message` (é para desenvolvedor).

### Tratamento por status

| HTTP | `error` | Ação | Mensagem ao usuário |
|---|---|---|---|
| 400 | `MISSING_PARAMETER` / `INVALID_DATE_FORMAT` | Validar antes de enviar. | "Dados inválidos. Revise os campos." |
| 400 | `INVALID_DESTINATION` | Recarregar `/api/destinations`. | "Destino indisponível." |
| 400 | `TIME_RANGE_EXCEEDED` | Bloquear > 48h no seletor. | "Período não pode passar de 48 horas." |
| 400 | `INVALID_TIME_RANGE` | Validar datas no form. | "Data de chegada deve ser posterior à partida." |
| 400 | `INVALID_PASSWORD_FORMAT` | Marcar campo senha. | "Senha: mín. 8 chars, 1 maiúscula e 1 número." |
| 401 | `TOKEN_EXPIRED` | **Refresh automático + retry** (fluxo abaixo). | (transparente) |
| 401 | `INVALID_CREDENTIALS` | Sem retry. Mostrar no form de login. | "Email ou senha incorretos." |
| 401 | `INVALID_CURRENT_PASSWORD` | Marcar campo senha atual. | "Senha atual incorreta." |
| 401 | `INVALID_REFRESH_TOKEN` | **Logout forçado** → tela de login. | "Sua sessão expirou. Entre novamente." |
| 401 | `UNAUTHORIZED` | Sem token: pedir login. Com token expirado: 1 refresh. | "Faça login para continuar." |
| 403 | `FORBIDDEN` | Não tentar de novo. Voltar à lista. | "Você não tem acesso a este recurso." |
| 404 | `SESSION_NOT_FOUND` | Fechar SSE, descartar sessão. | "Simulação expirou. Inicie uma nova." |
| 404 | `MISSION_NOT_FOUND` / `DEBRIS_NOT_FOUND` / `USER_NOT_FOUND` | Voltar à lista. | "Item não encontrado." |
| 409 | `EMAIL_ALREADY_EXISTS` | Marcar campo email. | "Email já cadastrado." |
| 409 | `SESSION_ALREADY_COMPLETED` | Usar resultado já existente. | (sem ação visível) |
| 503 | `CACHE_NOT_READY` | **Retry** após `CACHE_RETRY_DELAY_MS` com skeleton. | "Carregando dados orbitais..." |
| 500 | `INTERNAL_ERROR` | Log + opção de tentar de novo. | "Erro no servidor. Tente novamente." |
| — | network/timeout | Banner offline + retry manual. | "Sem conexão. Verifique sua internet." |

### Fluxo de refresh automático (TOKEN_EXPIRED)

```ts
let isRefreshing = false;
let pendingQueue: Array<{ resolve: (t: string) => void; reject: (e: unknown) => void }> = [];

function flushQueue(error: unknown, token: string | null) {
  pendingQueue.forEach((p) => (error ? p.reject(error) : p.resolve(token!)));
  pendingQueue = [];
}

httpClient.interceptors.response.use(
  (res) => res,
  async (error) => {
    const original = error.config;
    const status = error.response?.status;
    const code = error.response?.data?.error;
    const shouldRefresh = status === 401 && code === 'TOKEN_EXPIRED' && !original._retry;

    if (!shouldRefresh) return Promise.reject(error);

    if (isRefreshing) {
      return new Promise((resolve, reject) => {
        pendingQueue.push({
          resolve: (token) => { original.headers['Authorization'] = `Bearer ${token}`; resolve(httpClient(original)); },
          reject,
        });
      });
    }

    original._retry = true;
    isRefreshing = true;

    try {
      const refreshToken = await SecureStore.getItemAsync('refresh_token');
      if (!refreshToken) throw new Error('NO_REFRESH_TOKEN');

      // rawClient = instância SEM o interceptor de response (evita loop infinito)
      const { data } = await rawClient.post('/api/auth/refresh', { refresh_token: refreshToken });

      await SecureStore.setItemAsync('access_token', data.access_token);

      flushQueue(null, data.access_token);
      original.headers['Authorization'] = `Bearer ${data.access_token}`;
      return httpClient(original);
    } catch (err) {
      flushQueue(err, null);
      await authStore.forceLogout(); // limpa storage + navega para login
      return Promise.reject(err);
    } finally {
      isRefreshing = false;
    }
  },
);
```

**Garantias anti-loop:**
1. `isRefreshing`: só um refresh em voo — demais entram em `pendingQueue`.
2. `original._retry`: cada request tenta refresh exatamente uma vez.
3. `rawClient`: `/api/auth/refresh` usa cliente sem interceptor de response.

---

## 21. Mobile — Fluxo de Autenticação Completo

### Login

```
POST /api/auth/login { email, password }
→ 200 { user, access_token, refresh_token, expires_in }
1. SecureStore.set(access_token, refresh_token)
2. AsyncStorage.set(user, expires_at = now + expires_in * 1000)
3. authStore.setAuthenticated(user)
4. Navegar para Home
```

### Registro

```
POST /api/auth/register { email, password, display_name }
→ 201 { user, access_token, refresh_token, expires_in }
Mesma persistência do login. Role sempre "Researcher" em novos cadastros.
```

### Logout

```
POST /api/auth/logout { refresh_token }  (Bearer obrigatório)
→ 204
finally (mesmo se a request falhar):
  1. SecureStore.delete(access_token, refresh_token)
  2. AsyncStorage.delete(user, expires_at)
  3. Fechar qualquer SSE aberto
  4. authStore.reset()
  5. Navegar para Login
```

> Sempre limpar storage local mesmo se a request falhar — o usuário deve sair de fato.

### Bootstrap do app (startup)

```
Em paralelo:
  A) GET /api/status
     → "loading": splash "Carregando dados orbitais" + retry automático
     → "ready": liberar telas orbitais

  B) Ler tokens do storage:
     → Sem refresh_token: estado anônimo
     → Com tokens, expirados ou próximos (< TOKEN_REFRESH_SKEW_S):
         POST /api/auth/refresh
         → 200: gravar novo access_token → autenticado
         → 401 INVALID_REFRESH_TOKEN: limpar storage → anônimo
     → Com tokens válidos:
         Opcionalmente GET /api/users/me para hidratar perfil
         → autenticado
```

---

## 22. Mobile — Fluxo Anônimo vs Autenticado

### Detecção de estado

`isAuthenticated = !!user && hasValidOrRefreshableToken`. Mantido em `authStore` (Zustand/Context). A UI deriva tudo desse booleano.

### Disponível sem autenticação (🔓)

- `GET /api/status`, `/api/destinations`
- `GET /api/debris`, `/api/debris/stats`, `/api/debris/{id}`
- `GET /api/launch-windows`, `/api/launch-windows/best`
- `POST /api/mission/simulate`
- `POST /api/mission/session` + stream SSE + `POST .../complete` com `save_to_history: false`
- `GET /api/dashboard/alerts`
- `GET /api/dashboard/summary` (retorna `user: null`)

### Requer autenticação (🔑)

- `GET/PUT /api/users/me`
- `GET/DELETE /api/missions*`, `GET /api/missions/stats`
- `POST .../complete` com `save_to_history: true`

### O que muda na UI

| Surface | Anônimo | Autenticado |
|---|---|---|
| Dashboard | Só bloco `orbital` + CTA "Entrar para salvar missões". | `orbital` + `user` (total_missions, best_score, last_mission). |
| Fim de simulação | Botão "Salvar" → tela de login. | Botão salva direto (`save_to_history: true`). |
| Aba Histórico | Oculta ou placeholder "Faça login". | Lista de `/api/missions` + stats. |
| Perfil | Tela de login/registro. | Dados de `/api/users/me` + editar. |

### Salvar missão (`save_to_history: true`)

```
POST /api/mission/session/{sessionId}/complete
Headers: Authorization: Bearer <token>   ← obrigatório quando save_to_history=true
Body: { "status": "success", "save_to_history": true }
→ 200 { session_id, status, mission_score, risk_score, delta_v_km_s,
         obstacles_encountered, duration_seconds, saved_to_history, mission_id }
```

- Sem token + `save_to_history: true` → **401 UNAUTHORIZED**. Checar `isAuthenticated` na UI antes de oferecer o botão.
- `mission_id` (prefixo `msn_`) na resposta permite navegar ao detalhe.
- Segundo complete na mesma sessão → **409 SESSION_ALREADY_COMPLETED** — reutilizar resultado existente.

---

## 23. Mobile — SSE (Server-Sent Events)

> `fetch`/`XMLHttpRequest` nativos do RN não expõem streaming confiável. Use **`react-native-sse`** (suporta headers customizados, essencial para Bearer e `Last-Event-ID`).

### Abrir a conexão

```
// 1. Criar sessão
POST /api/mission/session { destination, departure_time, arrival_time }
→ 201 { session_id, stream_url, expires_at }

// 2. Abrir SSE no stream_url retornado
const url = API_URL + session.stream_url;
```

```ts
import EventSource from 'react-native-sse';

const es = new EventSource(url, {
  headers: {
    Accept: 'text/event-stream',
    'Cache-Control': 'no-cache',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(lastEventId ? { 'Last-Event-ID': lastEventId } : {}),
  },
  pollingInterval: 0, // sem polling — reconexão manual
});
```

### Tratamento de cada evento

| Evento | Frequência | Ação |
|---|---|---|
| `heartbeat` | ~15s | Resetar watchdog de timeout. Não renderiza. |
| `debris_update` | ~30s | Atualizar posições no mapa. Substituir estado (não acumular). |
| `conjunction_alert` | sob demanda (<200km) | Alerta visual/háptico por `risk_level`. Contagem via `seconds_until_conjunction`. |
| `session_complete` | 1x | Fechar stream, guardar resultado, navegar ao resumo, chamar `POST .../complete`. |

```ts
let lastEventId: string | undefined;

es.addEventListener('heartbeat', () => resetWatchdog());

es.addEventListener('debris_update', (e) => {
  const payload = JSON.parse(e.data);
  store.setDebrisPositions(payload.objects); // replace, não append
  if (e.lastEventId) lastEventId = e.lastEventId;
});

es.addEventListener('conjunction_alert', (e) => {
  store.pushAlert(JSON.parse(e.data));
  if (e.lastEventId) lastEventId = e.lastEventId;
});

es.addEventListener('session_complete', (e) => {
  const result = JSON.parse(e.data);
  store.setMissionResult(result);
  es.close();
  completeSession(session.session_id, result.status);
});

es.addEventListener('error', () => scheduleReconnect());
```

### Reconexão automática

```ts
function scheduleReconnect() {
  es.close();
  setTimeout(() => {
    const next = new EventSource(url, {
      headers: {
        Accept: 'text/event-stream',
        'Cache-Control': 'no-cache',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(lastEventId ? { 'Last-Event-ID': lastEventId } : {}),
      },
      pollingInterval: 0,
    });
    bindHandlers(next); // re-registra listeners
  }, SSE_RECONNECT_DELAY_MS); // 3000ms padrão (§14)
}
```

- Backoff suave (3s → 6s → 12s → cap ~30s).
- Watchdog: sem `heartbeat` por ~45s → tratar como queda e reconectar.
- Se reconectar e receber **404 `SESSION_NOT_FOUND`**: sessão expirou → parar, mostrar "simulação expirou".

### Fechar a conexão

```ts
useEffect(() => {
  const es = openStream();
  return () => {
    es.removeAllEventListeners();
    es.close();
    clearTimeout(reconnectTimer);
    clearTimeout(watchdogTimer);
  };
}, [session.session_id]);
```

Fechar em: unmount, navegação para fora, `session_complete`, logout.

---

## 24. Mobile — Identificação do Usuário

### O backend extrai userId do JWT — mobile NÃO envia userId

Nenhuma rota aceita `user_id` em path, query ou body. O backend lê identidade do claim **`sub`** do `access_token`. O campo `user.id` recebido no login serve só para UI/cache local, nunca é reenviado.

### Claims do JWT (`access_token`)

| Claim | Tipo | Uso no mobile |
|---|---|---|
| `sub` | string | ID do usuário (`usr_...`). Backend usa para resolver dados. |
| `email` | string | Exibição; fonte de verdade é `/api/users/me`. |
| `role` | string | Gating de UI (`"Researcher"` \| `"Administrator"`). **Nunca** para segurança — autorização é server-side. |
| `display_name` | string | Saudação imediata sem chamar `/api/users/me`. |
| `exp` | number (epoch s) | Validade. Base para refresh proativo. |

```ts
// Decodificar APENAS para UI/expiry — nunca para decisão de segurança.
import { jwtDecode } from 'jwt-decode';
type AccessClaims = { sub: string; email: string; role: string; display_name: string; exp: number; };
const claims = jwtDecode<AccessClaims>(accessToken);
```

> O app não valida assinatura JWT (não tem o secret). Toda autorização real (403 `FORBIDDEN`) é server-side.

---

## 25. Mobile — Regras de Negócio

### Sessão de simulação expira em 30 min

- `POST /api/mission/session` retorna `expires_at`. Tratar como descartável após esse instante.
- Qualquer chamada após expirar → **404 `SESSION_NOT_FOUND`**.
- Ao receber: fechar SSE, limpar estado, oferecer "iniciar nova simulação". Nunca reusar `session_id`.
- Manter timer local até `expires_at` para alertar o usuário antes de expirar.

### Cache pode não estar pronto

- Endpoints orbitais podem retornar **503 `CACHE_NOT_READY`** enquanto o backend ingere TLEs.
- Retry automático após `CACHE_RETRY_DELAY_MS` (≈30s), skeleton de loading.
- No startup: `GET /api/status` antes de liberar telas orbitais. Só liberar quando `status == "ready"`.

### Destinos válidos

- Buscar de `GET /api/destinations` — não hardcodar.
- Enviar sempre o **`id`** (`ISS`, `LEO_GENERIC`, `SSO`), nunca `display_name`.
- Destino inválido → **400 `INVALID_DESTINATION`**.
- Cachear localmente; revalidar no startup.

### Status de complete

- `status` ∈ `"success"` | `"failure"` | `"aborted"`.
- Valor vem do evento `session_complete` do SSE; abort manual envia `"aborted"`.

### Datas e ranges

- `arrival_time` deve ser > `departure_time` → **400 `INVALID_TIME_RANGE`**. Validar no seletor.
- Range de janelas: máximo 48h → **400 `TIME_RANGE_EXCEEDED`**. Limitar no date picker.
- Sempre converter local → UTC com `Z` antes de enviar → senão **400 `INVALID_DATE_FORMAT`**.

### Cache HTTP

- `GET /api/debris` traz `Cache-Control: max-age=60`. Cachear 60s antes de refazer — evita sobrecarga.

---

## Changelog

| Data | Versão | Mudança |
|---|---|---|
| 2026-05-26 | 1.0.0 | Criação inicial |
| 2026-05-26 | 2.0.0 | Auth JWT, histórico de missões, SSE dinâmico, dashboard completo, /api/destinations, /api/debris/stats, /api/debris/{id}, /api/launch-windows/best |
| 2026-05-28 | 2.1.0 | Adicionado `role` em todas responses de usuário; `aborted_missions` em stats de `/users/me`; formato de ID documentado (Guid:N); nota Aspire porta dinâmica; `USER_NOT_FOUND` no catálogo de erros; `duration_seconds` como number; seções 17–24 integração mobile (env, interceptors, SSE, auth flow, regras) |
| 2026-05-28 | 2.2.0 | Formato `id:` do protocolo SSE documentado; token rotation explicitado (sem rotation, refresh_token permanece); mocks adicionados para /simulate, /users/me, /missions/{id}, /dashboard/summary (anônimo+auth), /dashboard/alerts, /session/complete — auditoria mobile score 91/100, zero bloqueantes |
| 2026-05-30 | 2.3.0 | Rotas de favoritos documentadas: `GET /api/users/me/favorites` e `PUT /api/users/me/favorites` — replace atômico de debris_ids e windows com patch parcial por null |
| 2026-05-30 | 2.4.0 | `POST /api/admin/refresh` — forçar atualização de TLEs sem restart; requer role Administrator; nova §13 Admin; seções §14–§25 renumeradas |
