# Struct: status de digimon

Bloco de 248+ bytes com o estado completo de um digimon. Aparece embutido em vários S2C:
`0x02` (dados do personagem), `0x10`, `0x63` (criar batalha), `0x64`, `0x70` (level up), `0x71` (adicionar ponto de atributo), `0x80`, `0x82`, `0x84`, `0xef`.
Emulador: `Network/Packets/Map/DigimonStatusBlock.cs` (688 bytes, usado por todos esses opcodes). Extração: [extract_status.py](../../ferramentas/extract-status.md).

✅ **v322: o bloco tem 696 bytes** (688 na v242). Evidência: `0xB4` = 1 bloco (688 → 696); `0x02` tem 5 blocos a partir do offset 1240 e tudo depois deles andou +40 (X/Y do tamer 4680 → 4720). Os offsets 0..0x13F são iguais nas duas versões (comparação de `0xB4`). ❓ Os 8 bytes novos caem na área zerada depois de 0x13F; a posição exata ainda não é conhecida. Ver [mudanças v242 → v322](../opcodes.md#mudanças-v242--v322).

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
| 64 | 128 B | ? | tudo zero, exceto o u32 em +160 |
| 160 | u16, u16 | ❓ tipo/estágio | valores 0x0001/0x0001, 0x0102/0x0001, 0x0001/0x0002, 0x0102/0x0080, 0x0202/0x0200; variam por espécie e não por nível |
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
| 276 | … | skills e restante | a mapear |

❓ O layout depois do offset 232 pode variar por opcode (`RefreshDigimonStatus` escreve 16 bytes fixos em 216 em vez do EXP). Confirmar quando o schema existir.
