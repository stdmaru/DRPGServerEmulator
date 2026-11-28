# Movimento do jogador

## 0x20 (cliente → mapa), 20 bytes
| Offset | Tipo | Campo |
|---|---|---|
| 0 | i16 | ✅ sempre 5 |
| 2 | i16, i16 | x, y |
| 6 | 14 B | ✅ zero |

- Enquanto anda, o cliente manda um 0x20 a cada **~0,87 s** (mediana de 9623 intervalos em `captures.db`; cooldown de **800 ms** em `0x00b4b360`). Parado, não manda nada.
- ✅ **Conteúdo do `0x20` (`0x00b4d290` → `0x00ae1000`):** envia a **célula de destino clicada** (não a célula atual). Em 800×600, meia tela vertical tem `300 / 32 = 9,375` células — nas 9640 amostras v322 consecutivas de `captures.db`, o passo observado sem bônus chega a `|dx| ≤ 8` e `|dy| ≤ 11` (mediana de 3–4 células em 0,867 s).
- ✅ **Física do cliente (`DigimonRPG.bin @ 0x85e5b0..0x85e73b`):** cada célula tem 64×32 px (`0x85e848..0x85e86c`) e o laço roda a **30 FPS** (`0x00867ed0(0x1e)`, 1 passo por quadro). O passo base é `(±4 px, ±2 px)` (`1/16` célula/passo por eixo); no estado normal (`0x05`, `×2`) dá `1/8` célula/passo = **3,75 células/s**; com fantasia de velocidade (`0x0F`, `×4`, ex.: *Cycle Costume* `+200%`) dá `1/4` célula/passo = **7,5 células/s**.
- Como X e Y avançam juntos na diagonal (sem divisão por √2), a distância real é **Chebyshev** (`max(|dx|, |dy|)`).
- O cliente não deixa andar para células bloqueadas. O servidor oficial não foi observado recusando um passo.

## Validação no emulador (`Game/World/Movement.cs`)
1. **Célula:** o destino precisa ser andável ou vizinho de uma andável ([mapas](../cliente/mapas.md)).
2. **Velocidade:** `max(|dx|, |dy|) ≤ 4,5 + MoveSpeed × t`, com `t` limitado a `[0,867 s; 1,300 s]` (1,5× o intervalo normal; ficar parado não acumula distância) e `MoveSpeed = 7,5 × max(100, MoveSpeed%) / 100`. A folga de `4,5` células absorve cliques até a borda da tela (`11,00` células em 0,867 s sem bônus, cobrindo 100% das 9640 amostras v322) mantendo a recusa para passos `≥ 12` sem fantasia e `≥ 18` com fantasia de 200%.
3. Se falhar, o servidor não aceita o passo e devolve o jogador à última posição aceita com `0xB0` (❓ escolha nossa).
4. Teleporte (portal, comando) zera o relógio do passo.

Sem `grids.bin`, só a velocidade é checada.
