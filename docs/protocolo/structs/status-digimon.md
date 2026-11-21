# Struct: status de digimon

Bloco de 248+ bytes com o estado completo de um digimon. Aparece embutido em vários S2C:
`0x02` (dados do personagem), `0x10`, `0x63` (criar batalha), `0x64`, `0x70` (level up), `0x71` (adicionar ponto de atributo), `0x80`, `0x82`, `0x84`, `0xef`.
Emulador: `Network/Packets/Map/DigimonStatusBlock.cs` (688 bytes, usado por todos esses opcodes). Extração: [extract_status.py](../../ferramentas/extract-status.md).

✅ **v322: o bloco tem 696 bytes** (688 na v242). Evidência: `0xB4` = 1 bloco (688 → 696); `0x02` tem 5 blocos a partir do offset 1240 e tudo depois deles andou +40 (X/Y do tamer 4680 → 4720). Os offsets 0..0x13F são iguais nas duas versões (comparação de `0xB4`). Os offsets 0..343 também são iguais (sessões 53–56: ~1.400 blocos v322 contra ~50 v242). Depois de 343 o bloco é **sempre zero** nas duas versões, então os 8 bytes novos caem nessa área e qualquer posição nela gera bytes idênticos. ❓ Posição exata desconhecida, sem efeito prático por enquanto.

Cada forma de evolução tem bloco próprio: ver [evolução](../../mecanicas/evolucao.md). Ver [mudanças v242 → v322](../opcodes.md#mudanças-v242--v322).

Offsets relativos ao início do bloco (não do payload; em 0x71 o bloco começa no offset 0 do payload).

| Off | Tipo | Campo | Estado |
|---|---|---|---|
| 0 | u32 | UID | ✅ |
| 4 | 16 B | serial | ✅ |
| 20 | u16 | ID da espécie (`digimon_us`) | ✅ |
| 22 | u16 | ? | sempre 0 nas amostras |
| 24 | u8 | ? | sempre 1 |
| 25 | char[21] | nome | ✅ |
| 46 | u16 | nível | ✅ |
| 48 | i32×4 | STR, AGI, CON, INT | ✅ |
| 64 | 128 B | ? | tudo zero, exceto 160..163 |
| 160 | u8, u8, u16 | estágio, atributo, ? | ✅ estágio e atributo = colunas 2 e 3 do `digimon_us`; o u16 em 162 (1, 2, 0x80, 0x200) ❓ varia por espécie |
| 192 | i32×3 | HP, VP, EVP atuais | ✅ |
| 204 | i32×3 | ATK, DEF, BR | ✅ |
| 216 | 8 B | ? | `00 00 00 80 00 00 00 00` |
| 224 | i64 | EXP atual (0x70/0x71); ❓ em outros opcodes parece EXP ganho (0x64, 0x84) | parcial |
| 232 | i32 | MaxActionGauge | ✅ (500 em todas as amostras) |
| 236 | i32×3 | MaxHP, MaxVP, MaxEVP | ✅ |
| 248 | 8 B | ? | `00 00 00 00 A0 86 01 00` (0x186A0 = 100000) |
| 256 | i64 | EXP para o próximo nível | ✅ em 0x70/0x71 |
| 264 | i32, i32 | vitórias, batalhas | ✅ em 0x70/0x71 |
| 272 | u16, u16 | pontos de atributo, pontos de skill | ✅ em 0x70/0x71 |
| 276 | u32, u16, i16 | skill 1: id, ❓ (6, 11, 21, 41), nível (1–10) | ✅ id e nível (sessão 56: 1 → 10) |
| 284 | u32, u16, i16 | skill 2, mesmo formato | ✅ |
| 296 | u32 | ? (0x2A…0x75, cresce com o nível) | |
| 300 | u8 | dono | ✅ 0 no digimon do jogador, 2 no selvagem (era a "Classification" inventada) |
| 312 | u8 | ? (7, 8, 0x41) | |
| 320 | u32, u32 | ❓ id (20030, 20100) e 1; não é item do `item_info_us` | |
| 328 | i32×4 | ❓ bônus de STR/AGI/CON/INT (1 ou 15 nas amostras) | |
| 344 | … | zero em todas as amostras | |

❓ O layout depois do offset 232 pode variar por opcode (`RefreshDigimonStatus` escreve 16 bytes fixos em 216 em vez do EXP). Confirmar quando o schema existir.
