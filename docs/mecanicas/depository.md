# Digimon Depository (Armazém de Digimons)

O Digimon Depository (janela `CWindowDigimonDepository`, ID `0x29` no cliente) permite guardar até **10 Digimons** fora do time ativo (5 posições), retirar, trocar (swap) e expandir slots usando itens de expansão.

---

## 1. Map `0x89` — Ação no Depository ✅

Evidência: `DigimonRPG.bin` (S2C `0x00AC2498`, C2S `0x00AD9D80` abrir / `0x00AD9E60` operar) + sessão 62 de `captures.db` (C2S 750 B, S2C 736 B).

- **C2S:** 750 bytes (`736` da struct + 14 zeros finais).
- **S2C:** **736 bytes** (`0x2E0` = `40 + 696`).

| Offset | Tamanho | Tipo | Descrição |
|---|---|---|---|
| `0` | 2 | `u16` | `npcId` (`1` ao abrir; ecoado do `0x8A` nas demais ações) |
| `2` | 1 | `u8` | `action`: `1` = Abrir, `2` = Depositar, `3` = Retirar, `4` = Trocar (Swap) |
| `3` | 1 | `u8` | padding (`0`) |
| `4` | 4 | `u32` | `partyDigimonUid` (Digimon do time a depositar ou trocar; o cliente remove via `0x00B5CEC0`) |
| `8` | 4 | `u32` | `depositoryDigimonUid` (Digimon do armazém a retirar ou trocar) |
| `12` | 12 | `u8[12]` | reservado (`0`) |
| `24` | 8 | `f64` | `newBits` (Bits atualizados do tamer, gravados em `player + 0x12F0`) |
| `32` | 1 | `u8` | `result` (`0xFF` no C2S; códigos S2C abaixo) |
| `33` | 7 | `u8[7]` | padding (`0`) |
| `40` | 696 | `DigimonStatusBlock` | Bloco de status v322 do Digimon retirado (`action=3`) ou trocado (`action=4`) |

### Códigos de `result` (offset `32`) ✅
| `action` | Sucesso | Falha | Sem Bits | Erro de checagem |
|---|---|---|---|---|
| `1` (Abrir) | `1` (abre janela `0x29`) | `2` (precisa expandir) | — | `0` |
| `2` (Depositar) | `3` | `4` | `5` | — |
| `3` (Retirar) | `6` | `7` | `8` | — |
| `4` (Trocar) | `9` | `10` | `11` | — |

Regras:
- O líder (posição `0` do time) não pode ser depositado se ficar sem nenhum Digimon no time; na troca (`action=4`), o Digimon retirado assume a posição do que saiu.
- A taxa em Bits (`DepositoryFeeBits`) e a quantidade inicial de slots (`DefaultDepositorySlots`) são configuráveis em `GameOptions`.

---

## 2. Map `0x8A` — Slots disponíveis e lista do Depository ✅

Evidência: `DigimonRPG.bin` (S2C `0x00AC27F0` → `0x00B7C530` lista + `0x00B7BF50` renderização de cada entrada).

- **S2C:** **808 bytes** (`0x328` = `8 + 10 × 80`).

| Offset | Tamanho | Tipo | Descrição |
|---|---|---|---|
| `0` | 2 | `u16` | `npcId` (salvo em `client + 0x3E88`, ecoado no C2S `0x89`) |
| `2` | 2 | `u16` | `usedSlots` (salvo em `client + 0x3E8C`) |
| `4` | 1 | `u8` | `availableSlots` (`0..10`, salvo em `Window(0x29) + 0x142D`, logado como `사용가능슬롯 %d`) |
| `5` | 3 | `u8[3]` | padding (`0`) |
| `8` | 800 | `Entry[10]` | 10 entradas de **80 bytes (`0x50`)**; entradas com `uid == 0` são ignoradas pelo cliente |

### Entrada de 80 bytes (`0x50`) ✅
| Offset | Tamanho | Tipo | Descrição |
|---|---|---|---|
| `0` | 4 | `u32` | `uid` (`0` = vazio) |
| `4` | 4 | `u32` | `speciesId` (espécie base/atual; `> 9` desenha o retrato) |
| `8` | 21 | `char[21]` | `name` (null-terminated) |
| `29` | 1 | `u8` | padding (`0`) |
| `30` | 2 | `u16` | `level` |
| `32` | 1 | `u8` | `stage` / tipo (lookup tabela de texto `0x10D2`) |
| `33` | 1 | `u8` | `attribute` (lookup tabela de texto `0x10D3`) |
| `34` | 2 | `u16` | padding (`0`) |
| `36` | 4 | `i32` | `wins` / contador numérico (`> 0` desenha ícone) |
| `40` | 40 | `u8[40]` | zeros |

---

## 3. Map `0x94` — Expansão de slots por item ✅

Evidência: `DigimonRPG.bin` (C2S `0x00AD9CC0` chamado de `0x00B7BC16`, S2C `0x00AC2883`).

- Antes de enviar `C2S 0x94` (1 byte + 14 zeros), o cliente verifica se `availableSlots <= 10` e se o inventário contém 1 unidade do item **`901` (`0x385`)** ou **`929` (`0x3A1`)**.
- **S2C `0x94`:** **1 byte**:
  - `0` → sucesso (`Window(0x29) + 0x1440 = 1`)
  - `1` → sem item de expansão (`Window(0x29) + 0x1440 = 2`)
  - `2` → já está no máximo de 10 slots (`Window(0x29) + 0x1440 = 3`)
  - `3` → erro de checagem
- No sucesso, o servidor consome 1 unidade do item (`901` ou `929`), incrementa `DepositorySlots` (+1 até 10) e envia `0xFC` (inventário atualizado), `0x8A` (slots atualizados) e `0x94` (`0`).

---

## 4. Integração com a Enciclopédia (Global `0x7B`) ✅

Ao fechar a janela do Depository (`0x00B7C430`), o cliente dispara automaticamente o par **Global `0x7B` + `0x7C`**.
- `0x7B` marca `owned[speciesId] = 1` para todas as espécies (base e formas desbloqueadas) dos Digimons guardados no Depository.
- `0x7C` continua reportando os 5 slots do time ativo.
