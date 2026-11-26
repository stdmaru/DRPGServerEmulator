# Tela de login do launcher (sprites antigos)

O `DigimonRPG.bin` v322 **não desenha** mais a tela de login. Os sprites dela continuam no `resource.pf` e o nome deles só aparece na lista de pré-carga do `CSceneServer` (construtor `0x00b504b0`, vtable `0x0102f62c`). Não há código com coordenadas do painel: o layout abaixo foi **medido nos próprios sprites** e, onde não há pista, decidido aqui (❓). Formato dos arquivos em [formato `.spr`](formato-spr.md).

## Sprites (todos em `gui\`)
| Sprite | Frames | Tamanho | O quê |
|---|---|---|---|
| `dr2_in_000_000` / `_eng` | 1 | 800×600 | fundo. A versão `_eng` tem o logo "DIGIMON RPG" e não tem a classificação indicativa coreana |
| `dr2_in_003_000` | 1 | 315×212 | painel "LOG IN" com campos ID e PASSWORD e uma caixinha (❓ lembrar ID) |
| `dr2_in_003_001`, `dr2_in_003_002` | 4 cada | 48×63, 57×63 | mascotes animados (4 quadros) |
| `dr2_in_003_003` | 3 | 58×38 | botão OK (normal, sobre, pressionado) |
| `dr2_in_003_004` | 3 | 108×38 | botão CANCEL (mesmos estados) |
| `dr2_in_000_001` / `_eng` | 3 | 93×40 | botão sair (종료 / Close) |
| `dr2_in_000_006` / `_eng` | 3 | 93×40 | outro botão sair (também usado pelo `CSceneServer`) |
| `dr2_in_000_007` | 1 | 212×16 | título "NOTICE BOARD" |
| `dr2_in_000_004`, `_005` | 3 cada | 18×16 | setas de rolagem cima/baixo |
| `dr2_in_000_008` | 10 | 10×10 | dígitos 0–9 |
| `dr2_in_000_010` | 3 | 22×23 | botão fechar (X) |
| `dr2_in_001_000` | 1 | 239×196 | caixa "Loading" com moldura da barra |
| `dr2_in_001_002` | 2 | 231×23 | barra de progresso |

Os botões têm 3 estados nesta ordem: normal, sobre, pressionado (❓ confirmar a ordem no `CSceneServer`).

`dr_title_000..007` **não são** da tela de login: são a janela de títulos de digimon do jogo (TITLE, FRONT/BACK, Apply/Unlock, usada em `0x00be6b10` e `0x00c8ad40`). `dr2_in_002_*` é a seleção de mundo (DIGITAL WORLD / BATTLE ZONE) do `CSceneServer`.

## Medidas no painel `dr2_in_003_000`
Coordenadas relativas ao canto do painel.

- Campo ID: (138, 83) a (283, 100), 146×18. Campo PASSWORD: (138, 129) a (283, 146). Cor do fundo do campo: RGB565 `0x0A51`.
- O canto inferior direito é recortado: x ≥ 148, y ≥ 166. OK (58) + CANCEL (108) = 166×38 encaixam exatamente ali: OK em (148, 170), CANCEL em (206, 170).
- O título "LOG IN" ocupa o alto à esquerda (y 18–46); o alto à direita é vazio, onde ficam os mascotes.

## Layout escolhido (quadro 800×600)
- Fundo `_eng` em (0, 0).
- ❓ Painel em (242, 330): centrado na horizontal, na metade de baixo, sem cobrir o logo.
- ❓ Mascotes sobre a borda do painel: `003_001` em painel + (200, 3), `003_002` em painel + (250, 3).
- ❓ Botão sair `000_001_eng` no canto inferior direito, em (697, 550).
- ❓ Texto dos campos em branco, a 4 px da borda esquerda; a senha aparece como `*`.
- ❓ Mural de avisos (`000_007` + setas) e a caixa de loading (`001_000`) para o progresso do patch: posição a decidir quando o patcher existir.

A tela inteira segue a regra do [cliente novo](../planos/cliente-novo.md): quadro 800×600 centralizado, sem esticar.
