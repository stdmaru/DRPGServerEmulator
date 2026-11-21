# Channel: lista, criação e exclusão de personagens

Evidência: `captures.db`, sessões 3, 4 (v242) e 52–56 (v322). Payload sem o cabeçalho de 14 bytes.
Resposta do channel começa com `i32 resultado` (2 sucesso, 3 erro) e `i32 código`.

## 0x12 S2C: lista (500 bytes) ✅
`i32 1` e 4 slots de 124 bytes. Igual na v242 e na v322.

| off | tipo | campo |
|---|---|---|
| 0x00 | u32 | uid do personagem (0 = vazio) |
| 0x04 | u8 | tamer (1–4) |
| 0x05 | char[21] | nome |
| 0x1A | u16 | nível do tamer ✅ (confirmado pelo dono do projeto: um personagem está no nível 201) |
| 0x1C | i16, i16 | X, Y |
| 0x20 | u32[11] | equipamento ❓ (sempre zero nas 18 listas v322, mesmo depois de equipar) |
| 0x4C | u16, u16 | espécie e nível do digimon líder |
| 0x50 | char[24] | nome do líder |
| 0x68 | i32, i32 | batalhas, vitórias |
| 0x70 | u8 | índice do slot (preenchido também no slot vazio) |
| 0x71 | u8 | mapa |
| 0x72 | i16 | 0 |
| 0x74 | i32 | verificação = deslocamento + mapa + nível ✅ (todas as listas; slot vazio: = deslocamento) |
| 0x78 | i32 | deslocamento aleatório (muda a cada lista) |

O cliente pede a lista (`0x12` C2S, 14 bytes zero) a cada conexão no channel.

## 0x10: criar (C2S 78 → S2C 64) ✅
Pedido: `i32 1, i32 0`, `u8 slot`, `u8 mapa (0x4F)`, `u16 a`, `u16 b`, `u8 tamer`, `char[21] nome`, `u16 digimon`, `char[26] nome do digimon`.
- `slot` é o que o cliente escolheu (sessão 3: 1, 2, 3 em sequência; sessão 53: 3 depois de apagar o slot 3).
- `a` varia em 3B..3E, `b` em 37..3A. ❓ Parecem coordenadas, mas o personagem criado aparece em (56,56).
- Resposta: resultado e os **mesmos bytes do pedido** a partir de 0x08. Nome repetido: `3 / 102`, sem lista. Sucesso: `2 / 0` e depois a `0x12`.
- Mapa inicial do personagem novo: 79 (tutorial).

## 0x11: apagar (C2S 66 → S2C 52) ✅
Pedido: `i32 1, i32 0, u32 uid, char[40] MD5`. O cliente manda o MD5 de `""` (sem senha). Resposta: `2 / 0`, uid e o MD5 ecoado; depois a `0x12`.

## No emulador
`Network/Packets/Channel/ChannelPackets.cs` e `Network/Handlers/ChannelHandlers.cs`. Testes: `PacketSizeTests` (`Character_create_echoes_request_header`, `Character_list_check_is_offset_plus_map_plus_level`).
