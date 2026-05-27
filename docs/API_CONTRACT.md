# Mission Clear — API Contract v2.0

> **Fonte da verdade.** Backend e Mobile devem seguir este documento exatamente.
> Qualquer alteração de campo, rota ou schema deve ser atualizada aqui primeiro.

**Base URL (dev local):** `http://localhost:5000`
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
6. [Rotas — Usuário](#6-rotas--usuário)
7. [Rotas — Orbital (Público)](#7-rotas--orbital-público)
8. [Rotas — Janelas de Lançamento](#8-rotas--janelas-de-lançamento)
9. [Rotas — Simulação de Missão](#9-rotas--simulação-de-missão)
10. [Rotas — Histórico de Missões](#10-rotas--histórico-de-missões)
11. [Rotas — Dashboard](#11-rotas--dashboard)
12. [Rotas — Sistema](#12-rotas--sistema)
13. [SSE — Protocolo de Streaming](#13-sse--protocolo-de-streaming)
14. [Códigos de Erro](#14-códigos-de-erro)
15. [Referência de Campos](#15-referência-de-campos)
16. [Mocks para Mobile](#16-mocks-para-mobile)

---

## 1. Convenções Gerais

### Nomes de campo
- Todos os campos em `snake_case`
- Sem abreviações ambíguas: `altitude_km` não `alt`, `velocity_km_s` não `vel`
- Booleanos com prefixo `is_` ou `has_`

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
    "id": "usr_01JWK2M3X4Y5Z6A7B8C9D0E1F2",
    "email": "piloto@missionclear.app",
    "display_name": "Piloto Guss",
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
    "id": "usr_01JWK2M3X4Y5Z6A7B8C9D0E1F2",
    "email": "piloto@missionclear.app",
    "display_name": "Piloto Guss",
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
  "id": "usr_01JWK2M3X4Y5Z6A7B8C9D0E1F2",
  "email": "piloto@missionclear.app",
  "display_name": "Piloto Guss",
  "created_at": "2025-05-27T14:32:00Z",
  "stats": {
    "total_missions": 12,
    "successful_missions": 9,
    "failed_missions": 3,
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

Abre stream SSE da simulação dinâmica. Ver §13 para formato detalhado dos eventos.

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
  "duration_seconds": 22380,
  "saved_to_history": true,
  "mission_id": "msn_01JWK2M3X4Y5Z6A7B8C9D0"
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

## 13. SSE — Protocolo de Streaming

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

### Reconexão SSE (Mobile)

Se a conexão cair, o Mobile deve:
1. Aguardar 3 segundos
2. Reconectar com header `Last-Event-ID: <id_do_último_evento_recebido>`
3. Backend retomará o stream a partir desse ponto

---

## 14. Códigos de Erro

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

---

## 15. Referência de Campos

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
| `id` | `string` | user response (prefixo `usr_`) |
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
| `duration_seconds` | `integer` | session complete |
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

## 16. Mocks para Mobile

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

### Erro CACHE_NOT_READY
```json
{"error":"CACHE_NOT_READY","message":"Orbital data is still loading. Retry in 30 seconds.","timestamp":"2025-05-27T14:32:00Z"}
```

---

## Changelog

| Data | Versão | Mudança |
|---|---|---|
| 2026-05-26 | 1.0.0 | Criação inicial |
| 2026-05-26 | 2.0.0 | Auth JWT, histórico de missões, SSE dinâmico, dashboard completo, /api/destinations, /api/debris/stats, /api/debris/{id}, /api/launch-windows/best |
