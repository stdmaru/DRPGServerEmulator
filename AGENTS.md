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
  - **Handlers** ficam separados por domínio em `src/DRPGServer/Network/Handlers/<Servidor>/` (ex.: `Map/ItemHandlers.cs`, `Map/BattleHandlers.cs`, `Map/NpcShopHandlers.cs`). Domínio novo ganha arquivo novo.
  - **Serviços** ficam um por domínio em `src/DRPGServer/Game/<Domínio>/` ([onde fica cada coisa](docs/emulador/arquitetura.md#onde-fica-cada-coisa)).
  - **Testes unitários** ficam separados por domínio em `tests/DRPGServer.Tests/` (`BattleTests.cs`, `FormulaTests.cs`, `DigimonTests.cs`, `WorldTests.cs`, `ClientTablesTests.cs`, `TutorialTests.cs`, `CaptureTests.cs`, etc.).
- **Camadas** (detalhes em [`docs/emulador/arquitetura.md`](docs/emulador/arquitetura.md#camadas)):
  - **Pacotes** (`Network/Packets`) só descrevem layout: S2C em `ServerPacket`, C2S em request tipado (`IClientPacket<T>`) com tamanho declarado. Handler nunca lê byte nem usa offset.
  - **Handlers** (`Network/Handlers`) recebem o request tipado, declaram pré-condições (`[RequiresState]`), chamam **um** serviço e enviam a resposta. Nada de preço, inventário, moeda ou banco no handler.
  - **Serviços** (`Game/<Domínio>`) têm as regras e a orquestração.
  - **Entidades** (`Game/Entities`) guardam estado e invariantes, sem I/O e sem rede.
  - **Dados** (`Data/`) guardam repositórios e transações.
- **Thread única do mundo (`GameWorld`):** toda leitura e escrita de estado de jogo (zonas, jogadores, batalhas) ocorre exclusivamente na thread do mundo.
  - I/O a partir do mundo passa **só** por `WorldIo`.
  - Gravações passam pela `PersistenceQueue`.
  - É proibido `_ = AlgoAsync()`, `Task` solta e callback de banco escrito à mão.
- **Dependências obrigatórias:** nada de parâmetro `= null` para "funcionar sem" um serviço. Os testes usam fakes em `TestSupport`, nunca um caminho alternativo no código de produção.
- **Tempo:** calendário via `IGameClock`, tempo de jogo via `GameWorld.Now`. Nunca `DateTimeOffset.UtcNow` direto.

### Reuso e Extensibilidade
- **Uma regra, um lugar.** Antes de escrever lógica de jogo, procure o serviço do domínio ([onde fica cada coisa](docs/emulador/arquitetura.md#onde-fica-cada-coisa)). Lógica usada por mais de uma feature **é** do serviço:
  - dar, tirar, trocar item e mandar para o depósito da conta: `ItemService`;
  - gastar e creditar moeda: `Wallet`;
  - efeitos de equipar, desequipar e expirar: `EquipmentService`;
  - bônus de status: `StatModifiers`;
  - efeito de uso de item ou card: efeito tipado registrado.
- **Duplicou, extraia.** Se a mesma sequência (de pacotes, validações ou cálculo) aparece em dois lugares, ela vira um método do serviço no mesmo PR.
- **Tipos no lugar de tuplas e valores mágicos.** Use record com nome (`ItemGrant`, `ItemDuration`), não `(uint, int)` com um `-1` que significa "permanente".
- **Implementar já do jeito certo.** Peça nova entra com o desenho final, mesmo com um uso só. Não vale "por ora assim, depois fazemos direito".
- **Extensão por dados e registro, não por `switch`.** Comportamento novo de item, card ou título é um efeito tipado novo, não um `case` com id.

---

## 3. Protocolo, Evidências e Documentação

- **Marcação de confiança nos comentários e em `docs/`:**
  - `✅` comportamento confirmado nas capturas oficiais (`captures.db`) ou no cliente.
  - `❓` comportamento inferido, parcial ou ainda em aberto.
- **Sincronia obrigatória:** ao confirmar ou corrigir uma fórmula, layout de pacote ou mecânica, atualize no mesmo PR:
  1. O código em `src/DRPGServer/`.
  2. Os testes unitários em `tests/DRPGServer.Tests/`.
  3. A documentação correspondente em `docs/`.

---

## 4. Dados de Jogo

Regras completas em [`docs/emulador/dados.md`](docs/emulador/dados.md).
- **Texto de UI nunca define dado de jogo.** Não leia descrição, resumo ou nome (com regex ou qualquer parser) para extrair atributo, efeito, preço ou regra. Se o dado só existe como texto, gere XML estruturado **offline** com uma ferramenta versionada em `tools/`, revise e commite. O servidor só lê XML.
- **Sem mini-linguagem em string** nos XML (`OnUse="HealHP(150)"`): use elementos e atributos tipados (`<Effect Type="HealHp" Amount="150" />`).
- **Coluna do cliente tem nome de domínio** (`CoinPrice`, não `Col16`). Desconhecida = `UnknownColNN`, com o que se sabe no comentário.
- **Fail-fast na carga:** referência quebrada, valor fora do intervalo ou atributo desconhecido derrubam a partida com uma mensagem clara.
- **Sem número mágico de jogo no código:** preço, chance e duração vêm de tabela ou XML. Constante de protocolo confirmada (✅) vira `const` com nome e evidência.

---

## 5. Segurança e Validação Server-Side

Modelo de ameaça e controles em [`docs/emulador/seguranca.md`](docs/emulador/seguranca.md). **Feature faltando não é motivo para controle faltando:** código novo já nasce com os controles abaixo.

**Nunca confie em dados vindos do cliente.** Todo handler de pacote (`C2S`) deve assumir que o pacote pode ser forjado, repetido, truncado ou enviado fora de ordem, aplicando guards explícitos no servidor antes de alterar qualquer estado de jogo:

- **Propriedade e origem do recurso:**
  - Sempre valide que o `Uid` / `Serial` pertence ao jogador autenticado e está na bolsa, mapa ou batalha esperada (ex.: cards equipáveis em batalha só podem vir dos `CardSlots` do próprio jogador, nunca da mochila comum ou de outro jogador).
  - Confira todos os identificadores enviados pelo cliente contra o estado autoritativo do servidor (`Uid` + `ItemId`/`CardId` + tipo de drop/bolsa), impedindo conversão indevida entre tipos (ex.: item comum coletado como card).
- **Estado e pré-condições:**
  - Bloqueie operações incompatíveis com o estado atual (ex.: movimentar/destruir itens ou cards durante batalha, agir em batalha encerrada, interagir com NPC de outro mapa, executar comandos sem `Authority` suficiente).
- **Limites, índices e anti-duplicação:**
  - Valide limites de índices (`slot`, `row`, `col`, `bag`, `amount`) **antes** de casts de tipos sem sinal (`uint` → `int`) para evitar overflow/índices negativos.
  - Impeça duplicação de referências ou consumo sem saldo (ex.: mesma pilha equipada em múltiplos slots de batalha, troca atômica sem espaço, consumo de quantidade maior que o disponível).
  - Revalide a existência e quantidade do recurso no momento efetivo do uso/consumo (não apenas ao selecionar/equipar) e acione `RequestImmediateSave()` em operações críticas (itens, captura, batalha, depository, NPC).
- **Cobertura em testes:**
  - Todo novo handler ou mecânica deve incluir testes unitários exercitando entradas inválidas (índices fora do intervalo, IDs trocados, duplicação, estado inválido ou falta de permissão).
- **Alcance e contexto:** ação com alvo no mundo (NPC, loja, Depository, selvagem, drop) passa pelo `InteractionGuard`: mesmo mapa, dentro da distância e, para drop, dono durante a janela de exclusividade.
- **Conta × personagem:** mover moeda ou item entre a conta (Cash, depósitos) e o personagem é sempre uma **transferência** (`ItemService`/`Wallet`), gravada numa transação só, com rollback em memória. Nunca debite em memória e grave o outro lado por fora.
- **Auditoria:** movimento de moeda ou item entre fontes gera uma linha no `IAuditLog`, na mesma transação.
- **Segredos:** nunca registre `auth_key`, hash ou dado de autenticação no log. Dado do cliente no log sempre com limite de tamanho.

### Checklist de handler novo
Detalhes em [`docs/emulador/handlers.md`](docs/emulador/handlers.md#checklist-de-handler-novo).
- [ ] Request tipado com tamanho declarado e ✅/❓ da evidência.
- [ ] Pré-condição de estado declarada (`[RequiresState]`).
- [ ] Ids do cliente conferidos contra o estado do servidor (dono, bolsa, mapa, batalha).
- [ ] Alcance pelo `InteractionGuard` quando há alvo no mundo.
- [ ] Regra no serviço do domínio, reaproveitando `ItemService`, `Wallet` e `EquipmentService`.
- [ ] Movimento de economia auditado.
- [ ] Testes com entrada hostil: tamanho errado, id trocado, índice fora do intervalo, estado errado, longe do alvo.
