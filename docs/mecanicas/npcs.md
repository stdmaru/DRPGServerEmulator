# NPCs e Diálogos

Evidências: `table.pf` (`npc_info_us`, `npc_function_us`, `npcdialogwindow_us`, `questdialog_us`, `shopdialog_us`), `DigimonRPG.bin` (`0x00AC59A6` → `0x00C9D020`) e `captures.db` (sessões 53, 56, 61 e 62).

## 1. Como o cliente trata NPCs ✅
- **Diálogos 100% client-side:** o cliente carrega localmente os **656 NPCs em 109 mapas** (`npc_info_us`, onde a coluna `0` é o `MapId`, coluna `1` é o `NpcId`, coluna `2` o nome e colunas `4`/`5` a posição `(X, Y)`), junto com seus textos de diálogo (`npcdialogwindow_us`: 2 565 linhas; `questdialog_us`: 159 linhas; `shopdialog_us`: 20 linhas).
- **O servidor só é acionado em ações efetivas**, e **cada sistema de NPC tem seu próprio protocolo/opcode**:
  - **Escolhas legadas (`Map 0x56`)**: tutorial da *Village of Beginning* (Mapa 79) e Nyang no *Digital Administration Bureau* (Mapa 1).
  - **Digimon Depository (`Map 0x89` / `0x8A` / `0x94`)**: ver [depository.md](depository.md).
  - **Quests modernas (`Map 0xF9`, etc.)** ❓: protocolo próprio por `questId` (observado em `S56 C121` com `questId = 23` e `46`).
  - **Lojas, Cura/Digimon Center, Buffs diários, Warp, Crafting, Reforço, Troca de Tamer** ❓: cada janela da UI envia seu opcode específico.

## 2. Protocolo `Map 0x56` (`NpcDialogChoice`) ✅
- **C2S:** 18 bytes (`u32 choiceId` + 14 zeros).
- **S2C:** 4 bytes (`u32 result`).
- Handler no cliente (`DigimonRPG.bin @ 0x00AC59A6`):
  - `1..8`: repassado à janela `0x89` (`0x00C9D020`) — tutorial da *Village of Beginning* (Mapa 79):
    - `1..4`: manuais (`22004..22007`);
    - `5`: Tunomon troca 5× `Yuramon's Seed` (`41`) por 1× `Tsunomon's Introduction Letter` (`22008`);
    - `8`: encerra tutorial, teleporta para o Mapa 1 `(58, 82)` e entrega `50× Woodmon's Leaf` (`74`) + `24171` e `24172`.
  - `10` (`0x0A`): janela `0x38` estado `0x0D` — Nyang (NPC 33, Mapa 1): entrega da *Tsunomon's Introduction Letter* (`22008`) em troca de `Teleport X` (`907`), `Crest of Courage +5` (`107`) e `Taming Net : Rookie X` (`10026`) (capturado em `S62 C234`).
  - `11` (`0x0B`): janela `0x38` estado `0x0E` ❓ (ainda sem captura).
  - `99` (`0x63`): resposta oficial de erro ou repetição — fecha a janela `0x38` (`S56 C121`, `S62 C234`).

## 3. Arquitetura no Emulador (`src/DRPGServer/Game/Npcs/`)
Para manter o código organizado conforme novas cidades e sistemas forem implementados:
- **Serviços genéricos de NPC** ficam separados por funcionalidade (ex.: `Depository`, e futuramente `Shops`, `Quests`, `NpcServices`).
- **Scripts de escolha (`0x56`)** ficam em `src/DRPGServer/Game/Npcs/`, agrupados **por mapa/cidade** herdando de `NpcScriptBase`:
  - `NpcScriptBase.cs`: helpers reutilizáveis sobre o `ItemService` (`GiveItemsOnce`, `ExchangeItemsOnce` com rollback atômico, `ReplyError(99)`).
  - `NpcScriptRegistry.cs`: coleta todos os `NpcScriptBase` via DI. Na partida recusa `choiceId` duplicado e escolha cujo NPC não existe no mapa do script. Antes de executar, o `InteractionGuard` confere que o jogador está perto do NPC da escolha ([seguranca.md](../emulador/seguranca.md#interação-e-alcance)).
  - Cada escolha é registrada com o NPC que a oferece: `RegisterChoice(choiceId, npcId, handler)`. ✅ Mapa 79: `1` Patamon (123), `2` Yukimi Botamon (124), `3` Mochimon (125), `4` Pyocomon (126), `5` Tunomon (127), `7`/`8` Elecmon (128). Mapa 1: `10` Nyang (33). Vindos da posição do jogador em cada escolha nas capturas.
  - `VillageOfBeginningNpcs.cs`: Mapa 79 (tutorial).
  - `DigitalBureauNpcs.cs`: Mapa 1 (Nyang).
