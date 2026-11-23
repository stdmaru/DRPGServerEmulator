# Movimento do jogador

## 0x20 (cliente → mapa), 20 bytes
| Offset | Tipo | Campo |
|---|---|---|
| 0 | i16 | ✅ sempre 5 |
| 2 | i16, i16 | x, y |
| 6 | 14 B | ✅ zero |

- Enquanto anda, o cliente manda um 0x20 a cada **~0,87 s** (mediana de 1655 intervalos). Parado, não manda nada.
- Passo entre dois 0x20: mediana de 3 células. O maior foi 9,2 células (euclidiana), cerca de 10,6 células/s.
- O cliente não deixa andar para células bloqueadas. O servidor oficial não foi observado recusando um passo.

## Validação no emulador (`Game/World/Movement.cs`)
1. **Célula:** o destino precisa ser andável ou vizinho de uma andável ([mapas](../cliente/mapas.md)).
2. **Velocidade:** distância ≤ 2 + 11 × t, com t limitado a [0,87 s; 1,74 s]. Ficar parado não acumula distância.
3. Se falhar, o servidor não aceita o passo e devolve o jogador à última posição aceita com `0xB0` (❓ escolha nossa).
4. Teleporte (portal, comando) zera o relógio do passo.

Sem `grids.bin`, só a velocidade é checada.
