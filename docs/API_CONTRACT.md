# Mission Clear — API Contract

> **Fonte da verdade.** Backend e Mobile devem seguir este documento exatamente.
> Qualquer alteração de campo, rota ou schema deve ser atualizada aqui primeiro.

**Base URL (dev local):** `http://localhost:5000`
**Base URL (produção):** a definir
**Protocolo:** HTTP/1.1
**Formato:** JSON (`Content-Type: application/json`)
**Encoding:** UTF-8
**Timestamps:** ISO 8601 UTC — `2025-05-27T14:32:00Z`

---

## Índice

1. [Convenções Gerais](#1-convenções-gerais)
2. [Envelope de Erro](#2-envelope-de-erro)
3. [Destinos Válidos](#3-destinos-válidos)
4. [GET /api/debris](#4-get-apidebris)
5. [GET /api/launch-windows](#5-get-apilaunch-windows)
6. [POST /api/mission/simulate](#6-post-apimissionsimulate)
7. [GET /api/status](#7-get-apistatus)
8. [Códigos de Erro](#8-códigos-de-erro)
9. [Campos: Referência Completa](#9-campos-referência-completa)
10. [Guia Mobile — Mocks](#10-guia-mobile--mocks)

---

## 1. Convenções Gerais

### Nomes de campo
- Todos os campos em `snake_case`
- Sem abreviações ambíguas (`altitude_km` não `alt`, `velocity_km_s` não `vel`)
- Booleanos com prefixo `is_` ou `has_`

### Números
- Coordenadas: 4 casas decimais (`-23.5412`)
- Altitude/distância: 2 casas decimais (`408.50`)
- Velocidade: 3 casas decimais (`7.660`)
- Scores: 4 casas decimais para `risk_score`, inteiro para `mission_score`
- Delta-v: 2 casas decimais (`9.40`)

### Datas
- Sempre UTC, sempre com `Z` no final
- Formato: `YYYY-MM-DDTHH:mm:ssZ`
- Exemplo: `2025-05-27T14:32:00Z`

### Paginação
- Parâmetro: `limit` (inteiro, default e max por endpoint)
- Sem cursor por enquanto — lista retorna os primeiros N

### Versionamento
- Sem versionamento no MVP. Futuro: `/api/v2/...`

---

## 2. Envelope de Erro

Todo erro retorna o mesmo shape, independente do endpoint.

**HTTP Status:** 400, 404, 503, 500

```json
{
  "error": "ERROR_CODE",
  "message": "Descrição legível para o desenvolvedor.",
  "timestamp": "2025-05-27T14:32:00Z"
}
```

| Campo | Tipo | Descrição |
|---|---|---|
| `error` | `string` | Código de erro em UPPER_SNAKE_CASE |
| `message` | `string` | Mensagem descritiva (não exibir ao usuário final) |
| `timestamp` | `string` | Momento do erro em ISO 8601 UTC |

---

## 3. Destinos Válidos

Valores aceitos no campo `destination` em qualquer endpoint.

| ID | Nome de exibição | Altitude (km) | Inclinação (°) |
|---|---|---|---|
| `ISS` | Estação Espacial Internacional | 408 | 51.6 |
| `LEO_GENERIC` | Órbita LEO Genérica | 400 | 28.5 |
| `SSO` | Sun-Synchronous Orbit | 500 | 97.4 |

**Regra:** `destination` é case-insensitive no backend, mas Mobile deve enviar sempre em UPPER_SNAKE_CASE como documentado.

---

## 4. GET /api/debris

Retorna lista de detritos espaciais com posição orbital atual (propagada via SGP4).

### URL

```
GET /api/debris
```

### Query Parameters

| Parâmetro | Tipo | Obrigatório | Default | Limite | Descrição |
|---|---|---|---|---|---|
| `altitude_min_km` | `number` | não | `200` | — | Altitude mínima em km |
| `altitude_max_km` | `number` | não | `2000` | — | Altitude máxima em km |
| `limit` | `integer` | não | `500` | max `2000` | Máximo de objetos retornados |

### Exemplo de Request

```
GET /api/debris?altitude_min_km=300&altitude_max_km=500&limit=100
```

### Response — 200 OK

Array de objetos. Pode ser vazio `[]` se nenhum objeto estiver na faixa.

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

### Campos do Objeto (DebrisDto)

| Campo | Tipo | Valores possíveis | Descrição |
|---|---|---|---|
| `id` | `string` | NORAD Catalog Number | Identificador único do objeto |
| `name` | `string` | — | Nome oficial do objeto |
| `type` | `string` | `"debris"`, `"satellite"`, `"rocket_body"` | Classificação do objeto |
| `latitude` | `number` | `-90` a `90` | Latitude geodésica em graus |
| `longitude` | `number` | `-180` a `180` | Longitude em graus |
| `altitude_km` | `number` | `200` a `2000` | Altitude sobre a superfície em km |
| `velocity_km_s` | `number` | ~`6.5` a `8.0` | Velocidade orbital em km/s |
| `source` | `string` | `"celestrak"`, `"keeptrack"` | Fonte dos dados TLE |
| `updated_at` | `string` | ISO 8601 UTC | Momento da propagação orbital |

### Erros Possíveis

| HTTP | `error` | Quando |
|---|---|---|
| `503` | `CACHE_NOT_READY` | API ainda carregando TLEs na inicialização |

---

## 5. GET /api/launch-windows

Calcula janelas de lançamento seguras para um destino em um intervalo de tempo.

### URL

```
GET /api/launch-windows
```

### Query Parameters

| Parâmetro | Tipo | Obrigatório | Default | Limite | Descrição |
|---|---|---|---|---|---|
| `destination` | `string` | **sim** | — | ver §3 | ID do destino (ex: `ISS`) |
| `from` | `string` | **sim** | — | — | Início do período (ISO 8601 UTC) |
| `to` | `string` | **sim** | — | max 48h após `from` | Fim do período (ISO 8601 UTC) |

### Exemplo de Request

```
GET /api/launch-windows?destination=ISS&from=2025-05-27T00:00:00Z&to=2025-05-27T12:00:00Z
```

### Response — 200 OK

```json
{
  "destination": "Estação Espacial Internacional",
  "from": "2025-05-27T00:00:00Z",
  "to": "2025-05-27T12:00:00Z",
  "windows": [
    {
      "start": "2025-05-27T00:00:00Z",
      "end": "2025-05-27T00:15:00Z",
      "risk_score": 0.0312,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
      "conjunctions": []
    },
    {
      "start": "2025-05-27T00:15:00Z",
      "end": "2025-05-27T00:30:00Z",
      "risk_score": 0.8740,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
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

### Campos da Response

**Raiz:**

| Campo | Tipo | Descrição |
|---|---|---|
| `destination` | `string` | Nome de exibição do destino |
| `from` | `string` | ISO 8601 UTC — início do período solicitado |
| `to` | `string` | ISO 8601 UTC — fim do período solicitado |
| `windows` | `array<LaunchWindowDto>` | Lista de janelas (slots de 15 minutos) |

**LaunchWindowDto:**

| Campo | Tipo | Descrição |
|---|---|---|
| `start` | `string` | ISO 8601 UTC — início do slot |
| `end` | `string` | ISO 8601 UTC — fim do slot |
| `risk_score` | `number` | `0.0` (sem risco) a `1.0` (risco máximo) |
| `delta_v_km_s` | `number` | Delta-v necessário em km/s |
| `duration_hours` | `number` | Duração estimada da missão em horas |
| `conjunctions` | `array<ConjunctionDto>` | Detritos em rota de colisão neste slot |

**ConjunctionDto:**

| Campo | Tipo | Valores | Descrição |
|---|---|---|---|
| `debris_id` | `string` | NORAD ID | ID do debris em rota de colisão |
| `debris_name` | `string` | — | Nome do objeto |
| `closest_approach_km` | `number` | — | Distância mínima de aproximação em km |
| `time_of_closest_approach` | `string` | ISO 8601 UTC | Momento estimado da aproximação |
| `risk_level` | `string` | `"low"`, `"medium"`, `"high"`, `"critical"` | Nível de risco classificado |

**Tabela de risco:**

| `risk_level` | `closest_approach_km` |
|---|---|
| `"critical"` | < 25 km |
| `"high"` | 25 – 49 km |
| `"medium"` | 50 – 99 km |
| `"low"` | ≥ 100 km |

### Erros Possíveis

| HTTP | `error` | Quando |
|---|---|---|
| `400` | `INVALID_DESTINATION` | `destination` não é um dos IDs válidos (§3) |
| `400` | `TIME_RANGE_EXCEEDED` | Diferença entre `from` e `to` maior que 48h |
| `400` | `MISSING_PARAMETER` | `destination`, `from` ou `to` ausentes |
| `400` | `INVALID_DATE_FORMAT` | Data não está em ISO 8601 |
| `503` | `CACHE_NOT_READY` | API ainda carregando TLEs |

---

## 6. POST /api/mission/simulate

Simula uma trajetória de missão e retorna os detritos no caminho com pontuação.

### URL

```
POST /api/mission/simulate
Content-Type: application/json
```

### Request Body

```json
{
  "destination": "ISS",
  "departure_time": "2025-05-27T14:32:00Z",
  "arrival_time": "2025-05-27T20:45:00Z"
}
```

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `destination` | `string` | **sim** | ID do destino — ver §3 |
| `departure_time` | `string` | **sim** | ISO 8601 UTC — hora de partida |
| `arrival_time` | `string` | **sim** | ISO 8601 UTC — hora de chegada estimada. Deve ser posterior a `departure_time`. |

### Response — 200 OK

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
    },
    {
      "debris_id": "28884",
      "debris_name": "FENGYUN 1C DEB",
      "closest_approach_km": 62.80,
      "time_of_closest_approach": "2025-05-27T17:32:00Z",
      "risk_level": "medium"
    }
  ],
  "mission_score": 87,
  "risk_score": 0.1240,
  "delta_v_km_s": 9.40
}
```

### Campos da Response

| Campo | Tipo | Descrição |
|---|---|---|
| `destination` | `string` | ID do destino (echo do request) |
| `departure_time` | `string` | ISO 8601 UTC (echo) |
| `arrival_time` | `string` | ISO 8601 UTC (echo) |
| `trajectory` | `array` | Pontos da trajetória — vazio no MVP, reservado para V2 |
| `obstacles` | `array<ObstacleDto>` | Detritos identificados no caminho |
| `mission_score` | `integer` | `0` a `100` — pontuação da missão |
| `risk_score` | `number` | `0.0` a `1.0` — risco agregado |
| `delta_v_km_s` | `number` | Delta-v necessário em km/s |

**ObstacleDto:**

| Campo | Tipo | Valores | Descrição |
|---|---|---|---|
| `debris_id` | `string` | NORAD ID | ID do objeto |
| `debris_name` | `string` | — | Nome do objeto |
| `closest_approach_km` | `number` | — | Distância mínima em km |
| `time_of_closest_approach` | `string` | ISO 8601 UTC | Momento da aproximação |
| `risk_level` | `string` | `"low"`, `"medium"`, `"high"`, `"critical"` | Nível de risco |

**Fórmula mission_score (referência para o Mobile exibir corretamente):**
```
mission_score = 0 a 100
  50 pts = eficiência (quanto menor o delta-v, maior)
  50 pts = segurança  (quanto menor o risk_score, maior)
```

### Erros Possíveis

| HTTP | `error` | Quando |
|---|---|---|
| `400` | `INVALID_DESTINATION` | `destination` inválido |
| `400` | `INVALID_TIME_RANGE` | `arrival_time` não é posterior a `departure_time` |
| `400` | `MISSING_PARAMETER` | Campo obrigatório ausente |
| `503` | `CACHE_NOT_READY` | API ainda carregando TLEs |

---

## 7. GET /api/status

Retorna estado da API — útil para o Mobile saber se pode iniciar requests.

### URL

```
GET /api/status
```

### Response — 200 OK

```json
{
  "status": "ready",
  "tle_count": 21432,
  "propagated_count": 18901,
  "last_tle_fetch": "2025-05-27T14:00:00Z",
  "last_propagation": "2025-05-27T14:32:00Z",
  "uptime_seconds": 3720
}
```

| Campo | Tipo | Valores | Descrição |
|---|---|---|---|
| `status` | `string` | `"loading"`, `"ready"` | `"loading"` = cache ainda inicializando |
| `tle_count` | `integer` | — | Total de TLEs armazenados |
| `propagated_count` | `integer` | — | Total de objetos propagados no último ciclo |
| `last_tle_fetch` | `string` | ISO 8601 UTC | Último fetch de TLEs do CelesTrak |
| `last_propagation` | `string` | ISO 8601 UTC | Último ciclo de propagação SGP4 |
| `uptime_seconds` | `integer` | — | Segundos desde o início da API |

---

## 8. Códigos de Erro

Tabela completa de todos os `error` possíveis.

| Código | HTTP | Descrição |
|---|---|---|
| `INVALID_DESTINATION` | 400 | O valor de `destination` não é um ID válido |
| `TIME_RANGE_EXCEEDED` | 400 | Período solicitado excede 48 horas |
| `INVALID_TIME_RANGE` | 400 | `arrival_time` <= `departure_time` |
| `MISSING_PARAMETER` | 400 | Campo obrigatório ausente no request |
| `INVALID_DATE_FORMAT` | 400 | Data não está em ISO 8601 UTC |
| `CACHE_NOT_READY` | 503 | API inicializando — tentar novamente em 30s |
| `INTERNAL_ERROR` | 500 | Erro interno não previsto |

---

## 9. Campos: Referência Completa

Todos os nomes de campo usados na API — referência rápida para evitar inconsistências.

| Campo | Tipo | Usado em |
|---|---|---|
| `id` | `string` | DebrisDto |
| `name` | `string` | DebrisDto, ConjunctionDto, ObstacleDto |
| `type` | `string` | DebrisDto |
| `latitude` | `number` | DebrisDto |
| `longitude` | `number` | DebrisDto |
| `altitude_km` | `number` | DebrisDto |
| `velocity_km_s` | `number` | DebrisDto |
| `source` | `string` | DebrisDto |
| `updated_at` | `string` | DebrisDto |
| `destination` | `string` | LaunchWindowsResponse, SimulateRequest, SimulateResponse |
| `from` | `string` | LaunchWindowsResponse |
| `to` | `string` | LaunchWindowsResponse |
| `windows` | `array` | LaunchWindowsResponse |
| `start` | `string` | LaunchWindowDto |
| `end` | `string` | LaunchWindowDto |
| `risk_score` | `number` | LaunchWindowDto, SimulateResponse |
| `delta_v_km_s` | `number` | LaunchWindowDto, SimulateResponse |
| `duration_hours` | `number` | LaunchWindowDto |
| `conjunctions` | `array` | LaunchWindowDto |
| `debris_id` | `string` | ConjunctionDto, ObstacleDto |
| `debris_name` | `string` | ConjunctionDto, ObstacleDto |
| `closest_approach_km` | `number` | ConjunctionDto, ObstacleDto |
| `time_of_closest_approach` | `string` | ConjunctionDto, ObstacleDto |
| `risk_level` | `string` | ConjunctionDto, ObstacleDto |
| `departure_time` | `string` | SimulateRequest, SimulateResponse |
| `arrival_time` | `string` | SimulateRequest, SimulateResponse |
| `trajectory` | `array` | SimulateResponse |
| `obstacles` | `array` | SimulateResponse |
| `mission_score` | `integer` | SimulateResponse |
| `error` | `string` | ApiErrorDto |
| `message` | `string` | ApiErrorDto |
| `timestamp` | `string` | ApiErrorDto |
| `status` | `string` | StatusResponse |
| `tle_count` | `integer` | StatusResponse |
| `propagated_count` | `integer` | StatusResponse |
| `last_tle_fetch` | `string` | StatusResponse |
| `last_propagation` | `string` | StatusResponse |
| `uptime_seconds` | `integer` | StatusResponse |

---

## 10. Guia Mobile — Mocks

O Mobile pode desenvolver independentemente usando estes JSONs estáticos.

### Mock: /api/debris

Salvar como `mocks/debris.json`:

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
  },
  {
    "id": "28884",
    "name": "FENGYUN 1C DEB",
    "type": "debris",
    "latitude": -12.3456,
    "longitude": 98.7654,
    "altitude_km": 850.20,
    "velocity_km_s": 7.380,
    "source": "celestrak",
    "updated_at": "2025-05-27T14:32:00Z"
  },
  {
    "id": "22675",
    "name": "ARIANE 44L R/B",
    "type": "rocket_body",
    "latitude": 35.6789,
    "longitude": -10.2345,
    "altitude_km": 620.80,
    "velocity_km_s": 7.520,
    "source": "celestrak",
    "updated_at": "2025-05-27T14:32:00Z"
  }
]
```

### Mock: /api/launch-windows

Salvar como `mocks/launch-windows.json`:

```json
{
  "destination": "Estação Espacial Internacional",
  "from": "2025-05-27T00:00:00Z",
  "to": "2025-05-27T12:00:00Z",
  "windows": [
    {
      "start": "2025-05-27T00:00:00Z",
      "end": "2025-05-27T00:15:00Z",
      "risk_score": 0.0000,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
      "conjunctions": []
    },
    {
      "start": "2025-05-27T00:15:00Z",
      "end": "2025-05-27T00:30:00Z",
      "risk_score": 0.8740,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
      "conjunctions": [
        {
          "debris_id": "37820",
          "debris_name": "COSMOS 2251 DEB",
          "closest_approach_km": 18.50,
          "time_of_closest_approach": "2025-05-27T00:22:00Z",
          "risk_level": "critical"
        }
      ]
    },
    {
      "start": "2025-05-27T00:30:00Z",
      "end": "2025-05-27T00:45:00Z",
      "risk_score": 0.1230,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
      "conjunctions": [
        {
          "debris_id": "28884",
          "debris_name": "FENGYUN 1C DEB",
          "closest_approach_km": 87.30,
          "time_of_closest_approach": "2025-05-27T00:38:00Z",
          "risk_level": "medium"
        }
      ]
    },
    {
      "start": "2025-05-27T00:45:00Z",
      "end": "2025-05-27T01:00:00Z",
      "risk_score": 0.0050,
      "delta_v_km_s": 9.40,
      "duration_hours": 6.2,
      "conjunctions": []
    }
  ]
}
```

### Mock: POST /api/mission/simulate

Salvar como `mocks/mission-simulate.json`:

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
    },
    {
      "debris_id": "28884",
      "debris_name": "FENGYUN 1C DEB",
      "closest_approach_km": 62.80,
      "time_of_closest_approach": "2025-05-27T17:32:00Z",
      "risk_level": "medium"
    }
  ],
  "mission_score": 87,
  "risk_score": 0.1240,
  "delta_v_km_s": 9.40
}
```

### Mock: /api/status

Salvar como `mocks/status.json`:

```json
{
  "status": "ready",
  "tle_count": 21432,
  "propagated_count": 18901,
  "last_tle_fetch": "2025-05-27T14:00:00Z",
  "last_propagation": "2025-05-27T14:32:00Z",
  "uptime_seconds": 3720
}
```

### Mock: Erro CACHE_NOT_READY

Salvar como `mocks/error-cache-not-ready.json`:

```json
{
  "error": "CACHE_NOT_READY",
  "message": "Orbital data is still loading. Retry in 30 seconds.",
  "timestamp": "2025-05-27T14:32:00Z"
}
```

---

## Changelog

| Data | Versão | Mudança |
|---|---|---|
| 2026-05-26 | 1.0.0 | Criação inicial do contrato |
