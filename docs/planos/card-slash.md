# Plano de Implementação — Sistema de Cards e Card Slash

> Documento de especificação técnica e arquitetura para revisão antes da implementação.
> Baseado nas capturas v322 (`captures.db`, sessões 64 / `conn=424, 428, 433`) e na tabela `table\card_info_us.dat` do cliente.

---

## 1. Descobertas Confirmadas (`✅`) e Pontos em Aberto (`❓`)

### 1.1 Tabela de Cards (`table\card_info_us.dat`) ✅
- Contém **469 cards**, 31 colunas separadas por `|`, com IDs próprios **independentes** de `item_info_us.dat`.
  - *(Correção importante no código atual: o antigo `BattleItemRecord` tratava o ID `74` como `Woodmon's Leaf` de `item_info_us.dat`, quando na aba Cards o ID `74` é **`Golden Drill X`** de `card_info_us.dat`.)*
- Colunas relevantes já mapeadas:
  | Col | Campo | Valores observados |
  |---:|---|---|
  | 0 | `Id` | `1..N` (469 entradas) |
  | 1 | `Name` | Nome em inglês (ex.: `Golden Drill X`, `Recovery Floppy L`, `Defense Plug-In C`) |
  | 2 | `Type` | `1` Modificador/Combate, `2` Recuperação (HP/EVP), `3` Especial/Sacrifício, `4` Utilitário/AoE/BR/Debuff, `5` Mudança de Atributo, `6` Data Corruption |
  | 3 | `SubType` | `1..5` |
  | 4–5, 25 | `Description` | Texto descritivo do efeito |
  | 12, 13 | `BuyPrice`, `SellPrice` | Preço de compra/venda |
  | 21 | `MaxStack` | Pilha máxima (geralmente `999`) |
  | 23 | **`TriggerTiming`** | **`1` = Ativação no Ataque (312 cards)**, **`2` = Ativação na Defesa (157 cards)** |
  | 24 | `Restriction` | `0` Normal, `1` PvE/Tutorial/Evento, `3` Battle Zone (PvP) |

### 1.2 Os 6 Card Slots Equipados no Inventário ✅
- Os cards precisam primeiro ser equipados em um dos **6 Card Slots** da janela de inventário para poderem ser selecionados em batalha.
- **Localização exata confirmada em `S2C 0x02` (`CharacterDataPacket`, 7600 bytes):**
  - Offsets **`784..1239`** = exatamente **`6 × 76 = 456 bytes`** (logo antes dos 5 blocos de status do time em `1240`!).
  - Confirmado em `conn=428` (`pid=339542`): `slot 0` em `784` (`Golden Drill X`, `uid=0x1B55`, `qty=50`), `slot 1` em `860` (`Recovery Floppy L`, `uid=0x2286`, `qty=2`).
- Os 24 slots da mochila de Cards ficam nos primeiros `24 × 76` bytes de `S2C 0x03` (`InventoryDataPacket`) e `S2C 0xFC` (`InventoryRefreshPacket`).
- **Movimentação / Equipar / Desequipar / Coleta de Cards (`C2S/S2C 0x90`) ✅:**
  - `C2S 0x90` (186 B) → `S2C 0x90` (172 B = `20 B` cabeçalho + `76 B` `rec1` + `76 B` `rec2`), paralelo ao `0x91` da aba Items (`ItemMoveHeader` com registros de 76 bytes):
    - `+0` (`u8` `fromPlace`), `+1` (`u8` `toPlace`), `+2..3` (`u16` 0):
      - `1` = Chão (coleta de card dropado; `+4/+8` = `X/Y`)
      - `2` = **6 Card Slots equipados** (`row = 0`, `col = 0..5`)
      - `3` = **Mochila de Cards** (24 slots: `row = 0..3`, `col = 0..5`)
    - `+4` (`i32` `fromRow`), `+8` (`i32` `fromCol`), `+12` (`i32` `toRow`), `+16` (`i32` `toCol`).
    - Confirmado em `conn=437, 442, 446`: `3→2` (equipar), `2→3` (desequipar), `2→2` (mover entre os 6 slots), `3→3` (mover na mochila) e `1→3` (coletar do chão).

### 1.3 Equipar / Desequipar nos 3 Slots de Batalha (`0xA0` / `0xA1`) ✅
- Durante a batalha, o jogador equipa até **3 cards** (`slot = 0, 1, 2`) a partir dos 6 Card Slots equipados.
- **`C2S 0xA0` (98 bytes)** — Equipar card no slot de batalha:
  - `+0` (`u32`): índice do slot (`0`, `1` ou `2`)
  - `+4` (`76 B`): `CardRecord` (`+0` UID, `+4` CardId, `+14` quantidade…)
  - `+80` (`18 B`): zeros
  - **Sem resposta S2C.**
- **`C2S 0xA1` (98 bytes)** — Desequipar card do slot de batalha:
  - Mesmo layout de 98 bytes (`slot` + `76 B CardRecord` + `18 zeros`), **sem resposta S2C.**

### 1.4 Ativação e Consumo no `S2C 0x67` ✅
Na entrada de 136 bytes de quem ativa cards em `S2C 0x67` (`ActionResultsPacket`):
- `+40`, `+44`, `+48` (`u32 × 3`): **CardId** ativado nos slots `0`, `1`, `2` (`0` se vazio)
- `+52`, `+56`, `+60` (`u32 × 3`): **quantidade restante** nos slots `0`, `1`, `2` após consumir 1 unidade
- Quando a quantidade chega a `0`, o card é consumido do slot de batalha e do Card Slot equipado do inventário; no turno seguinte aquele slot volta a `0`.
- **Cards de Defesa (`TriggerTiming = 2`) ✅ (`conn=446`, `pid=371167` / `371258`):**
  - Quando o inimigo ataca (`side = 1`), apenas os slots equipados com cards de **Defesa (`col 23 == 2`)** são ativados e gravados na entrada do **defensor** (slots `5..9` do `0x67`), preservando exatamente o índice do slot (`+40/+52` para slot 0, `+44/+56` para slot 1, `+48/+60` para slot 2) — mesmo se o ataque inimigo errar (`h = 3`), o card de defesa é consumido.
  - Quando o jogador ataca (`side = 2`), apenas os slots com cards de **Ataque (`col 23 == 1`)** são ativados e gravados na entrada do **atacante** (`s0..s4`), também preservando o índice do slot (ex.: slot 1 vai em `+44/+56` com `+40/+52` zerados).

### 1.5 Limite de 20 Card Slash por Batalha
- Cada batalha começa com **20 Card Slash** disponíveis por jogador.
- Cada turno em que **ao menos 1 card** é ativado decrementa o contador em `1` (`20 → 19 → … → 0`).
- Ao atingir `0`, nenhum card pode mais ser ativado naquela batalha (mesmo que ainda haja cards equipados nos slots `0..2`).
- ❓ Verificar se o cliente controla o contador visual apenas localmente a cada `0x67` com cards ativados ou se existe algum campo explícito em `0x67`/`0x63`.

### 1.6 Animação de Card Slash e Sincronização de Barras ✅
Medido em `conn=428` e `conn=433`:
- Sem card: `S2C 0x67 → C2S 0x69` leva **`2,9 s – 3,4 s`**.
- Com Card Slash (`1` ou `2` cards): `S2C 0x67 → C2S 0x69` leva **`3,94 s – 3,96 s`** (~+0,5–1,0 s pela animação do Card Slash).
- Atraso após `S2C 0x69` até o primeiro `0x6B`: **`160 – 346 ms`** (idêntico às ações sem card).
- **Conclusão:** como a `Battle` já trava todas as barras em `Phase.Animating` até receber o `C2S 0x69` do cliente (e só então aplica `ResumeDelay = 200 ms`), **não é necessário adicionar nenhum delay artificial no servidor** — a sincronização já acontece naturalmente pelo handshake `0x68 → 0x67 → C2S 0x69 → S2C 0x69`.

---

## 2. Arquitetura Proposta

### 2.1 Camada de Dados (`Data/GameData/`)
- **`CardTrigger` enum:** `Attack = 1`, `Defense = 2`.
- **`CardInfo` record:** `Id`, `Name`, `Type`, `SubType`, `Trigger`, `Restriction`, `BuyPrice`, `SellPrice`, `MaxStack`, `Description`.
- **`ClientTables.Cards(ClientPack)`:** lê `table\card_info_us.dat` e expõe em `GameData.Cards` / `GameData.FindCard(uint id)`.
- **`CardRecord` (76 bytes):** substitui o antigo `BattleItemRecord` hardcoded para usar `CardInfo` da tabela `card_info_us.dat`.

### 2.2 Inventário e Persistência (`Game/Entities/` + `Data/Repositories/` + `sql/upgrade/`)
- **`Inventory`:**
  - `Main` (24 slots — aba Items, registros de 108 B)
  - `Battle` (24 slots — aba Cards, registros de 76 B)
  - **`CardSlots`** (6 slots — Cards equipados para batalha, registros de 76 B)
- **Persistência SQL:** salvar `CardSlots` junto ao inventário usando um novo `BagKind.EquippedCards` (ou coluna existente `bag` na tabela de itens do personagem, sem quebrar contas antigas).
- **`CharacterDataPacket` (`0x02`):** escrever os 6 `CardRecord` (456 bytes) nos offsets **`784..1239`** em vez de zeros.
- **Handler `0x90` (`CardMoveHandler`):** mover/empilhar cards dentro da aba Cards e entre a aba Cards e os 6 `CardSlots`.

### 2.3 Estado de Batalha (`Game/Battle/`)
- Por jogador participante na `Battle`:
  - `int CardSlashesRemaining = 20;`
  - `InventoryItem?[] ActiveCards = new InventoryItem?[3];` (referências aos cards atualmente nos slots `0..2`, validando que o item pertence aos `6 CardSlots` equipados do jogador e tem `Amount > 0`).
- **Handlers `0xA0` e `0xA1`:**
  - `0xA0`: valida batalha ativa, `slot in 0..2`, card presente em `Inventory.CardSlots` (por UID + CardId), e registra em `ActiveCards[slot]`.
  - `0xA1`: valida `slot in 0..2` e limpa `ActiveCards[slot]`.
- **Resolução na `Battle.Attack`:**
  - **Ativação no Ataque (`actor` é jogador):**
    - Se `CardSlashesRemaining > 0`, coleta os slots `0..2` com card de `Trigger == CardTrigger.Attack` e `Amount > 0`.
    - Se houver ≥ 1 card ativável: decrementa `CardSlashesRemaining`, consome `1` unidade de cada card ativado (limpando o slot se `Amount == 0`), aplica os efeitos no contexto do ataque e grava `(cardId, remainingQty)` nos offsets `40..63` do `0x67`.
  - **Ativação na Defesa (`victim` é jogador sendo atacado por selvagem):**
    - Se `CardSlashesRemaining > 0`, coleta os slots `0..2` com card de `Trigger == CardTrigger.Defense` e `Amount > 0`.
    - Se houver ≥ 1 card ativável: decrementa `CardSlashesRemaining`, consome `1` unidade, aplica modificadores defensivos (redução de dano, bônus de DEF/BR, reflexão) e grava os dados no `0x67` *(layout exato no defensor a validar em 1 captura de card de defesa)*.
  - Ao fim da batalha, persiste as alterações do inventário (`owner.RequestImmediateSave()`).

### 2.4 Motor de Efeitos Extensível (`Game/Battle/Cards/`)
Em vez de um `switch` gigante em `Battle.cs`, usar uma estrutura modular dedicada:
- `CardAttackContext` / `CardDefenseContext`: encapsulam atacante, alvos, `DamageMultiplier`, `DefenseMultiplier`, `ForceAllTargets` (AoE), `OverrideAttackerAttribute`, `OverrideTargetAttribute`, `HealHp`, `HealVp`, `HealEvp`, `LifestealFraction`, `ReflectDamage`, etc.
- `ICardEffect` + `CardEffectRegistry`: mapeia categorias/IDs de `card_info_us.dat` para efeitos coesos e testáveis isoladamente:
  1. **Recuperação (`Type == 2`):** *Recovery Floppy* (+HP fixo), *EVP recovery* (% ou fixo).
  2. **Modificadores de Dano / ATK (`Type == 1, Trigger == Attack`):** *Offense Plug-In A/N*, *Boost Chip*, *Iron Claw*, etc.
  3. **Modificadores de Defesa (`Type == 1, Trigger == Defense`):** *Defense Plug-In C*, *Holy Orb* (−50 % dano), *White Wings* (−100 % dano), *Reverse Sevens* (reflete ataque).
  4. **AoE (`Golden Drill X` #74, `Steel Drill` #73, `The Wicked God's Advent` #42):** força `TargetCount = Battle.MaxPerSide` com 100 % de dano.
  5. **Mudança de Atributo (`Type == 5`):** *Training Manuals* (#8–10), *Evil Spiral* (#13).
  6. **BR / Velocidade (`Type == 4`):** *High Speed Plug-In B*, *Speed Sevens*, *Strength Plug-In P/W*.

---

## 3. Fases de Implementação (PRs Incrementais)

| Fase | Escopo | Pré-requisito de captura |
|---|---|---|
| **Fase 1** | `CardInfo` (`card_info_us.dat`), `CardRecord` (76 B), 6 `CardSlots` no `Inventory` + `0x02` (`784..1239`) + persistência + `0x90` (mover/equipar/desequipar/coletar cards) | 100 % capturado (`conn=437, 442, 446`) ✅ |
| **Fase 2** | Slots `0..2` em batalha (`0xA0`/`0xA1`), contador de `20` Card Slash por batalha, consumo de quantidade e escrita dos offsets `40..63` no `0x67` (atacante e defensor) | 100 % capturado (`conn=428, 433, 446`) ✅ |
| **Fase 3** | `CardEffectRegistry`: efeitos de **Ataque** (`col 23 == 1`: HP/VP/EVP, AoE, % dano, atributo) e **Defesa** (`col 23 == 2`: DEF/redução de dano/reflexão) | 100 % capturado (`conn=428, 446`) ✅ |
| **Fase 4** | Testes unitários completos (`CardSlashTests.cs`) + sincronia da documentação em `docs/mecanicas/batalha.md` e `docs/protocolo/structs/inventario.md` | — |

---

## 4. Checklist de Validação antes de começar a Fase 1
- [x] Capturar movimentação de card da aba Cards ↔ 6 Card Slots (`C2S/S2C 0x90`) — ✅ `conn=437, 442, 446`.
- [x] Capturar ativação de card de **Defesa** (`col 23 == 2`, `Defense Plug-In C` #5) em batalha — ✅ `conn=446`.
- [ ] Revisar e aprovar este plano para iniciar a Fase 1.
