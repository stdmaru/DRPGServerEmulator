# Lojas Genéricas de NPC (`0x0050`–`0x0053`)

Especificação das lojas genéricas de NPC (compra e venda por Bits), baseada nas capturas oficiais v322 (`captures.db`, sessões 56 e 62) e na engenharia reversa de `DigimonRPG.bin`.

---

## 1. Catálogo (`src/DRPGServer/Resources/Item/NpcShop.xml`)

Cada `<Shop>` possui:
- `ID`: identificador enviado pelo cliente em `C2S 0x0050` (cards) ou `C2S 0x0052` (itens).
- `Kind`: `Card` (`0x0050`/`0x0051`) ou `Item` (`0x0052`/`0x0053`).
- `Currency` *(opcional, padrão `Bits`)*: `Bits`, `Medals`, `Coins` ou `LadderPoints` (confirmado em `table\textattach_us.dat` strings `3545..3549` e `DigimonRPG.bin @ 0x00AEEC90..0x00AEF238` / `0x00B131EA..0x00B132E0`):
  - `Bits` (`action=1`): preço em `ItemRecord +20` (`BuyPrice`), saldo em `double [player+0x12F0]`.
  - `Medals` (`action=3`): saldo em `WORD [player+0x4070]`. ❓ O cliente lê o preço de `ItemRecord +88` (`u16`), mas a tabela não tem coluna de preço em Medals: nas capturas, `+88` vale 9999 ou 32000 sem seguir o tipo (coluna 13 = 6 bate com 32000 em quase todos).
  - `Coins` (`action=4`, loja `5050`): preço em `ItemRecord +90` (`u16` = `CoinPrice`, coluna 15), saldo em `WORD [player+0x4072]` (máx. 20.000).
  - `LadderPoints` (`action=5`): preço em `ItemRecord +92` (`u32` = `LadderPrice`, coluna 16), saldo em `DWORD [player+0x12B0]`.
- `DeliverTo` *(opcional, padrão `Inventory`)*: `Inventory` (entrega na mochila + `S2C 0x00C9`) ou `ShopDepository` (deposita em `cash_depository_item` sem enviar `0x00C9`; usado pela loja `5050` de Coins, onde o cliente exibe `"The purchased item has been delivered to the Shop Depository"`, `DigimonRPG.bin @ 0x00AEEE49..0x00AEF199`).
- Até 40 `<Entry ID="..." [Price="..."] />`. Quando `Price` é omitido, o servidor usa o campo de preço correspondente na tabela do cliente.

### `Map 0x00CB` — Atualizar Moedas (`S2C`, 16 B) ✅
- **Evidência**: `DigimonRPG.bin @ 0x00ABFAC2`.
- `+0x00` (`double`): `Bits`; `+0x08` (`u32`): `Medals`; `+0x0C` (`u32`): `Coins`.
- Na entrada do mapa (`S2C 0x02` `CharacterDataPacket`, `DigimonRPG.bin @ 0x00B5C028..0x00B5C126`), as 4 moedas são carregadas diretamente: `Bits` (`+0x40`), `LadderPoints` (`+0x5C`, `u32`), `Medals` (`+0x19A8`, `u16`) e `Coins` (`+0x19AA`, `u16`).

Cada loja precisa de um NPC dono em `npc_info_us` (coluna `10` = janela `12` cards / `13` itens, coluna `12` = `shopId`); o servidor não sobe sem ele. Os 4 handlers só atendem com o jogador perto do dono ([seguranca.md](../emulador/seguranca.md#interação-e-alcance)). Donos ✅: `1101` Valkyrimon (1:286), `5050` Prince Mamemon (5:139), `5101` Kaori (5:3), `5201` Hyori (5:2), `13602` Mimi's Dad (13:65), `40101` Gekomon (40:12), `40201` Gekomon (40:11).

Lojas capturadas incluídas inicialmente:
- **Itens (`0x0052`)**: `1101` (Freyja/Digital Anvil), `5050` (Loja especial de **Coins**), `5101` (Digital Administration Bureau), `13602` (Símbolos e roupas Beginner), `40101` (Gekomon Village).
- **Cards (`0x0050`)**: `5201` (Digital Administration Bureau), `40201` (Gekomon Village).

---

## 2. Protocolo

### `Map 0x0050` — Abrir Loja de Cards ✅
- **Evidência**: `captures.db` (conn 221/226/234) + `DigimonRPG.bin @ 0x00ABF37E`.
- `C2S` (18 B): `+0x00` `u32 shopId`.
- `S2C` (3040 B = `40 × 76 B`): 40 `CardRecord`s (`uid = 0`, `amount = card.MaxStack`), slots vazios zerados.

### `Map 0x0052` — Abrir Loja de Itens ✅
- **Evidência**: `captures.db` (conn 145/150/158/221) + `DigimonRPG.bin @ 0x00ABF416`.
- `C2S` (18 B): `+0x00` `u32 shopId`.
- `S2C` (4320 B = `40 × 108 B`): 40 `ItemRecord`s (`uid = 0`, `amount = item.MaxStack` ou `ExpiresAt = now + DurationSeconds` para itens de período), slots vazios zerados.

### `Map 0x0051` — Comprar / Vender Card ✅
- **Evidência**: `DigimonRPG.bin @ 0x00ABF3E1 / 0x00AEE700`.
- **Tamanho S2C**: `112` bytes (`C2S` 126 B).
  - `+0x00` (`u32`): `action` (`1` = Comprar, `2` = Vender)
  - `+0x04..+0x4F` (`76 B`): `CardRecord` (zerado quando a venda esvazia o slot)
  - `+0x50` (`u32`): `quantity`
  - `+0x54` (`u32`): `0`
  - `+0x58` (`double`): Bits restantes do personagem
  - `+0x60` (`u16`): `place` (`3` = aba Cards)
  - `+0x62` (`u16`): `0`
  - `+0x64` (`i32`): `col` (`slot % 6`)
  - `+0x68` (`i32`): `row` (`slot / 6`)
  - `+0x6C` (`u32`): `shopId`
- Na compra (`action == 1`), seguido de `S2C 0x00C8` (`BattleItemReceivePacket`).

### `Map 0x0053` — Comprar / Vender Item ✅
- **Evidência**: `captures.db` (conn 145/221) + `DigimonRPG.bin @ 0x00ABF47A / 0x00ADD1CB / 0x00AEEC90`.
- **Tamanho S2C**: `144` bytes (`C2S` 158 B).
  - Em `C2S`, o cliente sempre envia `action = 1` (Comprar, independente da moeda da loja) ou `2` (Vender). O servidor resolve a moeda pelo `shopId` e devolve no `S2C` o `action` correspondente (`1` = Bits, `3` = Medals, `4` = Coins, `5` = LadderPoints).
  - `+0x00` (`u32`): `action` (`1` = Bits, `2` = Vender, `3` = Medals, `4` = Coins, `5` = LadderPoints)
  - `+0x04..+0x6F` (`108 B`): `ItemRecord` (zerado quando a venda esvazia o slot)
  - `+0x70` (`u32`): `quantity`
  - `+0x74` (`u32`): `0`
  - `+0x78` (`double`): Bits restantes do personagem
  - `+0x80` (`u16`): Coins restantes do personagem (quando `action == 4`)
  - `+0x82` (`u16`): `place` (`3` = aba Items)
  - `+0x84` (`i32`): `col` (`slot % 6`)
  - `+0x88` (`i32`): `row` (`slot / 6`)
  - `+0x8C` (`u32`): `shopId`
- Na compra, seguido de `S2C 0x00C9` (`ItemReceivePacket`) e, para `Medals`/`Coins`, `S2C 0x00CB` (`CurrencyUpdatePacket`).

## 3. No emulador
- `Game/Shop/NpcShopService.cs` faz compra e venda igual para cards (`0x51`) e itens (`0x53`). Os handlers só leem o pedido, conferem a distância do dono (`InteractionGuard.CanUseShop`) e chamam o serviço.
- **Preço:** o `Price` da loja, se houver. Senão, pela moeda da loja: Bits `BuyPrice`, Coins `CoinPrice`, Ladder `LadderPrice`. Card só tem preço em Bits. Loja de Medals precisa de `Price` em toda entrada (a carga recusa sem ele), porque a tabela não tem preço em Medals.
- **Pagamento** pela `Wallet` (moeda da loja), **entrega** pelo `ItemService` (mochila, ou Shop Depository como transferência atômica) e **venda** por `ItemService.TakeAt`, sempre em Bits pelo `SellPrice`. Pedido inválido (quantidade 0 ou acima de 999, item fora da loja, sem saldo, sem espaço, item vencido ou de outra posição) é ignorado sem resposta.
