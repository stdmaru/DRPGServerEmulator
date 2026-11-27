# Formato `.ani` (animações de sprite) ✅

Arquivos `Sprite\*.ani` no `resource.hf`. Um `.ani` descreve as animações de um [`.spr`](formato-spr.md): quais quadros do sprite formam cada animação em cada direção, e o ponto de âncora de cada quadro. Carregadores na v322: `0x0086e8c0` / `0x00872ad0`.

## Layout (little-endian)
| Offset | Tipo | Campo |
|---|---|---|
| `0x00` | char[14] | assinatura `DCANIMATION0.5` (sem terminador) |
| `0x0e` | char[24] | nome do sprite (terminado em zero); o sprite é `<nome>.spr` na mesma pasta ❓ (confere com o nome do `.ani` nos exemplos vistos) |
| `0x26` | u32 | `n` = número de quadros |
| `0x2a` | `n` × 12 | por quadro: i32 duração (lida como i16, `0x00873140`; em passos de lógica), i32 `cx`, i32 `cy` (âncora do quadro) |
| `0x2a + 12n` | 0x3c0 | tabela de animações: **20 animações × 8 direções** × {u16 início, u16 fim, u16 contagem} |
| depois | 6240 | zeros nos arquivos vistos ❓ |

## Uso
- `0x00873110(anim, dir)` → início; `0x008730e0(anim, dir)` → contagem. A animação `0xf` é tratada como 5 ❓.
- Animações usadas na seleção de personagens: **1** = parado, **5** = selecionado; sempre a direção 0.
- Laço (seleção de personagens): o quadro avança a cada 3 quadros do jogo; quando `quadro ≥ início + contagem − 1` volta a `início`. Ver [cenas](cenas.md#slots-0x00b47790).
- ✅ Âncora `cx`/`cy`: na seleção de personagens nem o digimon (offset por espécie, [tabelas por espécie](tabelas-de-especie.md)) nem o tamer (`0x0099fb90`, [cenas](cenas.md)) a usam; no **mapa** o digimon desenha o quadro com o canto em (x − cx, y − cy) (`0x0086d200`/`0x0086d220`); o tamer continua pelo canto (x − 120, y − 162). Ver [cena de jogo](cena-jogo.md#tamer-e-digimon-no-campo).
- ✅ Laço no mapa: cada quadro fica duração + 1 passos; o deslocamento volta a 0 quando passa de contagem − 1 (todos os quadros aparecem). Ver [cena de jogo](cena-jogo.md#tamer-e-digimon-no-campo).
