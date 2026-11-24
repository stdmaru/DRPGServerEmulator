# Equipamentos do Tamer

Dados: engenharia reversa de `DigimonRPG.bin` (`0x00AF030C`, `0x00B0E740..0x00B0FEFD`, `0x00B5BB50`, `0x00B5C170`) e capturas v322 em `captures.db` (`conn=234`, `conn=238`, `conn=428`).
Código em `Game/Entities/EquipmentSlots.cs`, `Game/Entities/Inventory.cs`, `Network/Handlers/Map/ItemHandlers.cs`, `Network/Packets/Map/CharacterPackets.cs` (`0x02`) e `Network/Packets/Map/BattlePackets.cs` (`0xB1`).

---

## ✅ Mapa dos 29 Slots de Equipamento (`ItemPlace` no `0x91`)

| `ItemPlace` | Slot | `ItemInfo.Type` aceito | Offset em `0x02` (7600 B) | Offset em `0xB1` (1252 B) |
|---:|---|---|---:|---:|
| `4` (`0x04`) | Crest 1 | `1` | `0x0064` (`100`) | `0x018` (`24`) |
| `5` (`0x05`) | Crest 2 | `1` | `0x00D0` (`208`) | `0x084` (`132`) |
| `6` (`0x06`) | Crest 3 | `1` | `0x013C` (`316`) | `0x0F0` (`240`) |
| `7` (`0x07`) | Digimental 1 | `2` | `0x01A8` (`424`) | `0x15C` (`348`) |
| `8` (`0x08`) | Digimental 2 | `2` | `0x0214` (`532`) | `0x1C8` (`456`) |
| `9` (`0x09`) | Digimental 3 | `2` | `0x0280` (`640`) | `0x234` (`564`) |
| `11` (`0x0B`) | Aura | `8` ou `27` | `0x1274` (`4724`) | — |
| `12` (`0x0C`) | D-ark / Digivice | `9` | `0x12E0` (`4832`) | `0x2A0` (`672`) |
| `14` (`0x0E`) | Meias (Socks) | `15` | `0x1354` (`4948`) | — |
| `15` (`0x0F`) | Sapatos (Shoes) | `16` | `0x13C0` (`5056`) | — |
| `16` (`0x10`) | Calças (Pants) | `17` | `0x142C` (`5164`) | — |
| `17` (`0x11`) | Luvas (Gloves) | `18` | `0x1498` (`5272`) | — |
| `18` (`0x12`) | Camisa (Shirt) | `19` | `0x1504` (`5380`) | — |
| `19` (`0x13`) | Jaqueta (Jacket) | `20` | `0x1570` (`5488`) | — |
| `20` (`0x14`) | Chapéu (Hat) | `21` | `0x15DC` (`5596`) | — |
| `21` (`0x15`) | Conjunto (Costume Set) | `28` | `0x1648` (`5704`) | — |
| `22` (`0x16`) | Brinco (Earring) | `23` | `0x16B4` (`5812`) | — |
| `23` (`0x17`) | Colar (Necklace) | `24` | `0x1720` (`5920`) | — |
| `24` (`0x18`) | Anel (Ring) | `25` | `0x178C` (`6028`) | — |
| `25` (`0x19`) | D-ark Card | `37` | `0x17F8` (`6136`) | `0x478` (`1144`) |
| `26` (`0x1A`) | Spirit 1 | `38` | `0x1864` (`6244`) | `0x30C` (`780`) |
| `27` (`0x1B`) | Spirit 2 | `38` | `0x18D0` (`6352`) | `0x378` (`888`) |
| `28` (`0x1C`) | Spirit 3 | `38` | `0x193C` (`6460`) | `0x3E4` (`996`) |
| `81` (`0x51`) | Asas (Wings) | `40` | `0x1AD4` (`6868`) | — |
| `82` (`0x52`) | Efeito Flutuante | `41` | `0x1B40` (`6976`) | — |
| `83` (`0x53`) | Emblema / Fundo | `42` | `0x1BAC` (`7084`) | — |
| `84` (`0x54`) | Fonte | `43` | `0x1C18` (`7192`) | — |
| `85` (`0x55`) | Gema (Gem) | `44` | `0x1C84` (`7300`) | — |
| `86` (`0x56`) | Relógio (Watch) | `45` | `0x1CF0` (`7408`) | — |

---

## ✅ Fluxo de Equipar / Desequipar (`C2S 0x91`)

Confirmado em `captures.db` (`conn=234`, `pid=115772..115794`):
1. Cliente envia **`C2S 0x91` (250 B)** com:
   - Equipar: `From = 3 (Inventory)`, `To = <ItemPlace>` (`ToA = 0, ToB = 0`).
   - Desequipar: `From = <ItemPlace>` (`FromA = 0, FromB = 0`), `To = 3 (Inventory)`.
   - Trocar entre slots compatíveis: `From = <ItemPlace>`, `To = <ItemPlace>`.
2. Servidor valida (fora de batalha, propriedade `Uid` + `ItemId`, quantidade `1`, tipo aceito pelo slot, nível mínimo do Tamer, compatibilidade de item deslocado em troca), atualiza a bolsa `BagKind.Equipment` (`5`), recalcula os status de todos os Digimons do time, agenda `RequestImmediateSave()` e envia na ordem exata:
   1. **`S2C 0xB1` (1252 B)**: dados do Tamer + 11 slots de equipamento.
   2. **`S2C 0xB4` (696 B) × N**: um bloco atualizado por Digimon da party.
   3. **`S2C 0x10` (204 B)**: atualização do Tamer no mapa.
   4. **`S2C 0x91` (236 B)**: eco da movimentação com o item movido e o item trocado (ou zeros).

---

## ✅ Bônus de Status (`item_info_us.dat` col. 28)

Os bônus são extraídos automaticamente da coluna 28 de `item_info_us.dat` na carga do servidor (`ClientTables.ParseEquipmentBonus`):
- **Atributos (`STR`, `AGI`, `CON`, `INT`, `All stats`):** somam antes de `StatFormulas.Combat` (✅ exato em `conn=234`: `Digimental #215` `+6 STR/AGI/CON` eleva `InputName L1` de `145/87/131/115/115` para `183/105/139/146/125`).
- **Status flat (`ATK`, `DEF`, `BR`, `HP`, `VP`) e `%` (`ATK%`, `DEF%`, `BR%`):** somam após `StatFormulas.Combat` (✅ exato em `conn=234`: `Crest #107` `+75 ATK / +40 DEF / +350 BR` leva `InputName L1` a `258/145/489/146/125` com 1 Crest + 1 Digimental e `333/185/839/146/125` com 2 Crests + 1 Digimental).
- **Efeitos extras:** `Card Slash N times` (D-ark expande o limite de 20 usos por batalha), `Digimon EXP +N%` (multiplicador na recompensa de batalha), `Tamer EXP +N` e recuperação de `HP/VP/EVP`.
- **Triplas em `ItemRecord` (`+36..83`):** até 4 triplas `(effectId, statCode, value)` (`1..4` = STR/AGI/CON/INT, `301..305` = ATK/DEF/BR/HP/VP), conforme observado em `conn=234/238/428`.

---

## ✅ Persistência no Banco de Dados

Usa a tabela existente `inventory_item` com `bag = 5` (`BagKind.Equipment`) e `slot = (byte)ItemPlace` (`4..86`), sem exigir nova migração SQL. Na recarga (`CharacterFactory.Build`), cada item equipado passa novamente por `EquipmentSlots.CanEquip` antes de ser colocado no slot.
