# AGENTS.md — Guia de Desenvolvimento e Contribuição

Este arquivo define as diretrizes de fluxo de trabalho, organização de código e qualidade para agentes e desenvolvedores que atuam no **DRPGServer**.

---

## 1. Fluxo de Trabalho via Pull Requests (PRs)

- **Nunca commitar ou fazer push diretamente na `main`.** Toda alteração deve passar por uma branch dedicada e um Pull Request.
- **Convenção de branches:**
  - `fix/<assunto-curto>` — correções de bugs ou divergências em relação ao oficial.
  - `feat/<assunto-curto>` — novas mecânicas, handlers ou pacotes.
  - `refactor/<assunto-curto>` — reorganização estrutural sem mudança de comportamento.
  - `docs/<assunto-curto>` — documentação pura.
- **Base da branch:** sempre partir da branch base atual (`main`, ou a branch de integração indicada pelo mantenedor).
- **Descrição do PR:**
  - Resumo objetivo do que muda e por quê.
  - Diagnóstico técnico com evidências (capturas v322 em `captures.db`, tabelas do cliente ou engenharia reversa quando aplicável).
  - Lista de alterações e checklist de validação.
- **Validação obrigatória antes de abrir/atualizar PR:**
  - Rodar toda a suíte de testes unitários com `dotnet test` (caso `obj/` ou `bin/` tenham permissões restritas no ambiente local, usar `dotnet test --artifacts-path /tmp/dotnet-drpg`).
  - Zero erros de compilação e 100% dos testes passando.
- **Testes locais antes do push:** quando o mantenedor pedir para testar localmente antes de subir, aplicar as mudanças sem fazer `git push` até receber confirmação.

---

## 2. Organização, Legibilidade e Clareza do Código

### Clareza acima de "esperteza"
- **Prefira código explícito, direto e óbvio** a truques matemáticos, expressões compactas demais ou ternários aninhados.
- Se um mapeamento tem poucos casos fixos (ex.: estágios de evolução, níveis de desbloqueio, atributos), **escreva cada caso explicitamente** em um `switch` legível em vez de derivar fórmulas artificiais.

Exemplo — **evite**:
```csharp
public static int StageLevel(byte stage) => stage switch
{
    <= InTraining => 1,
    Rookie => 11,
    _ => 21 + 10 * (stage - Champion),
};
```

Exemplo — **prefira**:
```csharp
public static int StageLevel(byte stage) => stage switch
{
    InTraining => 1,
    Rookie => 11,
    Champion => 21,
    Ultimate => 31,
    Mega => 41,
    _ => 1,
};
```

- **Evite ternários aninhados** (`a ? b : c ? d : e`): use `switch` ou `if`/`return` explícitos.
- **Nomes descritivos:** evite siglas obscuras em variáveis locais quando um nome claro facilita a leitura.

### Responsabilidade Única (SRP) e Coesão
- **Um domínio por arquivo:** não agrupe dezenas de classes de áreas diferentes em um único arquivo monolítico.
  - **Handlers de mapa** ficam separados por domínio em `src/DRPGServer/Network/Handlers/Map/`:
    - `MapSessionHandlers.cs` (entrada, movimento, chat, saída)
    - `DigimonHandlers.cs` (pontos, skills, evolução, time, renomeação)
    - `ItemHandlers.cs` (coleta, movimento, descarte, organização, uso)
    - `NpcHandlers.cs` (diálogos de NPC)
    - `BattleHandlers.cs` (início, confirmação, pronto, ações de batalha)
  - **Testes unitários** ficam separados por domínio em `tests/DRPGServer.Tests/` (`BattleTests.cs`, `FormulaTests.cs`, `DigimonTests.cs`, `WorldTests.cs`, `ClientTablesTests.cs`, `TutorialTests.cs`, `CaptureTests.cs`, etc.).
- **Separação entre rede e regra de negócio:** handlers devem apenas ler o pacote, validar pré-condições básicas, delegar para entidades/sistemas em `Game/` e enviar a resposta.
- **Thread única do mundo (`GameWorld`):** toda leitura/escrita de estado de jogo (zonas, jogadores, batalhas) ocorre exclusivamente na thread do mundo; I/O de banco roda fora dela via `PersistenceQueue`.

---

## 3. Protocolo, Evidências e Documentação

- **Marcação de confiança nos comentários e em `docs/`:**
  - `✅` comportamento confirmado nas capturas oficiais (`captures.db`) ou no cliente.
  - `❓` comportamento inferido, parcial ou ainda em aberto.
- **Sincronia obrigatória:** ao confirmar ou corrigir uma fórmula, layout de pacote ou mecânica, atualize no mesmo PR:
  1. O código em `src/DRPGServer/`.
  2. Os testes unitários em `tests/DRPGServer.Tests/`.
  3. A documentação correspondente em `docs/`.
