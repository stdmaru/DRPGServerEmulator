# Campo: andar, entidades e UI (marco 3, passo 2)

RE da v322 para o passo 2 do marco 3 (PR #51, `feat/client-walk`): andar com pathfinding, digimons do servidor andando no cliente, NPCs, portais e o comportamento da UI do campo. Complementa a [cena de jogo](cena-jogo.md) (entrada no mapa, quadro, HUD) e o [movimento](../mecanicas/movimento.md) (lado do servidor). ✅ = lido no dump; ❓ = ainda não lido.

> **Estado:** fase A **em andamento**. Feito: o laço de passos e o ciclo de vida dos selvagens (`0x11`). O resto está no [roteiro](#roteiro-do-que-falta) com os pontos de entrada.

## Como ler o binário
- Dump decompilado: `work/re/v322/dump/decomp/XXXX/<endereço>.c`, com `functions.tsv` (entrada, tamanho, chamadores), `calls.tsv` (quem chama quem) e `strings.tsv`.
- Disassembly direto do `client/DigimonRPG.bin.orig` (desempacotado): `objdump -D -b binary -m i386 --adjust-vma=0x700a00 --start-address=VA --stop-address=VA+N`. A fórmula `arquivo = VA − 0x701000 + 0x600` só vale para o `.text` (`0x701000..0x1006000`). Para dados (`.rdata` a partir de `0x1006000`, arquivo `0x904a00`), use as seções do PE: `arquivo = VA − início da seção + offset bruto da seção`. Exemplos: vtables em `0x0100xxxx` e as constantes `0x01007860` = 30.0, `0x01006a88` = 1000.0.
- O Ghidra às vezes indexa campos como `param_1[0x57]` (int) no lugar de `param_1 + 0x15c`: procure pelos dois jeitos.

## Ritmo da lógica
- ✅ O laço principal (`0x00aa4a90`) limita o jogo a **30 quadros por segundo** (`0x00867ed0(0x1e)`, `0x00867eb0`, `0x00868020`), e a cena faz **exatamente um passo de lógica por quadro**. O acumulador de `0x00b4e550` soma `DAT_0135af08 − 0x00867e40()`, mas `0x00867e40` devolve a taxa medida (`0x00867fb0`, uma vez por segundo) ou 30.0 (`0x01007860`) quando ela está abaixo de 30. A soma nunca é positiva, então os passos extras nunca acontecem: abaixo de 30 FPS o jogo fica mais lento. O cliente novo roda 30 passos/s pelo relógio (PR #49).
- ✅ **Consequência para o movimento:** a [física](../mecanicas/movimento.md) dá 1/8 de célula por eixo e por quadro no andar normal. A 30 quadros/s isso é **3,75 células/s**, não os 7,5 da wiki (que supôs 60). ❓ Conferir nas capturas (distância entre `0x20` seguidos, ~0,87 s) antes de mudar a validação do emulador (`Game/World/Movement.cs`).

## Passos por quadro (`0x00b4e550`)
Cada passo chama, nesta ordem:
1. ✅ **`0x00afdba0`**: rede (despachante do mapa `0x00abc350`, global `0x00acdc10`, `0x00abc270`); depois, no campo (`[DAT_01158504]+0x80 == 1`):
   - `0x00afe310` (❓ provavelmente o tamer do jogador);
   - para cada entidade das listas **`+0x2f0`** (❓ outros jogadores) e **`+0x2f8`** (selvagens): se `0x0085e760(célula do tamer, 10, 12)` der falso, ou seja, `|dx| > 10` ou `|dy| > 12` do tamer, ela é **removida** (`0x00a24460`, vtable `+0x44`, destrutor `+0`); senão roda a **atualização** (vtable `+0x24`);
   - os NPCs (lista **`+0x300`**) também recebem `+0x24`.
   - O servidor manda o `0x11` até 12 × 23 células, mas o cliente só mantém o que está a até **10 × 12** células.
2. ✅ **`0x00afe1c0`**: vtable `+0x34` no tamer (`DAT_011584f0`), nas listas `+0x2f0` e `+0x2f8`, em três árvores (❓ campos `+0x14` das entradas) e na `+0x300`.
3. ✅ **`0x00a49b10`** (`DAT_01158534`): ❓ não lido.

## Selvagens (`0x11`)
- ✅ Despachante do mapa: caso `0x11` em `0xabe125` → **`0x00af25c0`**. Layout do pacote em [selvagens](../mecanicas/selvagens.md#0x11-mapa--cliente-56-bytes). Ignorado no mapa `0xFA` (`[DAT_011584f8]+0x511c98 == −6`).
  - Estado **12** (saindo) → `0x00acfdd0(serial)`: remove o grupo.
  - Outros → `0x00abb5b0` (acha pelo serial `0x00ad17a0` ou cria) e `0x007f3b30(estado, x, y, destino x, destino y, direção)` **empilha um comando** na fila da entidade.
- ✅ **Criar** (`0x00abb5b0`): objeto de `0xcb8` bytes (construtor `0x007e6de0`, vtable `0x010071c8`), iniciado por `0x007e78c0(serial[16], espécie, nome, x, y, direção)`. Depois:
  - `+0x82e` = estágio (`+0x2a` do pacote);
  - `+4` = 1;
  - `+0x92c` = grau (`+0x2c`);
  - `+0x8a0` = nível (`+0x28`);
  - `+0x18c` = `0x007f3bf0(…)` (❓).
  - Se o tamer está a até 6 × 4 células e o estado < 2, toca o efeito `0x76` (`0x00a1b670`).
  - Entra no fim da lista `+0x2f8` (contador `+0x2fc`) e chama `0x00b232b0(espécie)` (❓ provavelmente carrega o sprite da espécie).
- ✅ **Classe base das entidades** (construtor `0x0085e1c0`, vtable `0x01007178`), campos usados aqui:

  | Campo | Conteúdo |
  |---|---|
  | `+4` | ligado (1) |
  | `+0x149` | estado (1 parado, 4 andando, 5 movendo, 0xb batalha, 0xf, 0x11) |
  | `+0x154..+0x160` | fila de comandos (deque de registros de 0x18 B: estado, x, y, destino x, destino y, direção) |
  | `+0x164` | direção |
  | `+0x168`, `+0x16c` | posição em pixels |
  | `+0x178`, `+0x17a` | célula atual |
  | `+0x17c`, `+0x17e` | célula de destino |
  | `+0x1a0` | contador da animação parada |
  | `+0x814` | espécie |

- ✅ **Vtable do selvagem** (`0x010071c8`): `+0x24` atualização `0x007f3df0`, `+0x28` `0x007f4b10` (❓ próximo passo do caminho), `+0x2c` `0x007f4db0`, `+0x30` `0x007f4080` (estado → animação, batalha), `+0x34` `0x007f4cf0`, `+0x38` `0x00859340` (lê a fila), `+0x40` `0x007e83c0`, `+0x44` `0x007e7950`, `+0x48` `0x007f4070`.
- ✅ **Atualização** (`0x007f3df0`), em ordem:
  1. se a posição em pixels está **no centro de uma célula** (`0x00b35f00`: `px % largura == largura/2` e `py % altura == altura/2`) → `+0x38`: tira **um** comando da fila;
  2. `+0x30`;
  3. estado 1 ou `0x11` com célula ≠ destino → estado 5;
  4. animação parada (ligado e estado < 4): `+0x1a0` conta; em **200**, animação 3 (1 em 4) ou 2 (`0x0085e4c0`), exceto a espécie `0xfb`; em **320**, animação 1 e zera (igual ao digimon do tamer, já no cliente);
  5. `0x007f55d0` (animação por espécie), `0x007ff330`, `0x0082a030` (❓);
  6. estados 4, 5 e `0xf`: célula ≠ destino → `+0x28` (próximo passo), depois **`0x0085e5b0`** (física, ver [movimento](../mecanicas/movimento.md));
  7. `0x0085e940`, `+0x2c`, `+0x48` (só nos mapas de `0x00b39760`), `+0x5c`.
- ✅ **Ler a fila** (`0x00859340`), um comando de cada vez:
  - **1, 2, 3** (parado e afins): se a entidade está em 5 e o comando traz outra célula, ou está em 4 a mais de 1 célula dela, o destino passa a ser o do comando e o estado 5. Estado < 4 fica. Direção = a do comando.
  - **0xb** (batalha): só a direção.
  - **4, 5, 0xf** (andando): a mais de **6 células** da posição do comando → **teleporta** (`+0x44`, `0x0085e890(x, y)`, animação 1) e para; senão o destino passa a ser o do comando.
  - **6** e os seguintes (batalha, por espécie): fora deste passo.
- Implementação no cliente novo: cada selvagem é uma entidade com essa fila. O `0x11` empilha, e a entidade só lê o próximo comando quando chega ao centro de uma célula. Por isso o selvagem termina o passo de célula antes de virar.

## Roteiro do que falta
Pontos de entrada já localizados; ✅ quando lido.
- **Física e caminho:** `0x0085e5b0..0x0085e73b` (passo por quadro, já resumido em [movimento](../mecanicas/movimento.md)), `+0x28` do selvagem `0x007f4b10`, `0x0085e940`, `0x0085e890` (pôr numa célula), `0x0085e8e0`.
- **Tamer do jogador** (`DAT_011584f0`): `0x00afe310` (atualização no passo), a vtable do objeto e o clique no chão. Comece pelo `Handle` da cena 5 (vtable da cena, ctor `0x00b494f0`) e pelas mensagens de mouse do laço principal. Depois: pathfinding, envio do `0x20` (~0,87 s, ver [movimento](../mecanicas/movimento.md)) e câmera.
- **Digimon do tamer seguindo:** o digimon líder (`[DAT_011584f0]+0x7a0` é usado no quadro da cena).
- **NPCs:** criação em `0x00abb750`/`0x0085f090` ([NPCs do mapa](cena-jogo.md#npcs-do-mapa)), lista `+0x300`, atualização `+0x24` da vtable deles, sprite e nome.
- **Portais:** `map_us` (+0x84, 4 portais, [NPCs do mapa](cena-jogo.md#npcs-do-mapa)); o desenho do redemoinho ❓ (procurar o sprite na fila de desenho `0x00a0d340`).
- **Minimapa:** `WindowMiniMap`, marcadores em `0x00b9b930` e nomes em `0x00b9c4b0` ([minimapa](cena-jogo.md#minimapa-windowminimap-janela-5)).
- **Botões e dicas da UI:** desenho da barra `0x00b8a850`, tamanho do chat `0x00b8e2a0`, abas `0x00b91a80`; hover, clique e dica ❓.
- **Desenho dos selvagens:** `0x007e83d0` (sombra e borda já lidas no #49), na fila de y.
