# Tela de login do launcher (sprites antigos)

O `DigimonRPG.bin` v322 **não desenha** mais a tela de login. Os sprites dela continuam no `resource.pf` e o nome deles só aparece na lista de pré-carga do `CSceneServer` (construtor `0x00b504b0`, vtable `0x0102f62c`). Não há código com coordenadas do painel: o layout abaixo foi **medido nos próprios sprites** e, onde não há pista, decidido aqui (❓). Formato dos arquivos em [formato `.spr`](formato-spr.md).

## Sprites (todos em `gui\`)
| Sprite | Frames | Tamanho | O quê |
|---|---|---|---|
| `dr2_in_000_000` / `_eng` | 1 | 800×600 | fundo. A versão `_eng` tem o logo "DIGIMON RPG" e não tem a classificação indicativa coreana |
| `dr2_in_003_000` | 1 | 315×212 | painel "LOG IN" com campos ID e PASSWORD e a caixinha "lembrar ID" (13×13 em painel + (24, 164)) |
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
- Mascotes **apoiados** na borda de cima da caixa (pés sobre a borda, y 54–61 do painel), à direita do título: `003_001` em painel + (225, 2) e `003_002` em painel + (270, −2). Os quadros têm linhas transparentes embaixo (o último pixel opaco fica em y 59 e 63), por isso os y diferem. Animados em laço pelos 4 quadros, cada um no seu ritmo: `003_001` a 300 ms por quadro e `003_002` a 200 ms (como no original, nunca andam juntos; definido pelo dono).

### Lembrar ID
A caixinha funciona (pedido do dono): clique na caixa ou no texto ao lado alterna. Marcada, desenha o visto `dr2_sub_001_001` (13×13, amarelo) por cima da caixa, e o texto "Remember ID" fica à direita (x + 17, centrado). ❓ Sprite do visto, texto e cor originais não encontrados (não há código de desenho desta tela; o `DigimonRPG.exe` é empacotado com Themida).

Guardado no `option.ini` do jogo, numa seção nossa: `[DRPGLauncher] Remember=1`, `Login=<id>`. Só o ID, nunca a senha; desmarcar apaga o ID. O ID só é gravado depois de um login bem-sucedido e só se for ASCII imprimível sem `;[]=` (até 64). ❓ O launcher oficial escreve `[Member] id=1`, de significado desconhecido; não é tocado. O jogo grava o `option.ini` chave por chave (`0x00b429f0`, `WritePrivateProfileString`), então a seção nossa sobrevive.

### Desabilitado e cursor
- **Só libera com o servidor conectado** (handshake feito) e nada em andamento (pedido do dono). Fora disso, campos, caixinha e OK ficam **cinza** e não respondem: cópia em tons de cinza do sprite (luminância Rec. 601 × 3/4, `SpriteStore.GuiDisabled`), só nos pixels do campo: preenchimento a partir do centro de cada campo e da caixinha sobre a mesma cor (tolerância 24 por canal), o que mantém os cantos arredondados (`SpriteStore.GuiDisabledRegions`). Texto cinza `#9A9A9A`. ❓ O original não tem visual de desabilitado nesta tela; é do launcher.
- CANCEL, Esc e fechar a janela **sempre** funcionam.
- Falhas que podem ser repetidas (patch, servidor fora, queda durante o login) **tentam de novo sozinhas** após 5, 10, 20 e depois a cada 30 s, com contagem na caixa de status ("... Retrying in 5 s..."). Não há mais "OK para repetir". Launcher desatualizado, versão antiga demais ou chave do servidor diferente não repetem.
- **Cursor do jogo:** `Interface\cursor_normal.spr` (seta roxa com o digivice girando, 8 quadros de 36×36), carregado pelo jogo em `0x00b3ebf0`; `cursor_click` enquanto o botão está apertado. Ponta da seta = pixel (0, 0). Desenhado como sprite por último no quadro; o cursor do sistema fica escondido. ❓ Tempo por quadro (100 ms) e uso do `cursor_click` não confirmados.

### Campos e login
- Campos na parte escura das barras: ID em painel + (138, 83), PASSWORD em painel + (138, 129), 146×18. Texto branco, 3 px de margem; texto maior que o campo mostra o final. Senha como `*`.
- Só ASCII imprimível (sem espaço no ID), até 64 caracteres. Clique escolhe o campo, Tab alterna, Backspace apaga, Enter ou OK enviam, Esc ou CANCEL fecham. ❓ Teclas e limites do original não confirmados.
- Foco inicial no ID, ou na senha quando há ID lembrado.
- OK com campo vazio: "Enter your ID and password." sem falar com o servidor. Durante o envio o painel congela com "Logging in...". Recusa ("Wrong ID or password.", "Too many attempts...", "This account cannot play.", "Server error...") volta ao painel com a mensagem e limpa a senha. Servidor fora: falha e repete o handshake sozinho.
- Sucesso: grava o ID (se marcado), inicia `LanuncherInfo.RunExeFileName` da pasta do jogo com `<usuário do jogo> <chave>` (`GameStarter`; `[Game] Runner=wine` no Linux) e fecha o launcher. Nome do executável, usuário (até 20, ASCII sem espaço) e chave (40 hex) são conferidos antes; erro volta ao painel com a mensagem.

### Fonte
O original usa GDI "굴림체" (GulimChe, cp949) com célula de 12 px para a UI e `Fonts\123RF_123RF_TTF.ttf` (face "123RF", 16 px) em outro lugar (v322 `0x00b563f0`). Gulim não pode ser distribuída, então o launcher usa a do sistema quando existe e, senão, a primeira que achar entre NanumGothic, Noto Sans CJK, DejaVu Sans, Liberation Sans, Arial e Segoe UI (`FontLocator`). Texto por SDL3_ttf (`SdlTtfFont`), em de 12 px (a altura de célula do GDI deixava as fontes substitutas pequenas demais). Sem fonte nenhuma o launcher funciona sem os textos. ❓ Aparência fiel depende da Gulim.
- OK em painel + (148, 170), CANCEL em painel + (206, 170).
- Sem botão sair: o **CANCEL** fecha o launcher (confirmado pelo dono). `dr2_in_000_001`/`_006` não são usados.
- Texto dos campos em branco, a 4 px da borda esquerda; a senha aparece como `*` com cursor `_`.

## Caixa de status
**Só aparece quando tem algo a dizer** (pedido do dono): durante o patch, "Connecting to server...", erros e mensagens do login. Com o login liberado e nada a informar, some. O aviso do modo de desenvolvimento vai só para o console.

Na captura, uma caixa pequena centrada **abaixo do painel**, ~182×24 na captura, mostra o estado em uma linha ("Verifying ID and Password...."). O launcher usa a mesma caixa para tudo:

1. Ao abrir: "Connecting to server..." enquanto faz o handshake. ID, senha e OK ficam desligados até a resposta.
2. Ao enviar: "Verifying ID and Password...".
3. Erros e avisos (credenciais inválidas, muitas tentativas, launcher desatualizado, servidor fora, impressão digital diferente) aparecem nela; os campos voltam a ficar ativos quando dá para tentar de novo.
4. Durante o patch, ❓ a caixa de loading `dr2_in_001_000` com a barra `001_002`.

Sprite da caixa: **`gui\dr_friend_016.spr`** (244×21, cantos redondos, borda clara), escolhido pelo dono em 2026-11-26. Nenhum sprite é igual ao da captura (pode ter sido desenhada por código). Fica centrado em x = 400, a partir de y = 392: em (278, 392). O texto vai em branco, a 8 px da borda esquerda.

**Implementado** em `DRPG.Launcher/Scenes/LoginScene.cs` (fundo, painel, mascotes animados, OK/CANCEL com 3 estados, caixa de status, lembrar ID). Campos ID e PASSWORD com digitação, foco e cursor; texto na caixa de status. Para conferir sem janela: `SDL_VIDEO_DRIVER=offscreen dotnet run --project src/DRPG.Launcher -- --game-dir <instalação> --screenshot tela.ppm`. Um pacote mínimo só com os sprites da tela pode ficar em `.local/client-mini` (fora do git), montado com `work/tools/drpg_pack.py pack`.

## Fluxo do launcher
Definido pelo dono em 2026-11-26:

1. Abre a janela principal (fundo).
2. **Patch:** baixa e aplica os patches pendentes ([configuração](../planos/cliente-novo.md#launcher-login--auto-patcher-marco-1)).
3. **Login congelado:** o painel aparece, mas nada responde a mouse ou teclado. A caixa de status mostra "Connecting to server..." até chegar o handshake.
4. **Login liberado:** campos e botões passam a responder.

Erros em qualquer etapa aparecem na caixa de status. Sem patch pendente, a etapa 2 é pulada.

✅ Implementado em `DRPG.Launcher/LauncherFlow.cs` (etapas `Patching` → `Connecting` → `Ready`, ou `Failed` com OK para repetir a etapa que falhou). Testado de ponta a ponta em 2026-11-26 contra o emulador local e o nginx do devcontainer. Qualquer exceção inesperada vira `Failed` (a tela nunca trava).

**Zip do patch** (igual ao oficial, conferido no `Patch_240.zip`): `patch_N.hf/.pf` são mesclados no `resource`; os demais arquivos substituem os da instalação (caminhos com `..`, absolutos ou `resource.*` são recusados). ❓ O zip oficial traz `DigimonRPG.bin`, que sobrescreveria o binário sem XIGNCODE / o cliente novo.

### Tela do patch
Sprites: `dr2_in_001_000` (239×196; título "Loading" e moldura da barra, só a faixa y 126–190 é opaca) e `dr2_in_001_002` (231×23, a barra; o 2º quadro é vazio). A barra encaixa dentro da moldura em (3, 165) e é desenhada **cortada na largura** = 231 × progresso. `dr2_in_001_001` (65×77, 3 quadros) é totalmente transparente.

**Provisório** (opção A, aprovada pelo dono em 2026-11-26 como primeira versão; ele pode trocar por algo melhor depois): a caixa "Loading" **no lugar do painel de login**, em (280, 124), e a caixa de status abaixo com o detalhe ("Downloading Patch_241.zip 12.3 / 27.1 MB", depois "Applying Patch_241..."). Dentro da barra, o texto curto ("Patch 241 (2/5) 45%"). Ao terminar, a caixa "Loading" some e o painel de login aparece congelado. A outra opção testada (painel visível e "Loading" embaixo dele) foi descartada: o título "Loading" bate nos botões OK/CANCEL.

**Janela fixa em 800×600** (pedido do dono): não redimensiona, porque escala fracionária deforma os sprites. Em tela cheia (`--fullscreen`) ou monitor HiDPI o quadro só cresce por múltiplos inteiros (2×, 3×), centralizado com bordas pretas. Compositores lado a lado (Hyprland) redimensionam a janela mesmo assim, ignorando o tamanho fixo pedido (mín = máx); por isso o renderizador lê o tamanho real a cada quadro e aplica a mesma regra (`Rendering/FrameLayout`): janela maior centraliza em escala inteira, janela menor **corta** o quadro em vez de encolher. Para o Hyprland manter a janela flutuante: `windowrulev2 = float, title:^(Digimon RPG)$`. Vale para o launcher; a regra de janela maior do [cliente novo](../planos/cliente-novo.md) é do cliente do jogo.
