# Cenas do cliente (até a seleção de personagens)

Levantamento para o marco 2 do [cliente novo](../planos/cliente-novo.md): abrir com `<usuário> <chave>` e chegar na **seleção de personagens** com o comportamento do original. Endereços da v322 ([dump de RE](../ferramentas/dump-re.md)).

## Sequência
`WinMain` (`0x00aa4a90`) → linha de comando (`0x00aa24b0`) → `Game Init` (`0x00aa2c10`) → **`CSceneTitle`** (carga) → **`CSceneServer`** (seleção de mundo) → **`CSceneCharSel`** → `CSceneGame`.

- Linha de comando: separa por espaço e exige **exatamente 2** tokens (usuário, chave); senão, caixa de erro e sai. Guarda em `[DAT_01158504+0xb8]` e `[+0xd0]`.
- Troca de cena: `FUN_00b500a0(n)` (o `CSceneTitle` chama com `2` ao terminar). ❓ Números das outras cenas.
- Toda cena desenha o texto `Digimon RPG 3.26` no canto (`FUN_00b55270(0, texto, x, 1, 0xffffff, 1, 0)`; x = 0 na carga, 1 na seleção de mundo).
- `FUN_00b3e3d0` aparece no fim do quadro das cenas: ❓ provavelmente o cursor (`Interface\cursor_*.spr`, carregados em `0x00b3ebf0`; ver [tela de login do launcher](tela-login-launcher.md#desabilitado-e-cursor)).
- `FUN_00b56010` + `FUN_007dbf10`: fim do quadro (apresentação).

## Interface das cenas (vtables)
| Cena | vtable | [0] dtor | [1] init | [2] quadro | [3] | outros |
|---|---|---|---|---|---|---|
| `CSceneTitle` | `0x0102f968` | `0x00b54c20` | `0x00b54c60` | `0x00b54cc0` | `0x00b54c50` | — |
| `CSceneServer` | `0x0102f62c` | `0x00b514c0` | `0x00b53120` | `0x00b54240` | `0x00b52da0` | `0x00b53d40`, `0x00b54310`, `0x00b51950` |
| `CSceneCharSel` | `0x0102ebc4` | `0x00b454d0` | `0x00b47b30` | `0x00b48840` | `0x00b47a80` | `0x00b48410`, `0x00b48c60`, `0x00b45500` |
| `CSceneGame` | `0x0102ee7c` | `0x00b498a0` | `0x00b4bab0` | `0x00b4e550` | `0x00b4b4d0` | |

❓ O 5º item das vtables de `CSceneServer`/`CSceneCharSel` (`0x10e23b8`, `0x10e21ec`) não é código: provável início de uma segunda base (entrada de mouse/teclado).

## CSceneTitle (carga) ✅
- **init** `0x00b54c60`: estado `+0x50 = 0x16`.
- **quadro** `0x00b54cc0`: no estado `0x16` chama a carga `0x00ad3d30` (tabelas, recursos) e passa a `0x17`. Quando a thread da barra marca o fim (`[0x135b150]+0x654 == 1`), troca para a cena **2** (`CSceneServer`).
- **Tela de carga**, desenhada pela "PreLoadingBar Thread" (`0x00b35560`), **30 quadros por segundo** (`FUN_00867ed0(0x1e)`):
  | Ordem | Sprite | Posição | Quadro |
  |---|---|---|---|
  | 1 | `GUI\dr2_in_000_000_ENG.spr` (fundo) | (0, 0) | 0 |
  | 2 | `GUI\dr2_in_001_000.spr` (caixa "Loading") | (289, 140) | 0 |
  | 3 | `GUI\dr2_in_001_001.spr` | (224, 258) | 0 (é transparente) |
  | 4 | `GUI\dr2_in_001_002.spr` (barra) | (293, 305) | `1 + (quadros − 1) × progresso / 100` |
  | 5 | `GUI\dr2_in_001_002.spr` | (293, 305) | modo 1 (primeiro argumento de `FUN_00b56ff0` = 1) ❓ |
  | 6 | texto `Digimon RPG 3.26` | (0, 1) | branco |
- `FUN_00b56ff0(modo, sprite, x, y, quadro, ?, ?)` desenha um sprite; `FUN_00b21be0` busca o sprite no cache por nome.
- **Progresso exibido** (`+0x64c`) persegue o alvo (`+0x644`, gravado pela carga) a cada 10 ms ou mais: `exibido += alvo × 0,01 × 0,18`, ×7 quando o alvo ainda está bem à frente (`+0x648 < alvo − 1`). A carga termina quando `[DAT_01158504]+0x81 == 1`.
- ✅ **Implementado** em `DRPG.Client/Scenes/TitleScene.cs` (fase A): sprites e posições da tabela, 30 FPS, texto da versão em (0, 1), progresso por `LoadingProgress` e carga real em segundo plano (`StartupLoader`: abre `table`, lê `Language/`; a barra recebe o progresso por etapa). Conferido com screenshot da instalação real: igual à tela do original.
- ✅ Descoberta no `0x00b34d40`: o multiplicador ×7 da perseguição usa o campo `+0x648` = 39,0, gravado ao subir o alvo, e não o alvo em si; implementado assim.
- ✅ `dr2_in_001_002` tem **2 quadros** de 231×23 (conferido na `resource.hf` real). Com a fórmula `1 + (quadros − 1) × progresso / 100` o quadro é sempre o de índice 1 (o contorno vazio, como a screenshot mostra); o avanço visível da barra vem então do **desenho "modo 1"** do item 5, que continua ❓ e **não é desenhado** no cliente novo. ❓ Se a contagem do original parte de 0 ou de 1 não está confirmado.
- ❓ Como a carga real é rápida (centenas de milissegundos), a tela dura pouco: o original só termina quando a carga acaba (`+0x81 == 1`), sem esperar a barra encher, e o cliente novo faz igual.

## CSceneServer (seleção de mundo) ❓
- **Pré-carga** (construtor `0x00b504b0`): `dr2_in_000_000`, `000_006`, `002_000..010`, `003_000..004` (os do login antigo também).
- **quadro** `0x00b54240`: limpa, desenha a interface (objeto em `+0x18`, método 1), o texto da versão em (1, 1) e o **texto de idioma `0x5eb`** (`FUN_0091b950(id)`) em (5, 585), branco. Depois `FUN_00b641b0` se `[DAT_0135b154]+8`, e o cursor.
- `0x00b54310`: desenha mensagens centradas (x = 400) com os textos `0x5ee` e `0x5ef` e contadores de tempo (80 e 100 quadros); usa `FUN_009b7570(0x2b5, 0x221)`.
- `0x00b51950` (513 linhas): entrada; toca sons com `FUN_00b3a950(n, 0)` (ids 5, 6, 7, 9, 10, 0xf, 0x10, 0x11, 0x12).
- `0x00b53120` (init): carrega `dr2_in_003_003/004` (OK/CANCEL).
- **Falta:** layout dos widgets (`dr2_in_002_*`: DIGITAL WORLD / BATTLE ZONE), a sequência de rede (quando conecta no login `0x05` e no channel `0x03`/`0x04`), BGM e o que leva à `CSceneCharSel`.

## CSceneCharSel ❓
- Sprites: fundo `dr2_ch_000_000` (800×600, 4 slots "Empty Slot" com quadros TAMER/DIGIMON), slots `dr2_ch_000_001..004` (2 quadros, ~355×230), botões `dr2_ch_000_005..009` (3 quadros), criação `dr2_ch_001_000..004`. Também `GUI\dr2_main_001_013.spr`, `GUI\dr2_sub_011_001.spr`, `Sound\menu-1.wav` e o texto `Map Server : %s %d`.
- init `0x00b47b30` (2256 bytes), quadro `0x00b48840`, `0x00b48c60` (2144 bytes, provável entrada).
- **Falta:** posições, o que aparece em cada slot (imagem do tamer e do digimon: ❓ formato `.ani`/sprites de personagem), textos (nome, nível), botões, sons e BGM.

## Texto, som e fontes
- Textos da interface por id: `FUN_0091b950(id)` → tabela de idioma (❓ qual arquivo de `table/language/`).
- Fontes GDI criadas em `0x00b563f0`: "굴림체" (GulimChe, cp949) em 14/12/11/10 px normal e negrito, 13×5, 32×24, 15; e `Fonts\123RF_123RF_TTF.ttf` (face "123RF", 16 px). Ver [fonte do launcher](tela-login-launcher.md#fonte).
- Som: `Sound\*.wav` (efeitos) e `*.ogg` (BGM, por exemplo `title.ogg`). `FUN_00b3a950(id, 0)` toca um efeito por id: ❓ tabela id → arquivo.
