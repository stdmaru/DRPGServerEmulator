# Inventário: 0x03 e registro de item

Evidência: `captures.db`, sessões 52–56 (v322), 20 pacotes `0x03` S2C de 4416 bytes (um por entrada no mapa).

## 0x03 S2C (4416 bytes) ✅
`24 × 76` + `24 × 108` = 1824 + 2592.

| off | conteúdo |
|---|---|
| 0 | 24 registros de 76 bytes. ❓ Bolsa de itens de evolução: aparecem Woodmon's Leaf (74, sempre no 1º), Digimental of Hope (19), Crest of Friendship (128). Flags em +64 (`0F 27 00 7D 40 42 0F`), sem checagem. |
| 1824 | 24 registros de item (108 bytes), pela posição no inventário (6 colunas). Vazio = zeros. |

## Registro de item (108 bytes) ✅
O mesmo registro aparece no `0x97` (item recebido: 12 bytes, `u32` posição, registro, zeros).

| off | tipo | campo |
|---|---|---|
| 0 | u32 | uid do item |
| 4 | u32 | id do item (`item_info_us` do cliente) |
| 8 | u8, u8 | ❓ tipo (`0C 02` carta/livro, `05 01` semente, `07 06` consumível, `0B 03`/`0C 03` …) |
| 12 | u32 | quantidade (sementes: 3 → 2 ao entregar) |
| 16 | u32… | ❓ limites e preço: `E7 03` (999), `E8 03` (1000), `64` (100), `10 27` (10000) |
| 88 | 12 B | flags (`0F 27 00 7D 40 42 0F 00 …`) |
| 100 | u32 | checagem = uid + id do item ✅ |
| 104 | u32 | 0 |

Consumíveis trazem pares `(u32 id de efeito, u32 valor)` entre +44 e +88 (ex.: `91 01` / `0A`, `92 01` / `64`). ❓ Os bytes de +8 a +99 vêm da tabela de itens do oficial, que o emulador ainda não tem (só ID e nome).

## No emulador
`Network/Packets/Map/ItemPackets.cs` (`ItemRecord`, `InventoryDataPacket`, `ItemReceivePacket`). O inventário **não é persistido**, então o `0x03` sai vazio na entrada. A bolsa de 76 bytes ainda não existe. Teste: `PacketSizeTests.Inventory_items_go_to_their_slot_with_amount_and_check`.
