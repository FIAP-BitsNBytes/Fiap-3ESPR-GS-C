# Prompt Padrão — Implementar Plano

> Copie este prompt, substitua `{{plan-01-database}}` pelo caminho do plano (ex: `plan-03-orbital`) e envie.

---

## PROMPT

```
Implemente o plano `docs/superpowers/plans/{{plan-01-database}}.md` **completo e funcionando**.

## Contexto obrigatório — leia ANTES de tocar qualquer arquivo

1. `CLAUDE.md` — regras do projeto, stack, não-negociáveis
2. `docs/API_CONTRACT.md` — contratos de resposta e campos (fonte de verdade)
3. `docs/superpowers/plans/architecture-overview.md` — mapa de arquivos e módulos
4. `docs/superpowers/plans/{{plan-01-database}}.md` — o que implementar (siga à risca)

## Regras de execução

### Antes de criar qualquer arquivo
- Leia os arquivos de contexto acima na íntegra
- Confirme que os tipos/interfaces que você vai consumir já existem (plans anteriores)
- Se um tipo ainda não existe, pare e sinalize — nunca invente tipos ou interfaces

### Implementação
- Execute **cada task do plano em ordem**, checkbox por checkbox
- Código funcionando desde o primeiro commit — nenhum `// TODO`, nenhum stub vazio
- Toda interface nova em `Services/Interfaces/` antes da implementação
- Toda implementação injeta dependências via construtor, nunca instancia diretamente
- `Program.cs` → **não editar** (exclusivo do plan-07), a menos que o plano seja plan-07
- `.csproj` → **não editar** (exclusivo do plan-00), a menos que o plano seja plan-00
- `appsettings.Development.json` → **não editar** (exclusivo do plan-01), a menos que o plano seja plan-01
- Siga `JsonNamingPolicy.SnakeCaseLower` — sem `[JsonPropertyName]` exceto em `ByAltitudeBandDto`
- IDs: `usr_{Guid:N}`, `msn_{Guid:N}`, `sess_{Guid:N}` — formato exato
- Timestamps: `DateTime.UtcNow.ToString("O")` — ISO 8601 UTC

### Testes — "Test, Break, Fix"
Escreva os testes ANTES da implementação (TDD):

**Por cada componente implementado:**
1. Escreva teste que FALHA (RED) — failure cases primeiro, happy path por último
2. Rode: `dotnet test --filter "<NomeDoTeste>"` → confirme RED
3. Implemente mínimo para passar (GREEN)
4. Rode novamente → confirme GREEN
5. Refatore se necessário

**Tipos de teste obrigatórios:**
- **Unitário de função:** testa cada método público isolado (Moq para dependências)
- **Unitário de fluxo:** testa sequência completa de um caso de uso (ex: register → BCrypt → save → return AuthResponse)
- **Edge cases:** null/empty input, não encontrado, conflito (409), expirado

**Estrutura dos testes (AAA obrigatório):**
```csharp
[Fact]
public async Task NomeClaroDoCenario_QuandoCondicao_RetornaOuLanca()
{
    // Arrange
    var mock = new Mock<IDependencia>();
    mock.Setup(...).ReturnsAsync(...);
    var sut = new ServicoTestado(mock.Object);

    // Act
    var result = await sut.MetodoTestado(input);

    // Assert
    result.Should().Be(expected);
    mock.Verify(..., Times.Once);
}
```

**Cobertura mínima:** 80% nos Services implementados neste plano.

### Erros
- Nunca `catch {}` vazio — sempre re-throw ou `ILogger.LogError` + re-throw
- Nunca `Console.WriteLine` — usar `ILogger<T>`
- Nunca `BadRequest(new { error = "..." })` direto — usar `throw new DomainException("CODIGO", "msg", httpStatus)`
- `DomainException` é definida em `MissionClear.Api/Exceptions/DomainException.cs` (plan-02) — não redefinir

### Build
Rode ao final de cada task:
```powershell
dotnet build MissionClear.sln
```
Resultado esperado: `Build succeeded. 0 Error(s).`

Se build falhar: pare, corrija, não avance para a próxima task.

## Critérios de conclusão

Só declare o plano concluído quando:
- [ ] Todos os checkboxes do plano marcados
- [ ] `dotnet build MissionClear.sln` → 0 erros, 0 warnings relevantes
- [ ] `dotnet test` → todos os testes do plano passando (GREEN)
- [ ] Cobertura ≥ 80% nos Services deste plano
- [ ] Nenhum arquivo compartilhado editado indevidamente (Program.cs, .csproj, appsettings.Development.json)
- [ ] Commit atômico por task (mensagem conventional-commit)

## Commit padrão por task

```
feat(<modulo>): <descrição do que foi implementado>

- <detalhe 1>
- <detalhe 2>
```

Tipos: feat, fix, refactor, test, chore
```

---

## Exemplo de uso

**Plan 03 (orbital):**
```
Implemente o plano `docs/superpowers/plans/plan-03-orbital.md` completo e funcionando.
[...resto do prompt acima...]
```

**Plan 04 (auth):**
```
Implemente o plano `docs/superpowers/plans/plan-04-auth.md` completo e funcionando.
[...resto do prompt acima...]
```

---

## Variáveis de substituição

| Variável | Valor exemplo |
|---|---|
| `{{plan-01-database}}` | `plan-03-orbital` |
| `{{plan-01-database}}` | `plan-04-auth` |
| `{{plan-01-database}}` | `plan-05-simulation` |
| `{{plan-01-database}}` | `plan-06-history-dashboard` |
| `{{plan-01-database}}` | `plan-07-controllers` |
| `{{plan-01-database}}` | `plan-08-mvc-web` |

> Plans 00, 01 e 02 têm regras especiais de arquivo exclusivo — leia o `parallel-execution-guide.md` antes de rodar em paralelo.
