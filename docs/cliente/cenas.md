# Cenas do cliente (até a seleção de personagens)

Especificação para o marco 2 do [cliente novo](../planos/cliente-novo.md): abrir com `<usuário> <chave>` e chegar na **seleção de personagens** com o comportamento do original. Endereços da v322 ([dump de RE](../ferramentas/dump-re.md)). Páginas de apoio: [carga inicial](carga-inicial.md) (o que a tela de carga carrega), [formato `.ani`](formato-ani.md), [tabelas por espécie](tabelas-de-especie.md), [formato `.spr`](formato-spr.md).

## Sequência
`WinMain` (`0x00aa4a90`) → linha de comando (`0x00aa24b0`) → abre `Resource` e `Table` → janela → DirectDraw → **`Game Init`** (`0x00aa2c10`, com a tela de carga já na tela) → cena **1** `CSceneTitle` → cena **2** `CSceneServer` → cena **4** `CSceneCharSel` → cena **5** `CSceneGame`.

- Linha de comando: separa por espaço e exige **exatamente 2** tokens (usuário, chave); senão, caixa de erro e sai. Guarda em `[DAT_01158504+0xb8]` e `[+0xd0]`.
- ✅ **Troca de cena** `FUN_00b500a0(n)`: 1 = `CSceneTitle` (ctor `0x00b54ae0`), 2 = `CSceneServer` (`0x00b504b0`), 4 = `CSceneCharSel` (`0x00b43f20`, vtable `0x0102ebc4`), 5 = `CSceneGame` (`0x00b494f0`). ❓ 3 não é criada aqui.
  - Ao pedir a cena 2: escolhe a linha 1 da tabela de servidores (`FUN_00b3a950(1, 0)`), conecta (`FUN_00b52460`) e, se conectou, envia o **login `0x05`** com o usuário (`FUN_00b54150`). Ou seja: **o login é feito ao entrar na seleção de mundo**, antes de qualquer clique.
  - Se a cena 2 for pedida com `[DAT_0135b150]+0x690 == 0` (volta do jogo/da seleção de personagens), antes desenha 30 quadros de `FUN_00b34250(i % 12)` com `Sleep(100)` entre eles (3 s). Na primeira vez (`Game Init` põe `+0x690 = 1`) isso é pulado.
    - ✅ `FUN_00b34250` (sprites de `0x00b34980`): `GUI\loading_image_ENG.spr` num quadro sorteado uma vez, em (0, 0); `GUI\loading_under_line.spr` quadro 0 em (0, 550); uma dica = pedaço n (sorteado de 1 a 15, uma vez) do texto `0xa6b` dividido nas quebras `†` (`FUN_0091b7d0`), amarelo com sombra preta, centrado nos 800 px em y 580 (`FUN_00b55560`); `GUI\game_end_gauge.spr` quadro `i % 12` em (740, 540); **sem cursor** (não chama `FUN_00b3e3d0`). Depois recria a cena 2, que faz o login de novo. Implementado em `ReturnLoadingScene` (desenho comum em `LoadingScreen`).
    - ✅ A mesma tela aparece na **entrada no mundo**, desenhada por uma thread (`0x00b34ea0`, 90 ms por quadro, no mínimo 16 quadros em cada uma das duas partes): ver [cena de jogo](cena-jogo.md#tela-de-carga-da-entrada-no-mundo).
- As cenas de carga (`0x00b35560`, `0x00b54cc0`) e seleção de mundo (`0x00b54240`) desenham o texto `Digimon RPG 3.26` no canto (`FUN_00b55270(0, texto, x, 1, 0xffffff, 1, 0)`; x = 0 na carga, 1 na seleção de mundo). ✅ Confirmado no decompilado: `CSceneCharSel` (`0x00b48840`) **não** desenha esse texto.
- `FUN_00b3e3d0`: cursor (`Interface\cursor_*.spr`, carregados em `0x00b3ebf0`). `FUN_00b56010` + `FUN_007dbf10`: fim do quadro (apresentação).
- ✅ 30 quadros por segundo em todas as cenas (`FUN_00867ed0(0x1e)` no `WinMain` e no fim do `Game Init`).

## Interface das cenas (vtables)
Cada cena tem **três** vtables (herança múltipla): a principal, uma em `+0x18` (interface de GUI) e uma em `+0x1c` (teclado). Métodos chamados pela interface `+0x18` recebem `this = cena + 0x18`; por isso, no decompilado, os campos aparecem com 0x18 a menos (ex.: flags `+0x26a8` = cena `+0x26c0`). Nesta página os campos são sempre **da cena**.

| Cena | principal: dtor, init, quadro, liberar | `+0x18`: ?, desenhar GUI, evento(id) | `+0x1c`: tecla |
|---|---|---|---|
| `CSceneTitle` `0x0102f968` | `0x00b54c20`, `0x00b54c60`, `0x00b54cc0`, `0x00b54c50` | — | — |
| `CSceneServer` `0x0102f62c` | `0x00b514c0`, `0x00b53120`, `0x00b54240`, `0x00b52da0` | `0x0102f640`: `0x00b53d40`, `0x00b54310`, `0x00b51950` | `0x0102f650`: `0x00b537a0` |
| `CSceneCharSel` `0x0102ebc4` | `0x00b454d0`, `0x00b47b30`, `0x00b48840`, `0x00b47a80` | `0x0102ebd8`: `0x00b48410`, `0x00b48c60`, `0x00b45500` | `0x0102ebe8`: `0x00b33960` (padrão, sem teclas) |
| `CSceneGame` `0x0102ee7c` | `0x00b498a0`, `0x00b4bab0`, `0x00b4e550`, `0x00b4b4d0` | | |

## Desenho e GUI (o que as cenas usam)
- ✅ **Sprite**: `FUN_00b56ff0(quadro, sprite, x, y, w, h, flags)` (thiscall no renderer `DAT_0135ad88`). `w`/`h` = −1 usam o tamanho do quadro; um `w` menor **recorta** o quadro na largura (é assim que a barra de carga cresce). Quadro fora do intervalo não desenha nada (log `Frame Error`). `FUN_00b569f0(quadro, sprite, x, y, modo, alpha, …)`: desenho com mistura; ✅ modo −1 com a opção `alpha` = `destino = (fonte·a + destino·(32 − a)) >> 5` (detalhes em [cena do jogo](cena-jogo.md#ordem-do-desenho-0x00b8a850-modo-campo-sem-batalha)).
- ✅ **Cache de sprites** `FUN_00b21be0(nome)`; `FUN_008e54c0` acrescenta o sufixo de idioma: `dr2_in_000_006` → `GUI\dr2_in_000_006_ENG.spr` quando existe a versão `_eng` (todos os `dr2_*` com variante `_eng` no `resource` seguem isso).
- ✅ **Texto** `FUN_00b55270(fonte, texto, x, y, cor, sombra, corSombra)`: GDI `TextOutA` alinhado no canto superior esquerdo, fundo transparente; com `sombra = 1` desenha antes o texto em `(x+1, y+1)` na `corSombra` (0 = preto). Cor é **COLORREF** (`0x00BBGGRR`): `0xffffff` branco, `0xffff` **amarelo**, `0x4080ff` laranja. Fonte 0 = a padrão `+0x1dc`: **GulimChe 12 px, normal, charset 0x81** (`0x00b563f0`). `FUN_00b55320` é o mesmo para texto UTF-16 (`FUN_0091ba70`).
- ✅ **Caixa de mensagem** `FUN_007dd610(texto, x, y, modo, cor)`: mede o texto (`FUN_00b561f0` → w, h); se `modo` ∉ {1, 2}, centra: `x0 = x − 14 − w/2`, `y0 = y − 14 − h/2`. Com `n = (w − 24) / 7 + 2`, desenha `GUI\dr2_in_000_003.spr` em 9 partes: linha de cima quadro 0 em `(x0, y0)`, quadro 1 repetido `n + 1` vezes a partir de `x0 + 14` de 7 em 7, quadro 2 em `(x0 + 14 + 7n, y0)`; linha de baixo em `y0 + 14` com os quadros 6, 7 (repetido), 8. Texto em `(x0 + (7n − w + 28) / 2, y0 + 9)` com sombra.
- ✅ **Botão** (classe `0x009b6fc0`, 0x40 bytes): `+8/+0xc` x, y; `+0x10/+0x14` w, h (do quadro 0); `+0x20` estado (0 normal, 1 sob o mouse, 2 pressionado); `+0x28..+0x34` quadro por estado (padrão 0, 1, 2, 3, `0x009b7670`); `+0x1e` "clicou" (vale 1 no quadro do clique; zerado por `0x009b7550`); `+0x39` duplo clique.
  - `0x009b75a0(x, y, sprite)` / `0x009b76b0(sprite)` + `0x009b7570(x, y)`: posição e sprite. `0x009b7610(x, y, w, h)`: retângulo sem sprite (links de texto).
  - `0x009b7140` → `0x009b7040(mouseX, mouseY)`: teste do retângulo `[x, x+w) × [y, y+h)`; fora → estado 0; dentro com botão solto → 1; dentro e o clique foi solto em cima → `+0x1e = 1`, estado 2. ❓ Detalhe do par aperta/solta (`+0x18` vem do tratador de mouse `DAT_0135a1e4`).
  - `0x009b7260`: desenha o quadro do estado; `0x009b71e0(q)`: desenha o quadro `q` fixo; `0x009b7400`: está sob o mouse.

## CSceneTitle (carga) ✅
- **init** `0x00b54c60`: estado `+0x50 = 0x16`.
- **quadro** `0x00b54cc0`: no estado `0x16` chama `0x00ad3d30` (última parte da carga) e passa a `0x17`. Quando a thread da barra marca o fim (`[0x135b150]+0x654 == 1`), troca para a cena **2**.
- **Tela de carga** desenhada pela "PreLoadingBar Thread" (`0x00b35560`, criada em `0x00b34ba0` **no começo do `Game Init`**), 30 quadros/s:
  | Ordem | Sprite | Posição | Desenho |
  |---|---|---|---|
  | 1 | `GUI\dr2_in_000_000_ENG.spr` (fundo) | (0, 0) | quadro 0 |
  | 2 | `GUI\dr2_in_001_000.spr` (caixa "Loading") | (289, 140) | quadro 0 |
  | 3 | `GUI\dr2_in_001_001.spr` | (224, 258) | quadro 0 |
  | 4 | `GUI\dr2_in_001_002.spr` (barra cheia) | (293, 305) | ✅ quadro **0** recortado na largura `w = 1 + (231 − 1) × exibido / 100` |
  | 5 | `GUI\dr2_in_001_002.spr` (contorno) | (293, 305) | ✅ quadro **1** inteiro, por cima |
  | 6 | texto `Digimon RPG 3.26` | (0, 1) | branco, com sombra |
- ✅ **Correção:** o primeiro argumento de `FUN_00b56ff0` é o quadro e o quinto é a largura (ver [desenho](#desenho-e-gui-o-que-as-cenas-usam)). A fórmula `1 + (quadros − 1) × progresso / 100` da versão anterior desta página era a largura (`FUN_00872680` = largura do quadro 0 = 231), não um índice de quadro. O `TitleScene` da fase A desenhava errado (sempre o quadro 1, nunca a barra); ✅ corrigido na fase C.
- ✅ **Progresso exibido** `+0x64c`: a cada quadro, se passaram ≥ 10 ms desde a última subida e `exibido ≤ alvo`, `exibido += v × 0,01 × 0,18`, onde `v = +0x648` (= 39,0, gravado a cada subida do alvo por `0x00b34d40`) e ×7 quando `v < alvo − 1`. Ou seja: **+0,0702 por quadro enquanto o alvo é 39; +0,4914 por quadro quando o alvo passa de 40** (constantes em `0x0102e35c`, `0x0102e360`, `0x0102e370`, `0x0102e368`).
- ✅ **Alvo do progresso** (`+0x644`, só sobe; `0x00b34d40`), na ordem da [carga inicial](carga-inicial.md): 39 → 70 → 90 → 99 (no `Game Init`) → 101 (fim de `0x00ad3d30`). A tela termina quando `[DAT_01158504]+0x81 == 1`, gravado no fim de `0x00ad3d30`, **sem esperar a barra encher**: a duração (~10 s no original) é o trabalho real de carga.

## CSceneServer (seleção de mundo) ✅
Construtor `0x00b504b0` (lista de pré-carga `dr2_in_002_005`, `000_006`, `000_000`, `003_000..004`, `002_000`, `002_001`, `002_006`, `002_007`, `002_010`, `002_008`, `002_009`, `002_002..004`, carregados no fim do init `0x00b53120`). Sprites desenhados em [figura dos sprites](#sprites-dr2_in_002).

### init `0x00b53120`
1. `FUN_00917e00(idioma)`.
2. **BGM**: para a atual e toca `Sound\title.ogg` em laço (`0x00b424a0`, `0x00b42500("Sound\title.ogg", 0)`, volume `option.ini` `[+8]` via `0x00b42460`, `0x00b42670(1)`).
3. **Efeitos** (`FUN_00a187b0(id, arquivo)` registra; `FUN_00a18890(id, 0)` toca): **2 = `sound\menu-bt.wav`**, **3 = `sound\menu-1.wav`**, **4 = `sound\menu-dark.wav`**; volume dos efeitos `option.ini` `[+9]` (`0x00a18910`: 0 = mudo, senão `(v − 4) × 400` centésimos de dB).
4. Duas caixas de edição Win32 (ID em (389, 217) 130×16 até 20 caracteres; senha em (389, 263) 130×16, máscara, até 8) e o texto `0xef3` em `+0x2480`. ❓ São da tela de login antiga dentro do cliente (`0x00b538f0`); com o launcher elas não aparecem. **Não implementar.**
5. `0x00b53c40`: botão **DIGITAL WORLD** (`+0x854`) = `GUI\dr2_in_002_000.spr` em **(120, 144)**; botão **BATTLE ZONE** (`+0x894`) = `GUI\dr2_in_002_001.spr` em **(430, 144)**. ❓ `FUN_00b569f0(2, superfície +0x58, 430, 171, −1, 12, …)` logo antes: efeito desconhecido.
6. Botão **Close/종료** (`+0x794`) = `dr2_in_000_006` (`_ENG`: "Close", 3 quadros) em **(693, 545)**. ❓ `dr2_in_003_003/004` (OK/CANCEL) em (464, 130) e (448, 305): do login antigo, não desenhados.
7. Estado: flags `+0x26c0 = 0`, `+0x78d` (quadro do painel) = 0, posições das artes em `+0x8d4/+0x8d8 = (−800, 800)`, `+0x8e4/+0x8e8 = (−800, 800)`, `+0x8dc/+0x8e0 = (800, −800)` (construtor).

### Flags `+0x26c0`
| Bit | Significado |
|---|---|
| 0 `0x1` | painel aberto: as artes deslizam para dentro; zerado, deslizam para fora |
| 1 `0x2` | mundo DIGITAL WORLD (arte `002_002`, painel `002_006`) |
| 2 `0x4` | mundo BATTLE ZONE (artes `002_003` + `002_004`, painel `002_007`) |
| 3 `0x8` | mundo escolhido: os dois botões grandes somem |
| 5 `0x20` | `0x03` enviado, esperando resposta |
| 6 `0x40` | canal aceito (resposta `0x03` = 2): passa para `0x00b546c0` (autenticação e saída da cena) |
| 7 `0x80` | mostra a mensagem `+0x2468` por 20 quadros |
| 8 `0x100` | `0x04` enviado |
| 9 `0x200` | `0x04` aceito: escurece e vai para a cena 4 |
| 10 `0x400` | resposta `0x03` chegou (libera o envio do `0x04`) |
| 12 `0x1000` | mostra o erro `+0x2480` por 20 quadros |
| 13 `0x2000` | falha ao conectar: mensagem `0x5ee` por 80 quadros |
| 14 `0x4000` | aviso de senha fraca (`0x5fa..0x5ff`, botão `0x600`) |
| 15 `0x8000` | lista de canais do Net Ocean aberta |

### Quadro `0x00b54240` → desenho `0x00b54310`
Ordem por quadro:
1. Limpa a tela; `0x00b524b0` processa as mensagens da rede ([abaixo](#rede-e-mensagens)).
2. Fundo `dr2_in_002_005` (`_ENG`) em (0, 0).
3. Se não está no bit 6:
   - Botão Close: atualiza, desenha, posição (693, 545). Clique → toca **2** e fecha o jogo (`DestroyWindow`).
   - Se não está no bit 3: **`0x00b528e0`** — botões dos mundos: desenha DIGITAL WORLD (120, 144) e BATTLE ZONE (430, 144) com o quadro 0; o que estiver sob o mouse é redesenhado com o quadro 1 e, ao entrar nele, toca **2** uma vez (`+0x78e` evita repetir). Clique em DIGITAL WORLD → toca 2, flags `(f & ~0x4) | 0xb`. Clique em BATTLE ZONE só se `+0x8fc == 1` (ver resposta `0x05`) → seleciona a linha 1, conecta, envia `0x05` (repete o login; ❓ e cria um `CSceneServer` sem uso, bug do original: não copiar), flags `(f & ~0x2) | 0xd`. Os cliques são ignorados com `[DAT_01158504]+0x348 ≠ 0` (sem rede).
   - Se está no bit 6: `0x00b546c0`.
4. **`0x00b51550`** — artes e painéis:
   - Arte DIGITAL WORLD `GUI\dr2_in_002_002.spr` (quadro 0) em (`+0x8d4`, `+0x8d8`), desenhada enquanto o bit 1 está ligado. Aberto: x += 80 até 0, y −= 80 até 0 (entra pelo canto inferior esquerdo em 10 quadros). Fechado: x −= 80, y += 80; ao chegar em (−800, 800) zera os bits 1 e 15, `+0x78d = 0`.
   - Arte BATTLE ZONE: `GUI\dr2_in_002_004.spr` em (`+0x8e4`, `+0x8e8`), mesmo movimento de 80; e `GUI\dr2_in_002_003.spr` em (`+0x8dc`, `+0x8e0`), entrando pelo canto superior direito de 40 em 40 (20 quadros). Cada uma desenha **primeiro o quadro 1 (silhueta) com `FUN_00b569f0(1, …, 0xc)`** e depois o quadro 0.
   - Sons: ao abrir toca **3** (`menu-1`), ao fechar toca **4** (`menu-dark`), uma vez cada (`+0x78f`).
   - `0x00b52b30` desenha o painel do mundo aberto (só com o bit 0):
     - DIGITAL WORLD: `GUI\dr2_in_002_006.spr` quadro `+0x78d` em **(363, 72)** (0 = painel curto, 2 = painel alto da lista de canais).
       - Sem o bit 15: `GUI\dr2_in_002_010.spr` quadro 1 em **(363, 330)**; links "Net Ocean" `0x5da` em (448, 166) (id 102), "Net Ocean Consignment Store" `0x5dd` em (430, 366) (id 210), "Net Ocean Event Channel" `0x5e8` em (430, 398) (id 213).
       - Com o bit 15: "Net Ocean - Channel 2" `0x5e0` em (430, 191) (id 201), "Channel 3" `0x5e1` em (430, 216) (id 202), "Channel 4" `0x5e2` em (430, 241) (id 203), "Channel 1" `0x5e3` em (430, 166) (id 200).
     - BATTLE ZONE: `GUI\dr2_in_002_007.spr` quadro 0 em **(231, 147)**; link "Net Ocean" `0x5da` em (326, 315) (id 102).
   - **Link de texto** `0x00b53db0(slot, idTexto, x, y, recuo, idEvento)`: retângulo = medida do texto na fonte padrão; normal: texto **amarelo** `0xffff` com sombra em (x, y); sob o mouse: texto em (x, y − 1) e os colchetes `GUI\dr2_in_002_008.spr` quadro 1 em (x − recuo, y − 9) (✅ recuos, argumentos das chamadas em `0x00b52b30`: "Net Ocean" do DIGITAL WORLD 30, Consignment Store 8, Event Channel 8, os quatro canais 9, "Net Ocean" do BATTLE ZONE 26). Clique → `evento(idEvento)` (`0x00b51950`).
5. Mensagens centradas (caixa `FUN_007dd610`, branco): bit 13 → `0x5ee` em (400, 260) por 80 quadros; sem rede (`IsNetworkAlive`, `0x00ad2870`) → `0x5ef` em (400, 300) por 100 quadros e bloqueia os cliques; bit 7 → `+0x2468` em (400, 260) por 20 quadros. O contador `+0xae0` é compartilhado; também some ao clicar (`[DAT_011584ec]+0x250`).
6. Depois do desenho da GUI: versão `Digimon RPG 3.26` em (1, 1); **`0x5eb` "ⓒ Akiyoshi Hongo, Toei Animation"** em (5, 585) branco (`FUN_00b55760`, ❓ com fundo/contorno); cursor.

### Eventos `0x00b51950(id)`
Todos tocam **2** e são ignorados com `+0x348 ≠ 0`. "Conectar na linha n" = `[DAT_011584f4]+4 = n`, `FUN_00b3a950(n, 0)` (copia a linha n da tabela de servidores: IP/porta do "Login" e da "Session"), `FUN_00b52460` (log `Login %s %d Session %s %d` e conecta no IP/porta "Login" da linha); se conectou, **envia `0x03`** (`0x00b540a0`, nome `MoveInteractive_Digimon_111`) e liga o bit 5; se falhou, liga o bit 13. Grava mundo/canal em `[DAT_01158504]+0x31e5/+0x31e6`.

| id | Ação (com o bit 1 = DIGITAL WORLD) | (sem o bit 1) |
|---|---|---|
| 1 | fecha o jogo | |
| 100 | escolhe DIGITAL WORLD (como o botão grande) | |
| 101 | escolhe BATTLE ZONE | |
| 102 | abre a lista de canais: `+0x78d = 2`, flags `(f & ~0x4) \| 0x800a` | conecta na linha 7 |
| 200–203 | conecta nas linhas 0–3 (Channel 1–4); `+0x31e5 = 1`, `+0x31e6 = 1..4` | linha 7 |
| 210 | linha 13 (Consignment Store) | linha 7/8 ❓ |
| 213 | linha 16 (Event Channel) | |
| 204–215 restantes | outras linhas (4, 5, 6, 10, 11, 12, 14, 15, 17, 18) para mundos que esta versão não mostra | |

### Teclado `0x00b537a0`
- **Esc** com um mundo escolhido (bit 3): fecha o painel (zera os bits 0 e 3; as artes saem e os botões grandes voltam).
- ❓ Tab/Enter/Esc com o bit 11 são da tela de login antiga.

### Rede e mensagens
`0x00b524b0` esvazia a fila de pacotes (`0x00b3fb30`) a cada quadro, pelo tipo:
- **5 (resposta do login `0x05`)**: `+0x8fc = (segundo campo ≠ 0x68)`. ✅ Isso **habilita o botão BATTLE ZONE**. As capturas oficiais mandam `0x2710` (habilitado); o emulador manda `0x68` (desabilitado).
- **3 (resposta `0x03`)**: liga os bits 7 e 10, guarda o terceiro campo em `[DAT_0135a308]+0x14` (o 4º digimon inicial). Resultado 2 → zera o bit 0, liga o bit 6 e **envia `0x04`** (`0x00b53ee0`: usuário `+0xb8`, chave `+0xd0` e três textos `DAT_0135b298`; liga o bit 8; `Sleep(100)`). Outro resultado → mensagem `0x5f1`.
- **4 (resposta `0x04`)**: resultado 2 → guarda o usuário em minúsculas e liga o bit 9.
  - ✅ **Chave recusada** (resultado 3, exceto 14): o texto vai para `+0x2480` e a conexão é fechada (`0x00b3f8a0`). A cena continua no bit 6 (sem Close nem botões dos mundos; o painel já fechou com o `0x03`), e a cada quadro `0x00b546c0` → `0x00b538f0` desenha `+0x2480` em (400, 260) enquanto não há conexão: **a mensagem fica na tela**. ❓ O clique que sai dela não foi achado no dump; pelo que o dono viu no original, o clique leva à tela de 3 s de `FUN_00b500a0(2)` e de volta à seleção de mundo. Resultado 3 → erro pelo código: 11 `0x5f2`, 10 `0x5f3`, 16 `0x5f4`, 12 `0x5f5`, 13 `0x5f6`, 15 `0x5f7`, 0x7d2 `0x5f8`, 14 liga o bit 14, outros `0x5f9` "Please Log-In through the website. (%d)" e desconecta.
- Com o bit 9 (`0x00b546c0`): `Sleep(100)`, e a cada quadro `escuro += dt × 10` (`0x0102ee70`) e `FUN_00b55f10(escuro)` escurece a tela (subtrai `4·escuro − 1` de cada canal de 5 bits, o verde de 6 bits em dobro); quando `escuro > 15` (`0x0102ee74`), **cena 4**. ❓ `dt` = tempo do quadro em segundos (`FUN_00867e30`): a 30 FPS são ~1,5 s, a tela fica preta em ~0,8 s.
- Liberar (`0x00b52da0`): destrói as caixas de edição, zera as flags; a conexão do channel continua aberta para a cena 4.

### Cruzamento com as capturas e o `AccountSession`
Ordem das capturas: login `0x05` → channel `0x03` → `0x04` → `0x12` + `0x14`. No original: `0x05` ao **entrar** na cena 2; `0x03` ao **clicar num canal**; `0x04` assim que chega a resposta do `0x03`; `0x12` e `0x14` no **init da cena 4** ([abaixo](#cscenecharsel-seleção-de-personagens-)). O `AccountSession` da fase A faz tudo de uma vez; na fase C ele passa a ser dirigido pelas cenas. ❓ Se a conexão do login fica aberta até a do channel (o original usa um socket só, `0x00b3f900`; provavelmente a nova conexão fecha a anterior).

### Sprites `dr2_in_002`
| Sprite | Quadros | Uso |
|---|---|---|
| `002_000` | 243×256, 286×333, 264×307 | botão DIGITAL WORLD (normal, sob o mouse; ❓ 3º) |
| `002_001` | 242×256, 314×269, 314×269 | botão BATTLE ZONE |
| `002_002`, `003`, `004` | 800×599 ×2 | artes de digimon (0 = cor, 1 = silhueta) |
| `002_005` (`_eng`) | 800×600 | fundo |
| `002_006` | 255×256, 355×256, 255×400 | painel DIGITAL WORLD |
| `002_007` | 255×255, 355×255 | painel BATTLE ZONE |
| `002_008` | 147×31 ×2 | colchetes do link sob o mouse |
| `002_009` | 120×22 ×3 | ❓ não visto no código destas cenas |
| `002_010` | 255×256, 255×252 | painel de baixo do DIGITAL WORLD |

## CSceneCharSel (seleção de personagens) ✅
### init `0x00b47b30`
1. **Envia `0x12`** (pedido da lista) e **`0x14`** com o usuário (21 bytes), nessa ordem.
2. Duas caixas de edição em (153, 57) e (522, 57), 130×15, até 20 caracteres: são da criação (nomes do tamer e do digimon). Não aparecem no modo 0.
3. Efeito **5 = `Sound\menu-1.wav`**. **BGM: não muda** (continua o `title.ogg` da cena 2).
4. Botões (`0x009b75a0(x, y, sprite)`):
   | Campo | Sprite | Posição | Função |
   |---|---|---|---|
   | `+0xf80` | `dr2_ch_000_001` | (26, 66) | slot 0 (os 4 slots: mapa de quadros 0, 1, 1, 1, laço em `0x00b48000`) |
   | `+0xfc0` | `dr2_ch_000_002` | (425, 66) | slot 1 |
   | `+0x1000` | `dr2_ch_000_003` | (26, 301) | slot 2 |
   | `+0x1040` | `dr2_ch_000_004` | (425, 301) | slot 3 |
   | `+0x1080` | `dr2_ch_000_005` (`_ENG` "Delete Character") | (26, 565) | apagar |
   | `+0x10c0` | `dr2_ch_000_006` ("Create Character") | (134, 565) | criar |
   | `+0x1400` | `dr2_ch_000_009` ("Exchange Password") | (246, 565) | trocar senha |
   | `+0x1100` | `dr2_ch_000_007` ("Game Start") | (520, 549) | entrar |
   | `+0x1140` | `dr2_ch_000_008` ("Back") | (681, 549) | voltar |
   | `+0x1380`, `+0x13c0` | `dr2_ch_001_001` / `001_002` | (565, 549), (681, 549) | botões da criação (modo 1) |
   - ✅ Os slots têm só 2 quadros: nos slots 1–3, o estado pressionado pede o quadro 2 e **não desenha nada** naquele quadro (mapa padrão). Fiel ao original.

### Quadro `0x00b48840`
1. Limpa; processa a rede (`0x00b45c30`).
2. Modo `+0x85c`: 0 = seleção (`0x00b48580` → desenho da GUI `0x00b48c60` e slots `0x00b47790`); 1 = [criação](#criação-de-personagem-modo-1--re).
3. Mensagens centradas: `0xee4` "The Tamer name already exists." 160 quadros; `0xee6` (manutenção) 80 quadros; `0xee5` até clicar; `+0x144c` (erro da criação) até clicar.
4. Sem rede (`IsNetworkAlive`) → desconecta e volta para a cena 2. Erro fatal do servidor (`[DAT_011584f0]+0xf802`) → `0x602` + texto, caixa em (400, 300).
5. Cursor, fim do quadro.

### Desenho `0x00b48c60` (modo 0)
1. Fundo `dr2_ch_000_000` (`_ENG`) em (0, 0).
2. Para cada slot i com personagem (`uid ≠ 0`): atualiza o botão; o selecionado (`+0x860 == i`) desenha o quadro 1 fixo, os outros o quadro do estado. **Clique** num slot com tamer (byte `+4` do registro ≠ 0) e diferente do atual → seleciona, toca **5**, põe tamer e digimon na animação "selecionado" (5) a partir do início. Clique em slot vazio → `+0x860 = −1`. **Duplo clique** no selecionado → entrar no jogo.
3. Botões, na ordem: Delete (com seleção → [diálogo de confirmação](#apagar-personagem--re)); Create (só fora da Battle Zone; → [modo 1](#criação-de-personagem-modo-1--re), `0x00b45510(1)`); Game Start (com seleção e a lista recebida → entrar); Back → desconecta e **cena 2**. Trocar senha: ❓ tratado fora deste método (`0x00b46690`/`0x00b46f00`), próximo marco.
4. Entrar no jogo (`0x00b48670`, log `Map Server : %s %d`): escurece como na cena 2 (`+0x14c0`, mesma fórmula) e no fim conecta no servidor de mapa e vai para a cena 5. **Fora do marco 2**: a fase C para no escurecimento + log.

### Slots `0x00b47790`
Registro do slot = os **124 bytes do slot do `0x12`** como vêm no fio (`CharacterListSlot`): `+0` uid, `+4` tamer, `+5` nome[21], `+0x1a` nível, `+0x1c/+0x1e` x, y, `+0x20` equipamento (11 × u32), `+0x4c` espécie do líder, `+0x4e` nível do líder, `+0x50` nome do líder[24], `+0x68` total de batalhas, `+0x6c` vitórias, `+0x70` slot, `+0x71` mapa. Chegada do `0x12` (`0x00b45c30` caso 0x12): modo 0, seleção −1, `+0x862 = 1`; nos mundos Battle Zone (linhas 7–12) o nível é trocado por 10/10. Até lá os slots ficam vazios.

Para cada slot com tamer (`+4 ≠ 0`):
- **Textos** (`0x00b47350`, branco com sombra, fonte padrão). Origem: slot 0 (231, 110), slot 1 (449, 110), slot 2 (231, 340), slot 3 (449, 340). Slots 0/1 (tamer em cima), a partir de `y`:
  | y | Texto |
  |---|---|
  | +0 | nome do tamer |
  | +18 | `0xef1` "Level %3d" (nível do tamer) |
  | +36 | nome do mapa (`Map_US` pelo byte `+0x71`, `0x00b395a0`) num retângulo (x, y+36)–(x+130, y+72), quebra em 140 px (`0x00b30d00`) |
  | +54 | `(%d,%d)` com x, y |
  | +108 | nome do digimon |
  | +126 | "Level %3d" (nível do digimon) |
  | +144 | `0xef2` "%d Win %d Lose %0.2f%%" com (vitórias, **total de batalhas**, vitórias/total × 100; 0 se total = 0) |
  Slots 2/3 (digimon em cima), a partir de `y`: digimon nome `+10`, nível `+28`, vitórias `+46`; tamer nome **`+100`**, nível `+118`, mapa `+136` (retângulo até `+172`), `(x,y)` `+154` (✅ `0x00b47350` linhas 31–53).
  - ✅ O original escreve o **total de batalhas** no lugar de "Lose". Fiel.
- **Digimon** (`0x00b46120`): sprite = `.ani` da espécie do líder (espécie → id de sprite → `Sprite\%03d_1.ani`, ver [tabelas por espécie](tabelas-de-especie.md)); desenha o quadro atual em (x + dx, yBotão + dy), com (dx, dy) da tabela por espécie (padrão (−40, −14)). x = xBotão, **+150 nos slots 1 e 3**: o `0x00b47790` soma os 150 (`0x00b4782c`) no x que passa **para o digimon e para o tamer** (antes da correção só o tamer andava, e o digimon dos slots da direita caía em cima dos textos).
- **Tamer** (`0x00b46d50` → `0x0099fb90`, boneco de papel): corpo + roupas, todos pelo **canto superior esquerdo** do quadro em (x + 70 − 120, yBotão + 164 − 162), com o mesmo x do digimon (+150 nos slots 1 e 3) e **o mesmo número de quadro** do `.ani` do corpo (a âncora do `.ani` não entra). Com a opção de sombra (`[DAT_01158530]+2`), antes desenha a camada de sombra do corpo (`FUN_00b57460(…, 0xe)`, 14/32) no mesmo canto ✅ (implementada em `TamerDoll.Draw`, igual no campo: [cena do jogo](cena-jogo.md)).
  - ✅ Arquivos (`0x00b21cd0`, tamers 1–4): corpo `Sprite\a_%02d.spr`/`.ani` de `id − 1`; roupas padrão `Sprite\a_NN_%02d_00.spr` de `id`, cada uma num mapa (`DAT_010e84xx`): `a_15` `+8414`, `a_16` `+841c`, `a_17` `+8424`, `a_19` `+842c`, `a_20` `+8434`, corpo `+843c`, `a_21` `+8444`, `a_18` `+844c`. Arquivo que não existe (ex.: `a_20_03_00`) fica vazio e não desenha.
  - ✅ Ordem com os 11 equipamentos da lista zerados (sempre, nas capturas): corpo (`0x00b1d0f0`), `a_15` (`0x00b1e630`), `a_16` atrás (`0x00b1e460`), `a_17` (`0x00b1e120`), `a_16` de novo na frente, `a_18` (`0x00b1da70`), `a_19` (`0x00b1e2c0`), `a_20` (`0x00b1cf10`), `a_21` (`0x00b1df90`).
  - ❓ Equipamento diferente de zero: cada função troca a roupa padrão pelo sprite do item (`FUN_0078ac80(categoria 0xf..0x15/0x1c, id)`, `Sprite\%s.spr` com `%d` do tamer, chave `tamer + id × 10`). Não implementado (as listas não trazem equipamento).
- **Animação** de ambos: animação 1 (parado) direção 0; ao selecionar, animação 5 direção 0. O quadro avança **a cada 3 quadros** do jogo; quando `quadro ≥ início + contagem − 1`, volta ao início (✅ o último quadro da animação nunca aparece). Ao perder a seleção, volta para a animação 1 (o quadro continua de onde estava até o próximo laço). Início/contagem vêm da tabela do [`.ani`](formato-ani.md).

### Sprites `dr2_ch_000`
`000_000` fundo 800×600 (4 molduras com retrato e quadros TAMER/DIGIMON); `000_001..004` slots ~355×230, 2 quadros (normal, selecionado/sob o mouse); `000_005..009` botões, 3 quadros (normal, sob o mouse, pressionado). Criação (próximo marco): `dr2_ch_001_000` fundo, `001_001/002` botões, `001_003` (107×154) retratos dos 4 iniciais em (73, 188), (254, 188), (439, 188), (619, 188); iniciais = espécies 10, 11, 12 e o 4º pelo `0x03` (0 → 24, 1 → 27, …).

### Criação de personagem (modo 1) ✅ RE
Offsets de cena (no `0x00b48c60` o ECX é `cena + 0x18`: offset local + 0x18 = offset de cena). Cores são COLORREF.

**Entrada `0x00b45510(1)`** (botão Create, que fica desabilitado nas linhas 7–12 da Battle Zone e nas linhas 0/4/5 quando `[DAT_011584f4]+8 == 1`):
1. Limpa os dois nomes. Slot alvo `+0x860`: o selecionado se estiver vazio, senão o **menor slot vazio**; sem nenhum → mensagem `0x264` "No empty tamer creation window." (até clicar) e não entra.
2. Tamer selecionado `+0xc72 = 1`, digimon selecionado `+0xc74 = 10` (Gigimon). Rolagens das descrições `+0x1468/+0x146c = 0` (✅ alteradas pela roda do mouse em `0x00b48420`, ±5 px por passo dentro das caixas de descrição).
3. Digimons dos 4 botões `+0xe78[0..3] = 10, 11, 12, X`, com X pelo `[DAT_0135a308]+0x14` (o 4º inicial do `0x03`): 0 → 24 (Chibimon), 1 → 27 (Yarmon), 2 → 31 (Hopmon), 3 → 40 (Dorimon); fora disso o 4º botão não aparece.
4. Animações: tamer 1 e digimon 10 já em "selecionado" (animação 5, do início); os outros em 1 (parado).
5. Foco na caixa do nome do tamer; modo `+0x85c = 1`. Sair (`0x00b45510(0)`): modo 0, `+0x860 = −1`.

**Quadro (modo 1, `0x00b48840`)**: GUI (`0x00b48c60` ramo do modo 1) → coluna dos tamers `0x00b46f00` → coluna dos digimons `0x00b46690` → caixas de texto `0x00a19680` → mensagens (as mesmas do modo 0: `0xee4` 160 quadros ou clique; `+0x1464`/`+0x144c` até clicar) → cursor.

**GUI** (`0x00b48c60`, modo 1):
- Fundo `dr2_ch_001_000` (`_eng`) em (0, 0).
- **OK** `dr2_ch_001_001` (`_eng`) em (565, 549), habilitado com a lista recebida e tamer e digimon escolhidos. Validação, nesta ordem; o primeiro erro põe a mensagem (centrada em (400, 300), até clicar) e o foco na caixa culpada:
  1. Algum byte não-ASCII em **qualquer** dos dois nomes → `0xee5` "Please rename it properly in English." (foco no tamer).
  2. Nome do tamer vazio ou com caractere fora de `0-9A-Za-z` → `0x25e`.
  3. Nome do tamer contém palavra proibida → `0x25f`. Regra (`0x00b3db80`): **cada substring** do nome, em maiúsculas, procurada no conjunto de `ProhibitedWords_US` (mesmo formato e mesma carga do `TextAttach`: `table\ProhibitedWords_US.dat` e `Language\ProhibitedWords_US.bin`, UTF-8 com BOM, `\r`, `id|PALAVRA`, 399 linhas na v322).
  4. Tamanho em bytes do nome do tamer ≤ 2 ou ≥ 17 → `0x260` (vale 3..16).
  5. Os mesmos três para o nome do digimon → `0x261`, `0x262`, `0x263`.
  6. Tudo certo → envia (`0x00b45990`, abaixo).
- **Cancel** `dr2_ch_001_002` (`_eng`) em (681, 549) → limpa os nomes e `0x00b45510(0)`.
- **Caixas de texto** (`0x00a19680` → `0x00a19040`): tamer em (153, 57), digimon em (522, 57), 130×15, até 20 caracteres; texto branco com sombra preta na fonte padrão (`FUN_00b55270`) e cursor piscando quando focado; aceitam o que o IME manda (por isso existe o erro 1).

**Coluna dos tamers** `0x00b46f00`, para i = 0..3 (botão `dr2_ch_001_003` em (73, 188), (254, 188), (439, 188), (619, 188); tamer do botão = `+0xe98[i]`, 1..4):
- Atualiza o botão (`0x009b7140`); **só o tamer selecionado** desenha o quadro 0 fixo (`0x009b71e0(0)`); os demais não desenham nada (sem efeito de hover).
- Clique (conectado `+0x863`) → `+0xc72 = tamer`, tamer na animação 5 do início, toca o efeito **5**, rolagem 0. Tamer que deixa de ser o selecionado volta para a animação 1.
- Animação igual à dos slots: 1 parado / 5 selecionado, direção 0, avança a cada 3 quadros, volta ao início quando `quadro ≥ início + contagem − 1`.
- Se este é o selecionado (ou nenhum está selecionado, `+0xc72 == 0`, que mostra o texto 0): nome `0xee8[tamer]` centrado em 130 px a partir de x = 18 (`x = 18 + (130 − w) / 2`), y = 60, cor `0x63cbff`, fonte padrão, **sem sombra** (`0x00b55900`: `TextOutA` com fundo transparente); descrição `0xee9[tamer]` em linhas (quebra por `\n`), x = 24, y = 84 + 14·linha, branca sem sombra, recortada ao retângulo (24, 83)–(279, 144) (`0x00b55bb0`).
- Boneco: `0x0099fb90(tamer, xBotão + 58, yBotão + 126, quadro…)`, ou seja canto superior esquerdo em **(xBotão − 62, yBotão − 36)** pela mesma regra do boneco dos slots (−120, −162).

**Coluna dos digimons** `0x00b46690`, para i = 0..3 (botão `dr2_ch_001_004` em (126, 394), (273, 394), (419, 394), (565, 394); o 4º só existe com o 4º inicial):
- Botão e clique como os tamers (só o digimon selecionado desenha o quadro 0 fixo; os demais não desenham nada): `+0xc74 = espécie`, animação 5 do início, efeito 5, rolagem `+0x1468 = 0`.
- Sprite = o `.ani` da espécie (mesmo caminho dos slots, `0x00b47b00`), quadro atual pelo canto superior esquerdo em (xBotão + dx, yBotão + dy): espécie 27 (−156, −148); 31 (−156, −165); 40 (−156, −160); 42 (−106, −82); as outras (10, 11, 12, 24) **(−110, −70)**. Animação 1/5 e avanço como os tamers.
- Se selecionado: índice n = espécie − 9 (10..12 → 1..3), 24 → 4, 27 → 5, 31 → 6, 40 → 7, 42 → 8. Nome `0xeeb[n]` centrado em 130 px a partir de x = 658, y = 60, cor `0x63cbff`; descrição `0xeec[n]` em linhas, x = 522, y = 84 + 14·linha, branca, recortada a (522, 81)–(780, 144).
- Nenhum digimon selecionado (`+0xc74 == 0`, não acontece na prática): `0xeec[0]` com `DrawTextA` em (522, 83)–(780, 510), `DT_WORDBREAK`, branco.

**Listas de texto** (init da cena, `0x00b44738..`; o carregador já trocou `†` por `\n`): `+0xf40` = `0xee8` partido por `\n` (nomes; o 0 é vazio), `+0xf4c` = `0xee9` partido por **`-\n`** (0 = "Please select a Tamer", 1..4 descrições), `+0xf58` = `0xeea` (habilidade de carta, carregado mas **não desenhado**), `+0xf64` = `0xeeb` por `\n` (nomes, 0 vazio, 1..8), `+0xf70` = `0xeec` por `-\n` (0 = "Select Digimon", 1..8). ❓ A última descrição de `0xee9` termina em `-` sem `†`: o pedaço fica com o `-` no fim (fiel).

**Envio `0x00b45990`** (só com nome do tamer não vazio, slot ≠ −1, tamer e digimon escolhidos): `CharacterCreateRequest` com `i32 1, i32 0`, cabeçalho **slot = `+0x860`, mapa = 0x4F, A = 62 − rand()%4, B = 55 + rand()%4** (dois `rand()` nessa ordem; bate com 3B..3E e 37..3A das capturas), tamer `+0xc72`, nome do tamer[21], digimon `+0xc74`, nome do digimon[26]. Se o digimon não for 10/11/12/o 4º inicial → `MessageBoxA` e fecha o jogo (antitrapaça; impossível pela GUI).

**Resposta `0x10`** (`0x00b45c30`): só trata o erro **102** (`NameTaken`): mensagem `0xee4` (160 quadros ou clique), limpa a caixa do nome do tamer e põe o foco nela. Sucesso não faz nada aqui: o servidor manda a **lista `0x12`** em seguida, cuja chegada põe o modo 0 e a seleção −1 (✅ o emulador já faz assim).

### Apagar personagem ✅ RE
**Diálogo** (objeto na cena `+0x24`, classe vtable `0x01035d00`, ctor `0x00c09e00`, base de janela `0x00b613f0`): estado `+0x308`, aberto `+0x30c` (é o `+0x318` local / `+0x330` de cena que trava os botões), caixa de texto `+0x200`, MD5 `+0x208`. Fundo de todos os estados = `0x007dcfb0(x, y, w, h, 0x20)`: 9 partes de `GUI\dr2_in_000_003.spr` (a mesma da caixa de mensagem), cantos de 14×14, laterais de 7 em 7: com `n = (w − 14) / 7` e `m = (h − 28) / 14 + 1`, linha de cima quadro 0 em (x, y), quadro 1 × (n + 1) a partir de x + 14, quadro 2 em `x + 14 + 7n` (✅ confirmado em `0x007dcfb0` linha 74, igual ao `0x007dd610`); m linhas do meio em y + 14k com 3, 4 × (n + 1), 5; linha de baixo em y + 14(m + 1) com 6, 7, 8. Botões são retângulos (`0x009b7610`) com o rótulo desenhado no canto (x, y) do botão: branco na fonte padrão; **sob o mouse, laranja `0x4080ff`** (`0x00b55640`, sem sombra; `+0x200` não é inicializado em `0x00b563f0`, mantendo a fonte padrão). Esc fecha (`0x00c0a540`); Enter só age no estado 0.
- **Botão Delete** (`dr2_ch_000_005` em (26, 565); com seleção, lista recebida e sem diálogo aberto): `+0xa44 = 1` (❓ sem leitor encontrado), abre (`0x00c0a780`, estado 0), cria a caixa de texto (`0x00c0a690`: em (x + 70, y + 45), 100×15, até 15 caracteres, mascarada), limpa e na v322 vai direto para o estado 2 (`0x00c0a5c0`). ✅ Quando o servidor desabilita a senha secundária (`Game:SecondaryPasswordMode = Disabled`, padrão v322) ou a conta não cadastrou (modo `Optional`), pula o estado 0 e envia MD5 de `""`; quando a conta tem senha secundária, abre o estado 0 primeiro.
- **Estado 0** (senha secundária): caixa (290, 255) 220×90, `0xf79` "Please enter the secondary password." em (305, 275), caixa mascarada em (360, 300) 100×15 até 15 caracteres, Check (345, 325) 45×13 ou Enter → estado 2, Cancel (428, 325) 45×13 → fecha.
- **Estado 2** (confirmação): caixa **(310, 265) 180×70**; `0xf7d` "Are you sure you want†to delete?" partido no `†`: linha 0 em (340, 285), linha 1 em (370, 300), brancas (`0x00b55900`); **Yes** `0x551` em (355, 315) 45×13; **No** `0x552` em (428, 315) 45×13. Yes → MD5 do texto da caixa (`0x00869140`; vazio → `d41d8cd98f00b204e9800998ecf8427e`, hex minúsculo) e `0x00b46020`: `CharacterDeleteRequest(uid do slot +0x860, md5)` (só se o slot tem nome); fecha. No → fecha.
- **Estado 1** (senha errada): caixa (295, 265) 210×70, `0xf7c` "Secondary password mismatch." em (310, 285), Check `0x515` em (390, 315) 45×24 → fecha.
- **Resposta `0x11`** (`0x00b45c30`), com resultado 3 (falha): erro **2000** → estado 1 e recria a caixa; erro **105** → mensagem `0xeb1a` "Your Consignment Store is currently active." (`+0x1464`, até clicar). Sucesso: nada aqui; a lista `0x12` que o servidor manda em seguida atualiza os slots (modo 0, seleção −1). ✅ O emulador já manda resposta + lista, valida a senha secundária conforme `Game:SecondaryPasswordMode` e `CharacterDeleteReply.Read` expõe o código de erro.
- 🔒 A senha (mesmo vazia) e o MD5 nunca vão para log (`CharacterDeleteRequest.ToString` já esconde).

### Senha secundária (Exchange Password) ✅ RE
Classe em `0x00c95000`–`0x00c96f90` (objeto na `CSceneCharSel + 0x334`, aberto quando `+0x538 != 0`).
- **Gatilho (`0x14` S2C, `0x00b45c30` linhas 168–178):** o byte 21 de `UserCheckReply` informa o estado da senha secundária (`0` = desabilitado/v322, `1` = obrigatório sem cadastro → abre automaticamente o Estado 3, `2` = cadastrada, `3` = opcional sem cadastro).
- **Configuração no servidor (`Game:SecondaryPasswordMode`):** `Disabled` (padrão, 1:1 com a v322: botão `Exchange Password` oculto, sem prompt), `Optional` (sem prompt automático; o jogador pode cadastrar/trocar clicando em `Exchange Password`, e após cadastrar a exclusão exige a senha) e `Required` (cadastro obrigatório ao entrar na seleção de personagens).
- **Estados (`0x00c950d0`, `0x00c96ee0`, `0x00c96ae0`):** Estado 1 (trocar: 3 caixas mascaradas, 4–15 caracteres, letra+dígito via `0x00c95000`, Check `0x515` / Cancel `0x516`), Estado 3 (cadastrar: 2 caixas mascaradas, Check `0x515`, Cancel no modo opcional), Estados 2/4 (validação `0x54b..0x553`/`0x55c`/`0xead7` ou confirmação Yes/No `0x550`), Estado 5 (resposta `0x15` S2C: `1` → `0x55d`, `2` → `0x55e`, `3` → `0x55f`). `Tab` alterna o foco (`0x00c968b0`).
- **Protocolo `0x15` (`0x00ae6070`):** C2S de 142 bytes (`u8 0`, `username[21]`, 3×40 bytes de MD5 hex minúsculo — no cadastro inicial o campo antigo envia `TextAttach[0x55b]`); S2C de 1 byte (`1` sucesso, `2` nova inválida, `3` antiga inválida). O servidor armazena PBKDF2-SHA256 sobre o MD5 (migração `0016_secondary_password.sql`) e nunca registra senha ou hash em log.

## Texto, som e fontes ✅
- **Textos da interface** `FUN_0091b950(id)` (ANSI) e `FUN_0091ba70(id)` (UTF-16): tabela **`TextAttach`** (loader de tabela 0x1a, `0x0091bf90`; se não carregar: `TextAttach Not Load!`). Id ausente → `Null Text`.
  - Fonte dos dados: `table\textattach_us.dat` do `table.hf` (contagem u32 + `{u32 len, utf16 texto}` por linha, ver `drpg_pack.py`) e por cima `Language\TextAttach_US.bin`: **UTF-8 com BOM, linhas separadas por `\r` (só CR), `id|texto`**. Na v322 os dois têm as mesmas 3214 linhas. `†` dentro do texto: ❓ quebra de linha.
  - Ids usados aqui: `0x5da..0x5ea` nomes dos servidores, `0x5eb` copyright, `0x5ee/0x5ef` conexão, `0x5f1..0x5f9` erros do `0x03`/`0x04`, `0x5fa..0x602` avisos, `0xee4..0xee6`, `0xef1` "Level %3d", `0xef2` vitórias, `0x25e..0x264` criação, `0xef3` "You are disconnected with server.".
- **Efeitos**: não há tabela fixa id → arquivo. Cada cena registra os seus com `FUN_00a187b0(id, caminho)` (790 posições, `0x00a18890(id, 0)` toca). Barra de carga: 5 = `sound\menu-bt.wav`; seleção de mundo: 2/3/4 (acima); seleção de personagens: 5 = `Sound\menu-1.wav`. ✅ **Correção:** `FUN_00b3a950(n, 0)` **não é som**: copia a linha n da tabela de servidores.
- **BGM**: `0x00b42500(caminho, 0)` no gerenciador `DAT_0135a1f0`.
- ✅ **Altura do texto:** o original mede e posiciona tudo com a célula GDI do GulimChe 12 px (12 de altura; ❓ ascendente 10). A fonte substituta (Noto Sans CJK no Linux: linha de 17 px, ascendente 14) é desenhada com a linha de base onde ficaria a do GulimChe e medida com 12 de altura (`UiText.CellHeight/CellAscent`); antes o texto ficava 3–4 px baixo dentro das caixas de mensagem.
- Fontes GDI criadas em `0x00b563f0`: "굴림체" (GulimChe, charset 0x81) — `+0x1dc` 12 px normal (a padrão), `+0x1e4` 14 px negrito, e outras (14/12/11/10, 13×5, 32×24, 15); `Fonts\123RF_123RF_TTF.ttf` (face "123RF", 16 px). Ver [fonte do launcher](tela-login-launcher.md#fonte).

## Implementação (fases C e E) ✅
Código em `src/DRPG.Client` (`Scenes/`, `Ui/`, `Startup/`, `Text/`, `Data/`) e `DRPG.Engine` (`SoundBank`, `Darken`). Tudo acima foi implementado como especificado; abaixo, o que ficou ❓ ou diferente.

- ✅ `SceneId`: Title=1, Server=2, CharacterSelect=4, Game=5 (a cena 5 ainda não existe: "Game Start" faz o fade e registra `Map Server : …`).
- ✅ Carga: mesmos passos e ordem, alvos 39/70/90/99/101, 39 tabelas, listas de pré-carga embutidas (`Startup/preload-*.txt`), sem espera artificial. O tempo fica na ordem do original (~9 s nesta máquina). ❓ **Paralelismo:** o original lê em uma thread; os 6,3 GB de sprites inflados levariam ~27 s assim, então o pré-carregamento usa 2–4 trabalhadores (`StartupLoader.Parallelism`; 1 = estritamente uma thread) e só guarda sprites de até 1 MiB decodificados (o resto é lido e descartado).
- ✅ `AccountSession` dirigido pelas cenas: login ao entrar na cena 2, canal ao clicar, `0x04` ao receber o channel, `0x12`/`0x14` ao entrar na cena 4, criação `0x10` e exclusão `0x11` (com atualização da lista `0x12` em seguida). Chave recusada volta para a seleção de canal com a mensagem.
- ✅ Fade `FUN_00b55f10` (`Ui/FadeOut`, `IRenderer.Darken` com subtração de blend), caixa de mensagem/diálogo de 9 partes (`MessageBox.Draw` e `DrawBox` de altura variável), botão (estados 0/1/2, duplo clique 500 ms / 2 px), texto GulimChe 12 px com sombra e recorte, `TextAttach` (`table\textattach_us.dat` + `Language\TextAttach_US.bin`), `ProhibitedWords` (`table\ProhibitedWords_US.dat` + `Language\ProhibitedWords_US.bin`), `EditBox`, `SoundBank` (790 efeitos, BGM em loop, volumes do `option.ini`).
- ✅ **Fase E (criação e exclusão):** `CharacterCreatePanel` (modo 1: slot alvo e `0x264`, 4º inicial via `AccountSession.FourthStarter`, colunas de tamers e digimons com animações 1/5, nomes centrados `0x63cbff`, descrições recortadas, validação pura em `CharacterNameRules` na ordem exata `0xee5`/`0x25e..0x263`, envio `0x10` com `Random` injetado, resposta `102` → `0xee4` por 160 quadros ou clique) e `DeleteCharacterDialog` (estados 2 Yes/No e 1 senha errada, hover laranja `0x4080ff`, Esc fecha, bloqueio dos botões da cena, envio `0x11` com MD5 hex minúsculo sem logar segredos, respostas `2000` e `105` → `0xeb1a`).
- Ferramentas só de desenvolvimento: `DRPG_GAME_DIR`, `DRPG_SCREENSHOT`/`DRPG_SCREENSHOT_MS` e `DRPG_SCRIPT` (mouse e Esc roteirizados, tempos contados a partir do fim da carga). `tools/publish-client.sh` publica o AOT como `DigimonRPG.bin`.

### Ainda ❓ (não inventado)
- ✅ **tamerId → `a_%02d`:** `id − 1` confirmado em `0x00b21cd0` (laço de 1 a 4, `a_%02d` com `id − 1`, roupas com `id`).
- **Silhueta do BZ** (modo de mistura `0xc`): não implementada.
- **Chave do `option.ini`** de cada volume: `[BGM] sound` (efeitos) e `bgm` (música), padrão 1.
- **Overlay de idioma da tabela Map** não aplicado; **`IsNetworkAlive`** não implementado.
- **Senha fraca:** o diálogo mostra só a primeira linha. **Sombra de sprite** (opção) não aplicada. **Espécie 817** com `+0x80 == 2` (`817_2.ani`) não tratada.
- **Map Server** é registrado sem endereço (a entrada no mapa é o marco 3).
- **Dados:** `Sprite\033_1.ani` (Gabumon) aponta para `s.spr` (✅ está assim no arquivo do jogo): o sprite não existe e cai no `065_1.ani`, como o fallback do original ❓.
