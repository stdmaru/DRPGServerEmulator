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

## Mover e destruir ✅ (sessões 53 e 56)
Cabeçalho comum de 20 bytes: `u8 origem`, `u8 destino` (1 chão, 3 inventário, 0x33/0x3D bolsas extras ❓), `u16 0`,
`i32 × 2` origem e `i32 × 2` destino (linha e coluna; x e y quando é o chão; o pedido de pegar do chão manda destino 4, 6 = "qualquer lugar").

| Pacote | C2S | S2C | Conteúdo |
|---|---|---|---|
| `0x91` mover | 250 | 236 | cabeçalho + registro do item (108) + 108 zeros. Do chão: o oficial manda `0x13` zerado (item sai do chão) e responde com a posição usada |
| `0xBD` jogar no chão | 254 | 240 | eco do cabeçalho e do registro, 108 zeros, `u32 1`. ✅ O item é destruído (não fica no chão) |
| `0xC9` receber | — | 604 | `u32 n` (≤ 5) + n × (`u32 3`, `u32 linha`, `u32 coluna`, registro 108) com o estado novo da posição (posição esvaziada = registro zerado) |
| `0xC8` receber na bolsa de 76 | — | 444 | como o `0xC9`, com registros de 76 (Woodmon's Leaf × 50 no fim do tutorial, uid 0) |
| `0xFC` | — | 4424 | `f64 bits` + as duas bolsas como no `0x03`. ✅ Sempre depois de um `0xC9`/`0xC8` |

O C2S tem sempre 14 bytes a mais que o S2C correspondente (zeros no fim).

## No emulador
`Game/Entities/Inventory.cs` (duas bolsas de 24: `Main` e `Battle`, pilha pela coluna 27 do `item_info_us`) e
`Network/Packets/Map/ItemPackets.cs`. ✅ Persistido em `inventory_item` (migração `0003`). Handlers `ItemMoveHandler` (`0x91`:
chão → inventário e inventário → inventário, trocando se ocupado) e `ItemDestroyHandler` (`0xBD`). ❓ Bolsas extras, segundo
registro do `0x91` numa troca (zero nas capturas) e os campos do registro de 76 fora o Woodmon's Leaf. Testes: `TutorialTests`.

Usar item (`0x41`): por ora só a Taming Net. Ver [captura](../../mecanicas/captura.md).
