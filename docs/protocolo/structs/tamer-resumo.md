# 0x10 S2C: resumo do tamer e do líder

Evidência: `captures.db`, 49 amostras v242 (200 bytes) e 1919 v322 (204 bytes), quase todas de outros jogadores no mapa. Também é a resposta ao `0x04` C2S na entrada do mapa.

| off | tipo | campo | estado |
|---|---|---|---|
| 0 | 16 B | serial do tamer | ✅ |
| 16 | char[21] | nome | ✅ |
| 37 | u8 | tamer (1–4) | ✅ |
| 38 | u16 | ? (v242: 1, 2; v322: 1, 2, 5, 6) | |
| 40 | u16 | nível do tamer | ✅ (14 para o personagem nível 14) |
| 42 | u8, u8 | ? (baixo 1/5/11/15, alto 0..7) | |
| 44 | i16, i16 | X, Y | ✅ |
| 48 | i16, i16 | X, Y de destino (igual à posição quando parado, 798 de 1919) | ✅ |
| 52 | i32 | 0 em todas | |
| 56 | 16 B | serial do líder | ✅ |
| 72 | u16 | espécie do líder | ✅ |
| 74 | char[21] | nome do líder | ✅ |
| 95 | u8 | estágio do líder (1, 2, 3, 5) | ❓ igual ao byte 160 do [bloco de status](status-digimon.md) |
| 96 | i32 | nível do líder | ✅ (4 para Guil L4, 12 para L12) |
| 100 | u32 | ? 20030 nos líderes de estágio 5 | ❓ mesmo valor do bloco de status em 320 |
| 128 | u32, u32 | ? `5` e 4985..5000 (539 de 1919, só v322) | ❓ |

- ✅ Offsets 0..100 iguais nas duas versões. ❓ Os 4 bytes novos caem depois de 100; o par em 128 só aparece na v322, mas isso não prova que é onde eles entram.
- Uma amostra v322 tem 108..198 preenchidos (parece outro formato; não analisada).
- Emulador: `RefreshCharacterInfoPacket` em `Network/Packets/Map/CharacterPackets.cs`. O estágio é fixo em 1 até existir a tabela de estágios das espécies.
