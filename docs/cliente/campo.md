# Campo: andar, entidades e UI (marco 3, passo 2)

RE da v322 para o passo 2 do marco 3 (PR #51, `feat/client-walk`): andar com pathfinding, digimons do servidor andando no cliente, NPCs, portais e o comportamento da UI do campo. Complementa a [cena de jogo](cena-jogo.md) (entrada no mapa, quadro, HUD) e o [movimento](../mecanicas/movimento.md) (lado do servidor). ✅ = lido no dump; ❓ = ainda não lido.

> **Estado:** etapas 1 (andar + digimon seguidor + `0x20`/`0xB0`), 2 (colisões + ocupação), 3 (pathfinding A* `0x00b43200`), 4 (selvagens `0x11` no campo), **NPCs client-side** (`npc_info_us` + `0x09`) e **Portais + transição de área (`0xB0`)** **implementadas**. Restante do roteiro (UI do campo) a seguir.

## Como ler o binário
- Dump decompilado: `work/re/v322/dump/decomp/XXXX/<endereço>.c`, com `functions.tsv` (entrada, tamanho, chamadores), `calls.tsv` (quem chama quem) e `strings.tsv`.
- Disassembly direto do `client/DigimonRPG.bin.orig` (desempacotado): `objdump -D -b binary -m i386 --adjust-vma=0x700a00 --start-address=VA --stop-address=VA+N`. A fórmula `arquivo = VA − 0x701000 + 0x600` só vale para o `.text` (`0x701000..0x1006000`). Para dados (`.rdata` a partir de `0x1006000`, arquivo `0x904a00`), use as seções do PE: `arquivo = VA − início da seção + offset bruto da seção`. Exemplos: vtables em `0x0100xxxx` e as constantes `0x01007860` = 30.0, `0x01006a88` = 1000.0.
- O Ghidra às vezes indexa campos como `param_1[0x57]` (int) no lugar de `param_1 + 0x15c`: procure pelos dois jeitos.

## Ritmo da lógica
- ✅ O laço principal (`0x00aa4a90`) limita o jogo a **30 quadros por segundo** (`0x00867ed0(0x1e)`, `0x00867eb0`, `0x00868020`), e a cena faz **exatamente um passo de lógica por quadro**. O acumulador de `0x00b4e550` soma `DAT_0135af08 − 0x00867e40()`, mas `0x00867e40` devolve a taxa medida (`0x00867fb0`, uma vez por segundo) ou 30.0 (`0x01007860`) quando ela está abaixo de 30. A soma nunca é positiva, então os passos extras nunca acontecem: abaixo de 30 FPS o jogo fica mais lento. O cliente novo roda 30 passos/s pelo relógio (PR #49).
- ✅ **Consequência para o movimento:** a [física](../mecanicas/movimento.md) dá 1/8 de célula por eixo e por quadro no andar normal (`0x05`). A 30 quadros/s isso é **3,75 células/s** (8 passos/célula). Confirmado em `captures.db` (v322, 9312 pares consecutivos de `0x20`): mediana de **3 células** (3206×) e **4 células** (2401×) em **0,867 s** (`3,75 × 0,867 = 3,25`; mediana de 3,46 células/s). Como o `0x20` envia a célula clicada (e um clique na borda da tela pode ficar a 7–10 células), a validação do emulador (`Movement.cs`) continua com folga para não gerar teleporte falso.

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

## Tamer do jogador e digimon seguidor (etapa 1 ✅)
- ✅ **Vtable do tamer** (`0x0102fccc`, ctor `0x00b58eb0` / base `0x00863ff0`): `+0x24` atualização `0x008671c0`, `+0x30` estado → animação `0x00867790`, `+0x34` passo da animação `0x00867a60`, `+0x38` fila `0x00b5a890`.
- ✅ **Física e direções** (`0x0085e5b0`, `0x007f3880`, `0x0085e890`, `0x0085e8e0`, `0x0085e940`):
  - Direções `0..7`: `0 = S (0, +2)`, `1 = SW (-4, +2)`, `2 = W (-4, 0)`, `3 = NW (-4, -2)`, `4 = N (0, -2)`, `5 = NE (+4, -2)`, `6 = E (+4, 0)`, `7 = SE (+4, +2)`.
  - No campo (`+4 != 0`), o passo por quadro é multiplicado por: `×1` no estado `4` (andar devagar, 16 passos/célula), `×2` no estado `5` (correr normal, 8 passos/célula = **3,75 células/s** a 30 FPS) e `×4` no estado `0x0f` (fantasia de velocidade, 4 passos/célula = **7,5 células/s**; no meio da célula `0x008671c0` alinha `worldX % 16 == 8` e `worldY % 8 == 4`).
  - Ao chegar no centro de uma célula (`0x00b35f00`: `px % 64 == 32 && py % 32 == 16`), atualiza `(cellX, cellY)` (`0x00b36c20`). Quando `cell == dest`, zera velocidade e volta para o estado `1` (`0x0085e4c0(1, 0)`).
- ✅ **Clique, segurar e `0x20`** (`0x00b4c1a0`, `0x00b4e550`, `0x00b4d290`, `0x00b4b360`, `0x00ae1000`):
  - Clique esquerdo (`0x00b4c1a0`) e manter o botão pressionado (`0x00b4e550`) convertem `(mouse + câmera)` em célula e chamam `0x00b4d290(cellX, cellY)` (ignorado sobre o chat `0x00b91ec0` ou barra inferior `y >= 495`; deduplicado contra a última célula clicada `DAT_0135b288/8c`).
  - `0x00b4d290` chama `0x008656b0` (define destino e estado) e empilha na fila `DAT_01158504 + 0x2d20` (`0x00b4fde0`).
  - A cada quadro (`0x00b4b550` → `0x00b4b360`), se a fila não estiver vazia e o cooldown de **800 ms** (`800 < GetTickCount() - last`) tiver acabado, tira o **último** destino, limpa o resto da fila e envia `0x20` (`0x00ae1000`, 20 B: `i16 estado, i16 destX, i16 destY, 14 zeros`).
- ✅ **Câmera** (`0x00afe310` → `0x00b36b80`): centraliza em `(worldX − 400, worldY − 300)` dentro de `[0..pixelWidth−800] × [0..pixelHeight−600]`.
- ✅ **Digimon seguidor** (`0x008671c0` linhas 93–172, `param_1[0xc2]`):
  - No centro da célula do tamer andando (`4`, `5`, `0x0f`): se distância Chebyshev `<= 1`, apenas vira na direção do tamer (se parado); se `>= 2`, calcula a célula **1 passo atrás** de `tamer.dest` na direção `tamer.cell → tamer.dest` e empilha comando na fila do digimon (`0x007f3b30`).
  - Se a distância Chebyshev `> 5`, teleporta para `(tamer.cellX − 1, tamer.cellY − 1)` (`0x0085e890`) e para (`1`).
  - Roda a atualização `+0x24` (`0x007f3df0`): lê a fila no centro da célula (`0x00859340`, teleporte `> 6` células), anda com `0x0085e5b0` e pausa o contador de idle enquanto `state >= 4`.

## Colisões e ocupação (etapa 2 ✅)
- ✅ **Limites do mapa** (`0x00b35f80`): `0 <= x < width && 0 <= y < height`.
- ✅ **Colisão estática** (`0x00b35f40` → `0x00b35ea0`): uma célula é andável sse está nos limites e o byte `+0x14` (`Flags & 0xFF`) é diferente de zero. Objetos do mapa não têm máscara separada no andar — o editor já grava os bloqueios no byte `+0x14` do `.map`.
- ✅ **Grade de ocupação por entidade** (`0x00b35e10`, `0x00b36550`, `0x00b365c0`): matriz de `Serial` (16 B) por célula (`+0x511c7c`). Ao ocupar `(x, y)`, grava o serial em `(x, y)` e também em `(x, y − 1)` se estiver vazia; ao sair, limpa `(x, y)` e limpa `(x, y − 1)` se tiver o mesmo serial.
- ✅ **Célula livre** (`0x00b35b30`): andável (`0x00b35f40`) **e** sem serial na grade de ocupação (`0x00b35e10 == zero`).
- ✅ **Destino bloqueado ou ocupado** (`0x00b432d0`, `0x00b4d290`, `0x008671c0`, `0x007f4b10`):
  - No pathfinding (`0x00b432d0`), células intermediárias precisam passar em `0x00b35f40` (andável estático), mas a **célula de destino** é sempre permitida (`x == destX && y == destY`).
  - No passo efetivo (`0x008671c0`, `0x007f4b10`) e no clique (`0x00b4d290`), se o próximo passo já é o destino e `!0x00b35b30(next)` (bloqueada ou ocupada), a entidade **para antes de entrar** (ou ignora o clique se já está adjacente).

## Pathfinding A* (etapa 3 ✅)
- ✅ **Entrada** (`0x00b43200` → `0x00b430f0`): usado pelo tamer (`0x008671c0`, `0x00b4d290`), pelo digimon seguidor e pelos selvagens (`0x007f4b10`).
- ✅ **Parâmetros exatos:**
  - 8 vizinhos em `0x00b433d0`, na ordem: `NW, N, NE, E, SE, S, SW, W`.
  - Custo de passo uniforme `g + 1` (cardinal e diagonal custam 1); heurística Manhattan `|dx| + |dy|`; `f = g + h` (`0x00b43520`, empates inseridos antes na lista aberta).
  - Limite de profundidade: `min(2 · manhattan + 1, 25)` (`0x19` em `0x00b430f0`). Se `g >= limite`, falha (retorna `null`).
  - Passabilidade: `0x00b432d0` permite qualquer célula intermediária com `0x00b35f40` (andável estático) e sempre permite a célula de destino; depois os chamadores conferem `0x00b35b30` quando o próximo passo já é o destino, fazendo a entidade parar adjacente a paredes/NPCs.
  - Retorna apenas o **primeiro passo** do caminho (retrocedendo pelos ponteiros de pai até `g <= 1`).

## NPCs client-side no mapa (✅)
- ✅ **Carga automática na entrada do mapa** (`0x00ad3730` → `0x00abb750` / `0x0085f090`): percorre `npc_info_us` (`NPC_INFO`, `0x007231e0`) e instancia toda linha com `col 0 == mapId` (exceto mapa `0x66`) e `col 9 == 1` na lista `+0x300`.
- ✅ **Sprite e avatar**:
  - Padrão (`0x00b40d30`): carrega `Sprite\<col6>.ani` (fallback `Sprite\065_1.ani`). Animação (`0x00863430` + `0x00863b80`): a cada 200 passos, 1 em 4 troca para animação `3` ou `2` (se disponível), retornando a `1` ao concluir o ciclo. Desenho (`0x00860e50`): sombra 14/32 + edge 14/32 + sprite pela âncora, na fila Y-sort (`0x00ad4a50`, tipo 6).
  - Avatar (`Custom_N`, `0x007231e0` + `0x007230e0` + `0x00862650`): extrai o índice base-1 em `Custom_AvatarNPC` e desenha como boneco de papel (`TamerDoll`) em `(x − 120, y − 162)`, avançando quadro a cada 3 passos.
- ✅ **Ocupação na colisão** (`0x0085e940` / `0x0085f5c0`): cada NPC recebe um `Serial` (GUID) e ocupa `(x, y)` e `(x, y − 1)` na `FieldCollision`, fazendo o pathfinder parar adjacente ao clicar nele.
- ✅ **NPCs dinâmicos via S2C `0x09`** (`FieldNpcPacket`, 10 B: `i16 mapId, i16 npcId, i16 x, i16 y, u8 action, u8 0`; golden `captures.db`: `8D005500C800FF000100`):
  - `action == 1` (`0x00abb9e0`): se `mapId == currentMapId` e `npcId` existe em `NPC_INFO`, instancia em `(x, y)` e grava na lista persistente (`+0x760`) para recriar em futuras cargas do mesmo mapa.
  - `action == 0` (`0x00acfed0`): remove da lista ativa (se `mapId == 0` ou `mapId == currentMapId`) e da lista persistente, desocupando a célula.

## Portais e transição de área (`0xB0`) (✅)
- ✅ **Renderização e animação** (`0x00b380d0`, `0x00a159a0`, `0x00a121f0`, `0x00876a30` modo 5): lidos do campo `MapCell.Type` (`+8`) nas células visíveis (margem `-15..+30` em X, `-20..+40` em Y):
  - `1000` / `1005`: `Effect\effect_portal.spr`, camada de chão (antes das entidades), centrado na célula `(cx, cy)`, **modo 5 aditivo** (`min(dst + src, 255)` via `IRenderer.DrawAdditive`), avança a cada 2 passos (30 FPS).
  - `1001`: `Effect\effect_portal2.spr`, camada de chão, canto `(screenX − 11, screenY + 19 − h)`, **modo 5 aditivo**, mesmo contador.
  - `1002`: `Effect\effect_portal3.spr`, camada de topo (depois das entidades), canto `(screenX − 46, screenY + 21 − h)`, **modo 5 aditivo**, mesmo contador.
  - `1003`: `Effect\effect_portal_square.spr`, camada de chão, centrado na célula, opaco, avança a cada 4 passos.
- ✅ **Cursor sobre portal** (`0x00b4e550` linhas 604–606, `0x00b3e3d0` caso 10, `0x00b3ebf0`): quando o mouse passa sobre uma célula com `1 <= cell.Type <= 8999`, o cursor troca para `Interface\cursor_door.spr` (avançando 1 quadro a cada 8 frames a 30 FPS).
- ✅ **Gatilho**: o cliente anda até o portal enviando `C2S 0x20`; o servidor detecta o portal (`ZoneSystem.Move`) e responde com `S2C 0xB0` (`TeleportCharacterPacket`, 8 B: `i32 mapId, i16 x, i16 y`).
- ✅ **Transição de mapa no cliente** (`0x00acb072` → `0x00af0d70` → `0x00afe310` → `0x00a1b670(0xa8)` → `0x00a4a9d7` / `0x00a46e0a` → `0x00aba410`):
  - Mesmo mapa: reposiciona tamer em `(x, y)` e digimon parceiro em `(x − 1, y − 1)` na `FieldCollision` e limpa fila de movimento.
  - Outro mapa:
    1. `0x00af0d70` trava cliques no campo (`+0x772 = 0`), marca transição pendente (`+0x771 = 1`) e **deixa o tamer terminar de andar até a célula do portal** (sem snap instantâneo).
    2. Assim que o tamer para (`state < 4`, `0x00afe310` linha 49), dispara o efeito `0xa8` (`Effect\effect_map.spr`, `0x00a26b72`), desenhado sobre o tamer em `(worldX − cameraX − w/2, worldY − cameraY − h + 100)` em **modo 5 aditivo** (`0x00a46e0a`), com `delay = 1` (2 passos por quadro, `0x00a1be69` + `0x00a4ab1c`).
    3. Ao terminar todos os quadros do `effect_map.spr` (`0x00a4ab91`), executa `0x00aba410`: para a BGM, limpa selvagens e NPCs do mapa anterior, abre a tela de carga (`WorldLoading.BeginMapTransition`, 16 quadros × 90 ms), carrega o novo `.map`, colisão, pathfinder, NPCs e BGM, posiciona tamer/digimon na célula de destino e, ao fim da tela de carga, envia `C2S 0x04` (`RefreshCharacterInfoRequest`) e `C2S 0xB0` (`TeleportAckRequest`, 14 bytes zero — `0x00aba757`).

## Roteiro do que falta
Pontos de entrada já localizados; ✅ quando lido.
- **Minimapa:** `WindowMiniMap`, marcadores em `0x00b9b930` e nomes em `0x00b9c4b0` ([minimapa](cena-jogo.md#minimapa-windowminimap-janela-5)).
- **Botões e dicas da UI:** desenho da barra `0x00b8a850`, tamanho do chat `0x00b8e2a0`, abas `0x00b91a80`; hover, clique e dica ❓.
