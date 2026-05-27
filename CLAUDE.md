# CLAUDE.md — Mission Clear Backend (C#)

Projeto acadêmico FIAP (3ESPR Global Solution). Backend C# / ASP.NET Core.
Frontend em repo separado: `Fiap-3ESPR-GS-Mobile`.
Prazo: 1 semana (MVP funcional).

---

## Contexto do Sistema

**Mission Clear** ingere dados reais de detritos espaciais, propaga órbitas via SGP4 e calcula janelas de lançamento seguras. Este repo é exclusivamente o motor orbital + API REST.

**Foco orbital:** LEO (200–2.000 km). Não cobrir GEO/MEO no MVP.

---

## Stack

| Categoria | Tecnologia |
|-----------|-----------|
| Linguagem | C# / .NET 8+ |
| Framework web | ASP.NET Core (REST API minimal ou controllers) |
| SGP4 | Biblioteca NuGet existente — **nunca implementar do zero** |
| HTTP client | `HttpClient` (IHttpClientFactory) para fontes externas |
| Serialização | `System.Text.Json` |
| Configuração | `IConfiguration` + `appsettings.json` + env vars |
| Logging | `ILogger<T>` (Microsoft.Extensions.Logging) |

---

## Fontes de Dados Externas

### CelesTrak (fonte principal)
- Endpoint: `https://celestrak.org/NORAD/elements/gp.php?GROUP=debris&FORMAT=json`
- Auth: nenhuma — HTTP GET direto
- Formato: JSON com campos TLE

### KeepTrack (fonte secundária)
- Base: `https://keeptrack.space/api`
- Auth: API key via `IConfiguration` — **nunca hardcoded**
- Rate limit free: 60 req/hr | Com key: 2.000 req/hr

**Regra:** React Native nunca fala com CelesTrak ou KeepTrack diretamente. Sempre via esta API.

---

## Contrato de Dados (fixo — não alterar schemas)

### `GET /api/debris`
```json
[
  {
    "id": "25544",
    "name": "ISS (ZARYA)",
    "type": "debris | satellite | rocket_body",
    "latitude": -23.5,
    "longitude": -46.6,
    "altitude_km": 408.5,
    "velocity_km_s": 7.66,
    "source": "celestrak | keeptrack",
    "updated_at": "2025-05-26T10:00:00Z"
  }
]
```

### `GET /api/launch-windows`
```json
{
  "destination": "ISS",
  "from": "2025-05-27T00:00:00Z",
  "to": "2025-05-28T00:00:00Z",
  "windows": [
    {
      "start": "2025-05-27T14:32:00Z",
      "end": "2025-05-27T14:48:00Z",
      "risk_score": 0.03,
      "delta_v_km_s": 9.4,
      "duration_hours": 6.2,
      "conjunctions": []
    }
  ]
}
```

### `GET /api/mission/simulate`
```json
{
  "trajectory": [],
  "obstacles": [
    {
      "debris_id": "1234",
      "closest_approach_km": 4.2,
      "time_of_closest_approach": "2025-05-27T15:10:00Z",
      "risk_level": "low | medium | high | critical"
    }
  ],
  "mission_score": 87
}
```

---

## Arquitetura Interna (módulos obrigatórios)

```
MissionClear.Api/
├── Controllers/          # Apenas roteamento e serialização. Zero lógica de negócio.
├── Services/
│   ├── DataAggregator    # Ingere CelesTrak + KeepTrack, deduplica, normaliza
│   ├── OrbitalEngine     # SGP4: propaga posições, calcula velocidade/altitude
│   ├── ConjunctionDetector # Proximidade de detritos a uma trajetória
│   └── LaunchWindowCalculator # Slots temporais livres de risco
├── Models/               # DTOs e entidades de domínio
└── Configuration/        # Leitura de IConfiguration
```

**Regra:** lógica de negócio fica em `Services/`, nunca em `Controllers/`.

---

## Não-Negociáveis

| Anti-padrão | Regra |
|-------------|-------|
| `catch {}` vazio | Sempre re-throw ou `ILogger.LogError` + re-throw |
| Secret/API key hardcoded | Sempre `IConfiguration.GetValue` ou `GetRequiredValue` |
| SQL com string interpolation | Parameterizado (se houver SQL direto) |
| `Console.WriteLine` em produção | `ILogger<T>` estruturado |
| Arquivo > 300 linhas | Extrai responsabilidade |
| Lógica de negócio no Controller | Move para Service |
| Reimplementar SGP4 do zero | Usar biblioteca NuGet existente |
| Resposta sem tipo definido | Todos os endpoints têm DTO de retorno tipado |

---

## Segurança (checklist antes de commit)

- [ ] Nenhuma API key ou connection string hardcoded
- [ ] KeepTrack API key lida de `IConfiguration` / env var
- [ ] Todos os inputs de query string validados
- [ ] Mensagens de erro não expõem stack trace em produção
- [ ] CORS configurado — aceita apenas origem do app Mobile (em dev: `*`)

---

## Qualidade

- Código funcionando + testes no mesmo ciclo de entrega
- Testes mínimo 80% de cobertura nos Services
- Fluxo TDD: RED → GREEN → REFACTOR
- Bug nos testes: corrige o código, não a asserção

---

## Estilo de Comunicação (IA → Gustavo)

- Resultado primeiro, explicação depois (se necessária)
- Sem preâmbulos ("Vou agora...", "Claro!")
- Código funcionando, nunca esqueletos com `// TODO`
- Português nas respostas; inglês nos identificadores de código

---

## Commits

```
<tipo>: <descrição>
```
Tipos: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`

---

## Bootstrap de Sessão

Em toda sessão nova, executar em paralelo:
1. `obsidian: vault_bootstrap`
2. `fenix: intelligence → memory_search, query="Mission Clear"` — time: **FIAP**
