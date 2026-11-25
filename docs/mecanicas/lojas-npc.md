# Lojas Genéricas de NPC (`0x0050`–`0x0053`)

Especificação das lojas genéricas de NPC (compra e venda por Bits), baseada nas capturas oficiais v322 (`captures.db`, sessões 56 e 62) e na engenharia reversa de `DigimonRPG.bin`.

---

## 1. Catálogo (`src/DRPGServer/Resources/Item/NpcShop.xml`)

Cada `<Shop>` possui:
- `ID`: identificador enviado pelo cliente em `C2S 0x0050` (cards) ou `C2S 0x0052` (itens).
- `Kind`: `Card` (`0x0050`/`0x0051`) ou `Item` (`0x0052`/`0x0053`).
- `Currency` *(opcional, padrão `Bits`)*: `Bits`, `Medals`, `Coins` ou `LadderPoints` (confirmado em `table\textattach_us.dat` strings `3545..3549` e `DigimonRPG.bin @ 0x00AEEC90..0x00AEF238` / `0x00B131EA..0x00B132E0`):
  - `Bits` (`action=1`): preço em `ItemRecord +20` (`BuyPrice`), saldo em `double [player+0x12F0]`.
  - `Medals` (`action=3`): preço em `ItemRecord +88` (`u16`), saldo em `WORD [player+0x4070]`.
  - `Coins` (`action=4`, loja `5050`): preço em `ItemRecord +90` (`u16` = `Col16`), saldo em `WORD [player+0x4072]` (máx. 20.000).
  - `LadderPoints` (`action=5`): preço em `ItemRecord +92` (`u32` = `Col17`), saldo em `DWORD [player+0x12B0]`.
- Até 40 `<Entry ID="..." [Price="..."] />`. Quando `Price` é omitido, o servidor usa o campo de preço correspondente na tabela do cliente.

### `Map 0x00CB` — Atualizar Moedas (`S2C`, 16 B) ✅
- **Evidência**: `DigimonRPG.bin @ 0x00ABFAC2`.
- `+0x00` (`double`): `Bits`; `+0x08` (`u32`): `Medals`; `+0x0C` (`u32`): `Coins`.

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
- **Evidência**: `captures.db` (conn 145/221) + `DigimonRPG.bin @ 0x00ABF47A / 0x00AEEC90`.
- **Tamanho S2C**: `144` bytes (`C2S` 158 B).
  - `+0x00` (`u32`): `action` (`1` = Comprar, `2` = Vender)
  - `+0x04..+0x6F` (`108 B`): `ItemRecord` (zerado quando a venda esvazia o slot)
  - `+0x70` (`u32`): `quantity`
  - `+0x74` (`u32`): `0`
  - `+0x78` (`double`): Bits restantes do personagem
  - `+0x80` (`u16`): `0`
  - `+0x82` (`u16`): `place` (`3` = aba Items)
  - `+0x84` (`i32`): `col` (`slot % 6`)
  - `+0x88` (`i32`): `row` (`slot / 6`)
  - `+0x8C` (`u32`): `shopId`
- Na compra (`action == 1`), seguido de `S2C 0x00C9` (`ItemReceivePacket`).
