# Janelas do campo, botões e dicas

UI interativa do campo no cliente novo (PR #51, marco 3, passo 2): botões da barra, dicas, gerenciador de janelas, janela de saída (ESC), janela de opções, minimapa e chat. Desenho da barra em [cena de jogo](cena-jogo.md#ui-do-campo-hud). Regras de coordenadas em [fidelidade e resolução](../arquitetura/resolucao.md) (as posições abaixo são do espaço original 800×600, copiadas do decompilado).

## Botão (`0x009b6fc0`)
- ✅ Estado `+0x20`: 0 normal, 1 mouse em cima, 2 pressionado, 3 desabilitado (`0x009b7660`). Mapa de quadros `+0x28` (padrão 0, 1, 2, 3 por `0x009b7670`); o dono pode reescrever (`0x00b91720` põe 3 ou 0 no estado 0 dos botões de menu).
- ✅ **Controle quente** (`DAT_0135a1e4+4`): o último botão (ou janela, `0x00b641b0`) sob o mouse no quadro (`0x00a17880`). A cena o zera no começo do quadro (`0x00b4e550`). O `WM_LBUTTONDOWN` entrega o "pressionar" **só ao controle quente** (`0x00a17790` → vtable `+8` = `0x009b7460`), e o `WM_LBUTTONUP` também (`0x00a177b0`).
- ✅ Quadro (`0x009b7040`): fora → estado 0; botão solto: pressionado dentro → **clique** (`+0x1e`), estado 2; senão estado 1. Botão segurado: dentro e pressionado dentro → 2; dentro sem ter sido pressionado dentro → **estado não muda**. O callback de clique (vtable `+0x14` = `0x00887cf0`) é um `ret`: o botão não toca som.
- ✅ As dicas só aparecem com o botão quente e o mouse dentro (`0x009b7400`), na hora em que o dono desenha (sem atraso nem fila).
- Código: `Ui/Button.cs`, `Ui/MouseState.cs` (`Hot`).

## Dicas
| Função | Caixa | Texto | Onde |
|---|---|---|---|
| ✅ `0x007de1b0(texto, x, y, w, h, margem, alfa)` (e a versão wide `0x007de3f0`) | `Interface\w_panel.spr` recortado a (texto + 2·margem), misturado a alfa/32 | branco **sem sombra** (`0x00b55760` / `0x00b55a10`) em (x + margem, y + margem) | barras HP/VP/EVP/EXP (margem 3, alfa 0x16), botões de tamanho do chat e botões de menu (alfa 0x10) |
| ✅ `0x007dcfb0(x, y, w, h, 0x20)` | 9 partes `GUI\dr2_in_000_003`, opaca | `0x00b55270` branco com sombra | REWARD, WORLD, SHOP, DEPOSITORY (altura 7 = sem linha do meio) |
| ✅ `0x007dd610(texto, x, y, modo, cor)` | caixa de mensagem de uma linha; modo 1 ou 2: canto em (x, y); outro modo: centrada (x − 14 − w/2, y − 14 − h/2) | centrado na caixa | botões de modo do chat (modo 1), canto 30 px acima |
| ✅ `0x007ddb10(x, y, w, h, alfa)` | `w_panel` recortado | — | nomes no minimapa |

- ✅ **Botões de menu** (`0x00b8e220`, depois dos botões laterais e de teletransporte e antes das abas do chat): botão com estado > 0 (mouse dentro, `+0x20`; não pergunta pelo controle quente) → `0x007de3f0(nome, x, y − 20, 0, 0, 3, 0x10)`. Nomes: textos `0x7db + i` (Tamer Info., Digimon Info., Inventory, Option, Digimon List, Party, Encyclopedia, Group), carregados por `0x00b90570` em `+0x771a0` (wstring a cada 0x18 B, 20 textos `0x7db..0x7ee`); texto vazio não desenha.
- ✅ **Barras** (`0x00b8cf05`…): mouse em `x ≤ mx ≤ x + w`, `y ≤ my ≤ y + h` (`0x00b31aa0`, bordas incluídas, quadro 0) → dica em (x, y − h − 12). HP, VP e EVP medem **o sprite do HP** (`+0x1b0`, `0x00b8d05a`, `0x00b8d16c`); EXP mede o próprio (`+0x2ac`). Textos `"HP %d/%d"`, `"VP %d/%d"`, `"EVP %d/%d"` e `0x885` `"EXP %I64d/%I64d %.2f%%"` com o percentual de `0x00b31c00` (float dos 32 bits baixos). `PrintfFormat` aceita os prefixos `I64`, `l`, `h`.
- ✅ **Botões laterais** (`0x00b8d6aa`): cadeia REWARD → WORLD → SHOP → DEPOSITORY (o primeiro quente). REWARD/SHOP: caixa em (x − w/3 − 10, y − 0x1f/0x1a), largura w + 10, texto em (x − w/3, y − 0x17/0x12); WORLD: caixa (x − 0x32, y − 0x1b, 200), texto (x − 0x28, y − 0x12), com **x e y do botão SHOP** (o teste é no `+0x1d18`, mas a posição vem de `+0x1ce0/+0x1ce4`, `0x00b8d7a1`); DEPOSITORY: caixa (x − 0x1e, y − 0x1a, 300), texto (x − 0x14, y − 0x12). Textos `0x886`, `0x888`, `0x10ee`, `0x887`.
- Código: `Ui/Tooltips.cs`.

## Barra (`0x00b8a850`)
- ✅ Botões de menu (`0x00b91720`; dicas em `0x00b8e220`, ver [dicas](#dicas)): janelas `1, 3, 4, 0xf, 2, 0xcc, 0xa8, 0xb`; o clique fecha/abre a janela (o 1º fecha a 2 antes). Só a **0xf (opções)** existe no cliente; as outras não fazem nada, como uma janela que a fábrica não cria. ❓ A mensagem `0x89e` da 6ª (BATTLE ZONE) e o `+0x20` da janela 0xcc esperam essas janelas.
- ✅ Botões laterais na ordem SHOP (`+0x1cd8`), DEPOSITORY (`+0x773f8`), SEAL (`+0x775e0`), WORLD (`+0x1d18`), depois de REWARD. ❓ Os cliques abrem janelas e mandam pedidos que ainda não existem (`0x00ad57c0`, `0x00ae5f80`, janela `0x1ce`, `+0x180` vtable `+8`): só mostram o pressionar.
- ✅ **Teletransporte** (`0x00b8db44`): `GUI\TeleportEasy_Icon24.spr` quadros 0, 1, 2 fixos (`0x009b71e0`) em (615, 65), (615, 92), (615, 119), só com o minimapa mostrando a imagem e aberto, fora da BATTLE ZONE e dos mapas 0x58, 0x76, 0x78..0x7b. ❓ O clique usa itens (`0x238`, `0x23a..0x23c`).
- Código: `Ui/FieldHud.cs`.

## Gerenciador de janelas (`DAT_01158500`)
- ✅ A fábrica (`0x00b930f0`, chamada por `0x00b9a170` para os ids 0..0x1e5) cria todas as janelas na partida; posição e atalhos de `Window_US.dat` (`0x0078ded0`). Lista na ordem de desenho, a última por cima.
- ✅ **Abrir** (`0x00b9a290`): só se existe, está fechada e o campo não está travado (`+0x3eb0`); vira a de cima (`0x00b99fa0`) e toca `Sound\w_on.wav` (efeito `0xe`), menos o minimapa (5) e a 0x1d. **Fechar** (`0x00b9a120`): vtable `+0x1c` e `Sound\w_off.wav` (`0xf`), menos o minimapa. Os sons da cena são registrados em `0x00b4bab0` (`GameSceneSounds`).
- ✅ **Desenho** (`0x00b9a7c0` → `0x00b641b0`): cada janela visível; a janela sob o mouse vira o controle quente. Com o campo travado, só a janela 6.
- ✅ **ESC** (`0x00b4c080`, tecla da cena): nada com o campo travado; senão `0x00b93030` fecha a janela visível mais alta que não esteja na lista (5, 7..10, 0x12, 0x16, 0x21, 0x42, 0x68, 0x3f, 0x123, 0x17b, 0x1ac): manda o ESC para ela (2ª vtable `+4`) e, se ainda visível, chama a vtable `+0x1c` direto (**sem som**). Sem nenhuma, abre a janela 6 com o texto `0xecc`. ❓ Os atalhos de janela (`0x00b9a8d0`) esperam as janelas.
- ❓ Clique no chão bloqueado sob qualquer janela visível (o `0x00b4c1a0` lista janelas uma a uma).
- Código: `Ui/GameWindow.cs`, `Ui/WindowManager.cs`, registro em `GameScene.RegisterWindows` até o gerador de `[Window]` existir.

## Saída, janela 6 (`0x00b80c10`)
- ✅ Vtable `0x01031518`: carga `0x00b81560`, abrir `0x00b814b0`, fechar `0x00b81420`, quadro `0x00b80e80`, tecla `0x00b81450`.
- ✅ Painel `GUI\dr2_sub_007_005.spr` (214×117) centrado: (0x190 − w/2, 0x12c − h/2) = (293, 242). Botões `GUI\dr2_sub_007_001.spr` (45×23): Close (0x34, 0x49), Cancel (0x73, 0x49) e o Cancel da contagem (0x50, 0x49), a partir do canto. Rótulos brancos com sombra em (botão + 8, botão + 6).
- ✅ Pergunta `0xecc` ("Are you sure you want to exit?") centrada (x + w/2 − largura/2) em y + 0x28, cor `0x80ffff`; Close = `0xf1f`, Cancel = `0x516`.
- ✅ **Close**: fecha todas as outras janelas (menos 0xd8), manda o **C2S `0x2B`** (14 B zero; `0x00ae4bf0`; captura: 28 B, resposta S2C `0x2B` vazia), trava o campo (`+0x3eb0 = 1`) e conta **10 s** (`DAT_01011c10`): texto `0xf1c` "Exit after %d second(s)." com `10 − (int)segundos`. No fim, fecha, sai do mundo (`0x00ad0ae0`) e liga `+0x7a4` (`0x00ad0bd0`): o quadro seguinte para a música, fecha as conexões e vai para a **cena 2** (seleção de mundo), não fecha o jogo.
- ✅ **Cancel** (pergunta ou contagem): fecha pela vtable (sem som) e destrava. ❓ O 2º caso abre a janela 0x17b (painéis de missão) e o `0x32` do grupo (`0x00ae2870`) esperam essas features.
- ✅ ESC com a janela aberta a fecha (`0x00b81450`).
- Código: `Features/Exit/ExitWindow.cs`, `ExitActions.cs`; pacote `ExitGameRequest` no Shared.

## Opções, janela 0xf (`0x00b161b0`)
- ✅ Vtable `0x0102d3f8`: carga `0x00b18540`, abrir `0x00b181c0`, quadro `0x00b16980` + `0x00b18000`. O 4º botão de menu a abre.
- ✅ Fundo `GUI\dr2_sub_001_000(_eng)` (212×486) em ((0x320 − w)/2, (0x258 − h)/2 − 20) = (294, 37); quadro 1 misturado a 0x10. Marca de check `dr2_sub_001_001` (13×13), marca de rádio `dr2_sub_001_002` (8×16); a área de clique de cada caixa é o tamanho da marca.

| Controle | Canto (do da janela) | Opção (`DAT_01158530`) | Efeito |
|---|---|---|---|
| Fechar `dr_friend_002` | (0xbe, 6) | — | fecha pela vtable (sem som) |
| Window Mode | (0x1a, 0x3b), área do rádio | `+0x14` (1 na partida) | `0x00aa4440`: janela ou tela cheia (`IDisplayMode`) |
| Shadow / Translucent / Card Slash | (0x3c, 0x3b / 0x49 / 0x59) | `+2` / `+1` / `+3` | `SpriteOptions` na hora |
| Music 0..4 | (0x46 + 30i, 0x93) | `+8` | 0 para a BGM (`0x00b426e0`); outro: volume `(n − 4)·400` cB (`0x00b42460`), e de 0 volta a tocar (`0x00b42670`) |
| Effect 0..4 | (0x46 + 30i, 0xb3) | `+9` | volume dos efeitos (`0x00a18910`) |
| Refuse Whisper / Party | (0x15 / 0x6e, 0xe9) | `+4` / `+5` | guardado |
| Refuse Fight / Trade | (0x15 / 0x6e, 0xff) | `+6` / `+7` | Trade imprime `0xef7`/`0xef8` no chat (amarelo) |
| View My Item | (0x15, 0x115) | `+0xb` | ❓ manda C2S `0x11b` (`0x00ae50c0`), layout sem captura: só a escolha |
| Simplified 0..2 | (0x1b, 0x150 + 19i) | `+0x10` (`Optimization`) | guardado |
| Idioma 0..1 | (0x7c, 0x195 + 19i) | `+0xc` | ❓ trocar pergunta antes (`0x00b176d0`): só a marca |
| Location Sync `dr2_sub_001_005` | (0xb, 0x196) | — | BATTLE ZONE: `0xef6`; ❓ campo: C2S `0x78` (`0x00ae5b30`) e espera de 5 min "m : ss" em (0x29, 0x1a0) |
| Hot Key (H) `dr2_sub_001_003` | (0xb, 0x1c1) | — | alterna a janela 0x55 (não existe ainda) |
| Exit Game `dr2_sub_001_004` | (0x6f, 0x1c1) | — | abre a janela 6 e fecha as opções |

- ✅ Os botões de baixo são desenhados **antes** de ler o mouse; as caixas e rádios leem e depois desenham a marca.
- ✅ **Gravação**: quando o laço de mensagens termina (`0x00aa4a90` → `0x00aa27e0`), `0x00b429f0` grava no `option.ini`, nesta ordem e com `"%d"` (`0x00b32a00` → `WritePrivateProfileStringA`): `[SPRITE]` alpha, anti, shadow, card, Optimization; `[BGM]` bgm; `[GAME]` whisper, party, pvp, trade; `[BGM]` sound; `[LANGUAGE]` language. Window Mode e View My Item não são gravados. O cliente faz o mesmo ao sair do laço (uma escrita atômica, o resto do arquivo intacto; erro de escrita vai para o log). ❓ O laço também alterna o Window Mode com Alt+F12 fora da batalha: espera o teclado ter essas teclas.
- Código: `Features/Options/OptionsWindow.cs`, `GameOptions.cs` (lido do `option.ini` por `0x00b42be0`), `SoundBank.SetMusicLevel`/`SetEffectLevel`.

## Minimapa, janela 5
Desenho e tabelas em [cena de jogo](cena-jogo.md#minimapa-windowminimap-janela-5). Conferido no assembly neste PR:
- ✅ Imagens: além da tabela da página, os mapas 0x33..0x35 usam `w_minimap_2` quadro `mapa − 46` (escondidos por `0x00b9cfe0` de qualquer forma); 37..39 → `mapa − 32`.
- ✅ Sem a opção `alpha`, a moldura fica em y + 15 e a imagem é opaca; com ela, y + 20 e a imagem a 0x18.
- ✅ Botão recolher em (0x30d, 5): `dr2_sub_007_022` aberto, `023` recolhido.
- ✅ Marcadores (`0x00b9b930`): NPC na **célula da linha da tabela** (colunas 4, 5), não na do NPC; portal ligado com o quadro 0. Mouse dentro `[x, x + w) × [y, y + h)` → nome. Posição `(int)((float)célula / células · tamanho)`.
- ✅ Nomes (`0x00b9c4b0`): caixa `w_panel` de 17 px, 3 px mais larga que o nome, centrada no mouse e presa à borda direita (x + w ≤ 800), a 0x10; texto branco sem sombra 2 px dentro; um nome por linha, subindo 17 px.
- ❓ Quem abre a janela 5 na entrada não foi lido: o cliente a abre ao entrar no primeiro mapa. ❓ A área de clique é o título e a moldura (o `+0x78` não foi lido). Clique na imagem = andar (`0x00b9d120`, C2S `0xE3`), fora deste PR.
- Código: `Features/Field/MiniMapWindow.cs`; `NpcDefinition.MiniMapFrame`/`OnMiniMap`; `GameData.MapPortals`.

## Chat (abas, modos e tamanho)
- ✅ Abas (`0x00b91a80`): General e Battle (`+0x4b7b0` = 0/1) usam o quadro 2 quando atuais, 0 senão; Setting usa 2 com a janela 0x10 aberta e o clique a abre; a 3ª ("Watch Battle") **nem lê o mouse nem desenha**. Quadro fixo (sem hover).
- ✅ Modos (`0x00b8fd80`): `000_025` General (0), `000_026` Whisper (1), `000_024` Party (2), `000_027` Shout (3), `000_023` Guild (4); estado 0 com quadro 3, 0 no modo atual. Shout só lê o mouse com GM, classe ≥ 5 ou na BATTLE ZONE; senão desabilitado (`0x009b7660`). Dica `0x88b..0x88f` por `0x007dd610(…, x, y − 30, 1, 0xffffff)`: modo 1, canto da caixa 30 px acima (não centrada). ❓ O clique também foca a caixa de texto.
- ✅ Tamanho (`0x00b8e2a0`): botões normal/max/min (quadros 0, 1, 2 fixos em x 413, 428, 398, y 549); dica `0x890..0x892` 20 px acima; o clique escolhe o tamanho `+0x770a4`: fundo `dr2_main_000_018` em (28, 460) com 81 px, (28, 352) inteiro, (28, 491) com 50 px (`0x00b8f04d`); o topo vale para o clique no chão (`0x00b91ec0`). ❓ As 3 listas (`0xb33a40(0x1f, topo, linhas, 0x14a)`: 463/5, 354/13, 494/3) e a barra de rolagem (`+0xef0`) vêm com as linhas.
- ✅ `ChatLog`: linhas do próprio cliente (`0x00b869a0` → `0x00b32cd0`, cor r, g, b; 0x60 linhas). ❓ O desenho das linhas, a rolagem, a caixa de texto e os pacotes de chat são o próximo passo.

## Entidade sob o mouse
- ✅ `0x00b4dd90`: com o chão como controle quente, a entidade da grade de ocupação na célula do mouse (`DAT_0135b254`; a célula de cima também é dela). `FieldState.Hovered`.
- ✅ **Placa e barra** (parte do hover da passada `0x00a0d340`, depois da fila e dos portais de cima, `0x00a10aae`): cada entidade da fila cujo serial é o sob o mouse define o texto e o lugar; no fim a passada desenha a placa `0x00a11180` e, só para o **digimon líder do próprio jogador**, a barra de HP `0x00a13e60`. Nada aparece com o mouse fora do chão.

  | Fila | Entidade | Texto | Âncora (x, y) da placa | Modo |
  |---|---|---|---|---|
  | 5 | selvagem | `LV%d %s` (nível `+0x82e`, nome completo da `MonsterRegen`) | (x − w/2 − 7, y − altura) | 2: ícone de grau, amarelo `0xffff` (`+0x2830`) |
  | 2 | digimon do tamer | `LV%d %s` | (x − w/2 − 7, y − altura) | 1: branco `0xffffff` (`+0x282c`) |
  | 3 | tamer | `[%s]` (nome) | classe ≠ 0: (x − 13 − w/2, y − altura); 0: mais 7 px à esquerda | 0: ícone de classe, verde `0xff00`; classe 0 → modo 1 |
  | 6 | NPC (menos os ids `0x2b` e `0x49`) | `[%s]` (nome) | (x − w/2 − 7, y − 100), 50 px mais alto no NPC `0x93` | 1 |

  - ✅ Nome do digimon: o `0x11` traz o nome em 21 bytes; a passada procura na `MonsterRegen` (grupo 19, `CTableClass_RegenInfo`, `0x007b19b0`: id, espécie, nome, …) a **primeira linha da espécie cujo nome, nos primeiros 20 caracteres, é o do pacote** e mostra o nome inteiro (`Dukemon : Crimson Mo` → `Dukemon : Crimson Mode`). Com `[LANGUAGE] language` > 1 o texto fica vazio (sem placa). ❓ O texto com dono `'%s'의 %s` é montado e sobrescrito logo em seguida pelo `LV%d %s`; os efeitos de título (`+0x940`, `0x00a09580`/`0x00a09200`, `+0x93c == 0xdc` → `0x00a11cd0`) esperam os títulos. ❓ O `Language\MonsterRegen_US.bin` (só os nomes, um por linha) não é aplicado; na v322 ele é igual à tabela.
  - ✅ **Altura** `+0x18c` (o quanto a placa fica acima dos pés): digimon `0x007f3bf0(estágio)`, refeito a cada quadro (`0x00a0ece5`): espécie mapeada por `0x007e7470` em {0x35, 0x38, 0x3b, 0x3f, 0x41, 0x4e, 0x63, 0x292} → 65, `0xa0` → 130; senão pelo estágio (byte): 1 → 55, 5 → 130, outro → 85. Tamer 100, ou 140 no tamer 8 (`0x00b5bb50`, `0x00864650`). NPC 100 (`0x0085f0de`).
  - ✅ **Placa** `0x00a11180(x, y, texto, ícone, modo)`: w = largura do texto (+14 com ícone ≠ 0), n = (w + 4) / 7; peças 7×7 de `Interface\draw_balloon.spr` misturadas a 16/32 (modo −1 de `0x00b569f0`): linha de cima em y − 7 (quadros 1, 0 × n, 2), duas linhas de quadro 0 em y e y + 7 (n + 2 peças), linha de baixo em y + 14 (3, 0 × n, 4 recortado a 14 × 7). Ícone de grau `Interface\draw_Monster_class.spr` quadro = grau em (x + 3, y − 1) (`0x00a145d0`); ícone de classe `draw_tamer_class` quadro classe − 1 em (x + 8, y − 1) (`0x00a14910`). Texto sem sombra (`0x00b55760`) em (x + 22, y + 1) com ícone, (x + 8, y + 1) sem. ❓ As linhas de guilda (verde, `w_panel` 18 px) e de título (`+0x250`, ícone `draw_guild_emblem*_S`, cor `0x99ffff`) acima da placa do tamer esperam esses dados.
  - ✅ **Barra do líder** (`0x00a10249` → `0x00a13e60` com uma linha no campo): `GUI\dr2_sub_007_014.spr` quadro 0 em (x − w/2, y + 10), quadro 1 em (x − w/2, y + 11) recortado a `(int)((w − 1) · pct / 100 + 1)`, `pct = 0x00b31c00(HP +0x8c0, HP máx. +0x8ec)`. Na batalha a mesma função desenha VP e EVP e o percentual.
  - ❓ **Próximo passo**: a classe do cursor de ataque pela diferença de nível ao líder (`[DAT_011584ec]+0x25c`: ≥ 10 → 1, 7..9 → 2, 4..6 → 3, −3..3 → 4, −6..−4 → 5, −9..−7 → 6, ≤ −10 → 7), os ícones de coleção (`+0x25e`, `0x00a171d0`) e os nomes sempre visíveis dos mapas `0x58`/`0x78..0x7b` (`0x00b37b00`, `0x00b38da0`). Correção: `0x00a10e10` é o **balão de fala**, não o nome.
  - Código: `Features/Field/EntityPlates.cs`, `Startup/MonsterRegenTable.cs`, `SpeciesTables.NameHeight`.
