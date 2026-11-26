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

## Layout (quadro 800×600)
Proporções tiradas de uma captura da tela antiga enviada pelo dono (era "Digimon Battle"; o fundo dela não existe mais na v322). O fundo é o atual.

- Fundo `dr2_in_000_000_eng` em (0, 0).
- Painel `dr2_in_003_000` em (243, 151): centrado na horizontal.
- Mascotes **apoiados** na borda de cima da caixa (pés sobre a borda, y 54–61 do painel), à direita do título: `003_001` em painel + (225, 2) e `003_002` em painel + (270, −2). Os quadros têm linhas transparentes embaixo (o último pixel opaco fica em y 59 e 63), por isso os y diferem. Animados em laço pelos 4 quadros. Tempo por quadro: 300 ms (decidido pelo dono).
- OK em painel + (148, 170), CANCEL em painel + (206, 170).
- Sem botão sair: o **CANCEL** fecha o launcher (confirmado pelo dono). `dr2_in_000_001`/`_006` não são usados.
- Texto dos campos em branco, a 4 px da borda esquerda; a senha aparece como `*` com cursor `_`.

## Caixa de status
Na captura, uma caixa pequena centrada **abaixo do painel**, ~182×24 na captura, mostra o estado em uma linha ("Verifying ID and Password...."). O launcher usa a mesma caixa para tudo:

1. Ao abrir: "Connecting to server..." enquanto faz o handshake. ID, senha e OK ficam desligados até a resposta.
2. Ao enviar: "Verifying ID and Password...".
3. Erros e avisos (credenciais inválidas, muitas tentativas, launcher desatualizado, servidor fora, impressão digital diferente) aparecem nela; os campos voltam a ficar ativos quando dá para tentar de novo.
4. Durante o patch, ❓ a caixa de loading `dr2_in_001_000` com a barra `001_002`.

Sprite da caixa: **`gui\dr_friend_016.spr`** (244×21, cantos redondos, borda clara), escolhido pelo dono em 2026-11-26. Nenhum sprite é igual ao da captura (pode ter sido desenhada por código). Fica centrado em x = 400, a partir de y = 392: em (278, 392). O texto vai em branco, a 8 px da borda esquerda.

**Implementado** em `DRPG.Launcher/Scenes/LoginScene.cs` (fundo, painel, mascotes animados, OK/CANCEL com 3 estados, caixa de status). Falta o texto (campos e status), que depende da fonte. Para conferir sem janela: `SDL_VIDEO_DRIVER=offscreen dotnet run --project src/DRPG.Launcher -- --game-dir <instalação> --screenshot tela.ppm`. Um pacote mínimo só com os sprites da tela pode ficar em `.local/client-mini` (fora do git), montado com `work/tools/drpg_pack.py pack`.

## Fluxo do launcher
Definido pelo dono em 2026-11-26:

1. Abre a janela principal (fundo).
2. **Patch:** baixa e aplica os patches pendentes ([configuração](../planos/cliente-novo.md#launcher-login--auto-patcher-marco-1)).
3. **Login congelado:** o painel aparece, mas nada responde a mouse ou teclado. A caixa de status mostra "Connecting to server..." até chegar o handshake.
4. **Login liberado:** campos e botões passam a responder.

Erros em qualquer etapa aparecem na caixa de status. Sem patch pendente, a etapa 2 é pulada.

### Tela do patch
Sprites: `dr2_in_001_000` (239×196; título "Loading" e moldura da barra, só a faixa y 126–190 é opaca) e `dr2_in_001_002` (231×23, a barra; o 2º quadro é vazio). A barra encaixa dentro da moldura em (3, 165) e é desenhada **cortada na largura** = 231 × progresso. `dr2_in_001_001` (65×77, 3 quadros) é totalmente transparente.

**Provisório** (opção A, aprovada pelo dono em 2026-11-26 como primeira versão; ele pode trocar por algo melhor depois): a caixa "Loading" **no lugar do painel de login**, em (280, 124), e a caixa de status abaixo com o detalhe ("Downloading Patch_241.zip 12.3 / 27.1 MB", depois "Applying Patch_241..."). Dentro da barra, o texto curto ("Patch 241 (2/5) 45%"). Ao terminar, a caixa "Loading" some e o painel de login aparece congelado. A outra opção testada (painel visível e "Loading" embaixo dele) foi descartada: o título "Loading" bate nos botões OK/CANCEL.

**Janela fixa em 800×600** (pedido do dono): não redimensiona, porque escala fracionária deforma os sprites. Em tela cheia (`--fullscreen`) ou monitor HiDPI o quadro só cresce por múltiplos inteiros (2×, 3×), centralizado com bordas pretas. Compositores lado a lado (Hyprland) redimensionam a janela mesmo assim, ignorando o tamanho fixo pedido (mín = máx); por isso o renderizador lê o tamanho real a cada quadro e aplica a mesma regra (`Rendering/FrameLayout`): janela maior centraliza em escala inteira, janela menor **corta** o quadro em vez de encolher. Para o Hyprland manter a janela flutuante: `windowrulev2 = float, title:^(Digimon RPG)$`. Vale para o launcher; a regra de janela maior do [cliente novo](../planos/cliente-novo.md) é do cliente do jogo.
