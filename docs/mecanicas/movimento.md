# Movimento do jogador

## 0x20 (cliente → mapa), 20 bytes
| Offset | Tipo | Campo |
|---|---|---|
| 0 | i16 | ✅ sempre 5 |
| 2 | i16, i16 | x, y |
| 6 | 14 B | ✅ zero |

- Enquanto anda, o cliente manda um 0x20 a cada **~0,87 s** (mediana de 9623 intervalos em `captures.db`, ~40 ticks do cliente). Parado, não manda nada.
- ✅ **Física do cliente (`DigimonRPG.bin @ 0x85e5b0..0x85e73b`):** cada célula tem 64×32 px (`0x85e848..0x85e86c`). A velocidade base por direção é `(±4 px, ±2 px)` (`1/16` célula/tick por eixo); no estado normal do tamer (`0x05`, `0x85e6ab`) o passo é multiplicado por **2** (`1/8` célula/tick por eixo → ~5 células por `0x20` ≈ `5,77 células/s`); com fantasia de velocidade (`0x0F`, `0x85e6fd`, ex.: *Cycle Costume* `Movement speed +200%`) o passo é multiplicado por **4** (`1/4` célula/tick por eixo = **200% da base** → ~10 células por `0x20` ≈ `11,54 células/s`).
- Como X e Y avançam juntos na diagonal (sem divisão por √2), a distância real é **Chebyshev** (`max(|dx|, |dy|)`).
- O cliente não deixa andar para células bloqueadas. O servidor oficial não foi observado recusando um passo.

## Validação no emulador (`Game/World/Movement.cs`)
1. **Célula:** o destino precisa ser andável ou vizinho de uma andável ([mapas](../cliente/mapas.md)).
2. **Velocidade:** `max(|dx|, |dy|) ≤ 1,5 + MoveSpeed × t`, com `t` limitado a `[0,867 s; 1,300 s]` (1,5× o intervalo normal; ficar parado não acumula distância). `MoveSpeed = 6,0 × max(100, MoveSpeed%) / 100`.
3. Se falhar, o servidor não aceita o passo e devolve o jogador à última posição aceita com `0xB0` (❓ escolha nossa).
4. Teleporte (portal, comando) zera o relógio do passo.

Sem `grids.bin`, só a velocidade é checada.
