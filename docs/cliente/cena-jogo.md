# Cena de jogo (cena 5, `CSceneGame`) e entrada no mapa

Marco 3 do [cliente novo](../planos/cliente-novo.md) (PR #49). Endereços da v322 (`DigimonRPG.bin` desempacotado). Cores são COLORREF. ✅ = lido no dump ou confirmado nas capturas; ❓ = ainda não lido.

## "Game Start" na seleção de personagens
1. ✅ Clique (ou duplo clique no slot) → `0x00b48670`. Sem seleção (`+0x860 == −1`) → mensagem `0x265` "No tamer selected.". Com seleção e o slot com nome (< 21 bytes):
   - copia o nome para `[DAT_011584f0]+0x1b0` e os 21 bytes do slot para `+0x1276`;
   - mapa do slot (byte `+0x71` do registro do `0x12`) em `[DAT_011584f0]+0x10064` e `[DAT_011584f8]+0x511c98` (o mapa atual do jogo);
   - log `Map Server : %s %d` com IP (`0x00b39460`) e porta (`0x00b394b0`) da tabela de servidores, pela linha/slot do mundo escolhido e pelo **id do mapa** (cada mapa tem o seu endereço; ver [patch_client](../ferramentas/patch-cliente.md#tabela-de-servidores---server));
   - começa o escurecimento (`+0x14c0 = 1`), o mesmo da cena 2 ([cenas](cenas.md)).
2. ✅ No fim do escurecimento (`0x00b47790`, `+0x14c4 > limite`):
   - confere o slot: `check ≠ offset + mapa + nível` → registra (`0x00ae0150`) e desconecta o channel (antitrapaça; não acontece com lista válida);
   - **conecta no servidor de mapa** (`0x00b3f9a0(ip, porta)`: objeto de conexão novo, `connect` síncrono);
   - conectou → `0x00b8e690(0)`, **troca para a cena 5** e `[DAT_0135a1dc]+0x10 = 1` (o laço principal `0x00aa4a90` vê a marca depois do quadro e abre a [tela de carga](#tela-de-carga-da-entrada-no-mundo));
   - não conectou → `+0x864 = 1`: mensagem `0xee6` "The server is under maintenance. Please try again later. 12" por 80 quadros, continua na seleção.
3. ✅ O `0x16` (`MapConnectionConfirm`, 69 B) e o `0x18` (`ClientHardwareInfo`, 88 B: endereços MAC) vão no channel **junto com o `0x01`**, não antes da conexão: `0x00adca00` manda o `0x01` e chama `0x00ae8700` logo depois (pcaps `first-access` e `inventory_packet`: o `0x16` no mesmo milissegundo do `0x01`, o `0x18` 0,2 s depois). A captura do proxy (sessão 52) parecia mostrar o contrário porque o proxy só grava a conexão do mapa depois de conectar no servidor de cima, e os pacotes do cliente ficam no buffer até lá. ✅ Decisão do dono: o cliente novo manda os **MACs reais**, como o original (1:1). Só no pacote; nunca em log.

## Tela de carga da entrada no mundo
✅ Entre "Game Start" e o campo o original mostra a **mesma tela de carga genérica** da volta para a cena 2 ([cenas](cenas.md), `0x00b34250`), desenhada por uma thread do `CLoadingMgr` (`DAT_0135b150`, sprites de `0x00b34980`: `+0x7c` `loading_image_ENG`, `+0x44` `loading_gauge` (só a contagem de quadros, 12), `+0x658` `game_end_gauge`, `+0x5ec` `loading_under_line`). O corpo da thread é `0x00b34ea0`; ela roda **duas vezes** na entrada:

1. **"PreMap Loading Thread"** (`0x00b34c10`, aberta pelo laço principal logo depois da troca para a cena 5; `+0x5e4 = 1`): fundo sorteado uma vez (`rand() % contagem`, `+0x5e0`), dica = pedaço **0** do texto `0xa6b`. Depois de **16 quadros** começa um cronômetro (contador de desempenho); quando passa de **1,0 s** (`DAT_0100f030`), a thread chama `0x00adca00`, que manda o **`0x01`** (nome + usuário, 43 B) e o `0x16`/`0x18` do channel, e termina. É por isso que o `0x01` sai 2,75–2,9 s depois da conexão nos pcaps, e o `0x49` e o eco do `0x0E` antes dele.
2. Entre as duas: a thread acabou mas `+0x5e4` continua 1, e a cena 5 (`0x00b4e550`) não desenha nada enquanto `+10` ou `+0x5e4` estiverem ligados (só os textos de conexão perdida): **o último quadro fica parado na tela** até o `0x03`.
3. **"Map Loading Thread"** (`0x00b34dc0`, chamada pelo tratador do `0x03` `0x00abbb20` quando `+10 == 0`): dica sorteada `rand() & 15` (`+0x624`), espera a primeira thread terminar, mantém o fundo e liga `+10`. O primeiro quadro ainda mostra a dica 0 e dorme 30 ms a mais (marca `[DAT_011584f0]+0xf803`, ligada por `0x00adca00` e desligada a cada quadro). O tratador carrega o `.map` (`0x00ad3730`, que termina com `0x00b348e0`: desliga `+10` e espera a thread), então a tela some só quando **o mapa carregou e já foram 16 quadros**. Só depois o tratador manda `0x04` e `0xF0` e põe `+0x34c = 0x5f` (o clareamento). Nos pcaps: `0x03` → `0x04` ≈ 2,0 s.

Cada volta do laço: confere a saída; desenha o quadro (fundo em (0, 0), `game_end_gauge` quadro `+0x5cc` em (740, 540), `loading_under_line` em (0, 550), dica centrada em y 580 amarela com sombra, `0x00b55560` ignora o x recebido); `Sleep(80)`; avança o medidor (volta a 0 em 12, **sem zerar** entre as duas threads); `Sleep(10)`. O limitador de 30 FPS (`0x00867ed0(0x1e)`) nunca segura, então o quadro dura **90 ms** (≈ 115–125 ms no Windows, pela resolução de 15,6 ms do `Sleep`, como nos pcaps). Sem cursor. Mapa perdido durante a carga (`0x00b3fdf0() == 0`): texto `0xa8b` em (400, 300) e a thread sai.

## Sequência na rede
✅ Pcap `first-access` (sessão 4; `inventory_packet`, sessão 5, igual com 0,15 s a menos), tempo a partir do SYN do mapa. Os layouts vêm da captura do proxy `v322 login até o mapa` (sessão 52, conexões 87–89), cujos tempos do lado do mapa estão comprimidos (ver o passo 3 acima).

| t (s) | Conexão | Sentido | Pacotes |
|---|---|---|---|
| 0,000 | map | C2S | `0x49` (32 B) — começa a [tela de carga](#tela-de-carga-da-entrada-no-mundo) |
| 0,152 | map | S2C/C2S | `0x0E` hello (30 B); mais dois `0x49`; `0x0E` eco (44 B) |
| 2,896 | map | C2S | `0x01` (71 B, entrar com o personagem): fim da primeira thread de carga (16 quadros + 1 s) |
| 2,896 / 3,108 | channel | C2S | `0x16`, `0x18` |
| 3,27–3,59 | map | S2C | `0x33` × 3, `0x02` (personagem), `0x03` (inventário), `0x7E` × 7, `0x7F` × 7, `0x05`, `0xE6`, `0x74`, `0x152`, `0xB4` (digimon líder), `0x73` |
| 5,297 | map | C2S | `0x04`, depois `0xF0`, `0x121`: fim da segunda thread de carga (mapa carregado + 16 quadros) |
| 5,297 | global | C2S | `0x64` (128 B), `0xDE` × 2 → S2C `0x0E`, C2S `0x0E`, `0x7B`, `0x7C` → S2C `0xE1`, `0x65`, `0x7B`, `0x7C` |
| 5,63 | map | S2C | `0x10`, `0xF0`, `0x121` (10318 B), depois `0x11` em rajadas (selvagens) |
| +5 s | map | S2C/C2S | `0xC4` keep-alive (~5 s) |

A sessão 10 (`login-into-map`) manda o `0x01` 0,16 s depois da conexão; ❓ ainda sem explicação (versão diferente do cliente?).

- O emulador já responde a tudo isso (`MapSessionHandlers`, `MapSequences.WorldState`). O `0x0E` do mapa traz a semente da chave AES do `0xE6` (evento de EXP).
- ❓ Por que o `0x49` sai 3 vezes: o envio `0x009b5840(n)` guarda `n` em `+0x1134` e só manda uma vez por `+0x1340`; os três vêm de chamadores diferentes (`0x009acd10`, `0x009b4d00`, `0x009b5590`, `0x009b55d0`).
- Envios do mapa usam `0x00b3ffd0` (o channel usa `0x00b3ffa0`). Montagem de qualquer pacote: `0x009b4ab0(opcode, dados, tamanho)` (cabeçalho `0xCC`, opcode em `+2`, tamanho + 14 em `+4`, CRC-16 da tabela `0x01011c18` em `+0xC`).

## `CSceneGame` (cena 5)
- ✅ ctor `0x00b494f0` (objeto de 0x108 B, vtables `0x0102ee7c`/`+0x18 0x0102ee90`/`+0x1c 0x0102eea4`); pré-carrega `GUI\dr2_main_001_013.spr` e `GUI\dr2_sub_011_001.spr`. O estado do mundo fica em objetos globais (`DAT_01158504` jogo, `DAT_011584f8` mapa, `DAT_011584f0` personagem, `DAT_011584ec` câmera/mouse), não na cena.
- ✅ **init `0x00b4bab0`**: frequência do contador; limpa o título da janela; `0x00ad1f30`, `0x009b4bd0`, `0x00b86e90` (❓ GUI do jogo); sem conexão no mapa → **cena 2**. Registra os efeitos da cena (`FUN_00a187b0`): 0 `sound\walk01_1.wav`, 2 `be_impact2`, 3 `be_impact1`, 4 `be_attackmiss1`, 5 `effect_slash`, 6 `effect_evol`, 7 `effect_put`, 9 `052_s2`, 10 `093_s2`, 0xb `bt_win`, 0xc `bt_lose`, 0xd `BIT_Ringing`, 0xe `w_on`, 0xf `w_off`, 0x10 `effect_pickup`, 0x11 `be_captwin`, 0x12 `be_dgcapt`, 0x13 `Bit_spend`, 0x14 `be_attackmiss2`, 0x15 `be_levelup`, 0x16 `be_captlose`, 0x17 `ui_charge`, 0x18–0x1b `ds_beam1/2`, `ds_bubble`, `ds_missile`, 0xf0–0xf5 (`ds_explosion`, `Effect_Clockwork`, `Effect_Marching`, `Effect_Pang`, `ds_red_spark`, `B_LD_Count`), 0x155–0x15c (dados do *Digimon Monopoly*), 0x312–0x315 (Jogress). Volume de efeitos de `[DAT_01158530]+9`. Textos de dica `0x8c5..0x8d3` (`0x00b400f0`). Mapa `0xFA` (−6) → aviso `0xecf`.
- ✅ **quadro `0x00b4e550`**:
  1. Erro fatal (`[DAT_01158504]+0x7a4`) → para a BGM, desconecta tudo e **cena 2**.
  2. Perda de conexão (channel `0x00b3fde0`, mapa `0x00b3fdf0`, global `0x00ad2870`) → `+0x348 = 1` e motivo em `+0x349`/`+0x34a`; mostra `0xed0`/`0xed1`/`0xed2` em (400, 300) até a saída.
  3. **Lógica em passo fixo**: acumula o tempo real e roda `0x00afdba0` (rede) + `0x00afe1c0` (mundo) + `0x00a49b10` uma vez por passo de `DAT_0135af08` ms (no máximo 10000 passos por quadro). ❓ Valor de `DAT_0135af08` (o emulador calibrou o movimento a 60 passos/s).
  4. Desenho conforme o modo `[DAT_01158504]+0x80`: **1 = campo `0x00b4b550`**, 2 = batalha `0x00b498d0`.
  5. Transição de mapa (`+0x34c`: ≥ 200 escurece, 100..199 clareia com `FUN_00b55f10`, 500..650 mensagem `+0x352`): portais, fora deste marco.
  6. Texto de aviso por tipo de célula sob o tamer (`0x00b35e60`; `0xed3`..`0xed7` para nível < 5/6 em certas áreas).
  7. Nome sob o cursor (`0x00b377d0`/`0x00b37810`, Ctrl/Alt), clique no chão (`0x00b4d290` andar), cursor, `0x00b3e3d0`, fim do quadro.
- ✅ **campo `0x00b4b550`**, na ordem (cada bloco cronometrado em `+0x40..+0x58`):
  1. Câmera: `0x00b36c20(x, y, …)` com a posição do tamer `[DAT_011584f8]+0x144720/0x144721 × 4` + deslocamento `[DAT_011584ec]+0xc/+0x10`; depois `0x00b36630`, `0x00b36900(0)` e o desenho do mapa (vtable `+8` do objeto de mapa). ❓ Próximo passo do RE.
  2. Entidades `0x00ad4a50`; efeitos de clima por mapa (`0x00a14360`, `0x00a16d40`); `0x00a0d340`; nomes `0x00b37b00`.
  3. UI: `0x00a45230(0)`, `0x00a45230(1)`, GUI `0x009b6010`, `0x00b42f60`, `0x00a0ceb0`, `0x007dca50`. ❓ Quais janelas aparecem na entrada.

## Mapa e entidades no campo
- ✅ Formatos `.map`, `.til`, `.obj` e a ordem de desenho do campo: ver [mapas](mapas.md#desenho-campo-).
- ✅ Entidades (`0x00ad4a50`, modo campo): percorre a lista `[DAT_01158504]+0x2f8` (objetos com vtable: tamers, digimons, NPCs) e põe cada um na fila de desenho com o tipo do byte `+5` e a posição em pixels `+0x170/+0x174`; tipo `+0x149 == 8` ganha uma entrada extra (tipo 3) acima (`y − +0x18c`), `== 0xb` uma do tipo 2; quem está sob o cursor (`+0xca0`, 12 B) ganha a do tipo 4 (destaque). Depois o jogador local (`0x00b5af10`, abaixo) e a lista `+0x300`. A fila (`0x00a0d340`, ordem em y) desenha os tipos 2/4/5 pela vtable **`+0x3c`** da entidade e o tipo 3 pelo `0x00864810`.

## Entrada no mundo (S2C do mapa)
Despachante do mapa `0x00abc350`: índice = opcode − 2 na tabela de bytes `0xacb6a4` → tabela de saltos `0xacb2c4` (✅ `0x02` → `0xabdfc1`, `0x03` → `0xabe059`, `0x0E` → `0xac3394`, `0x10` → `0xabe0e8`, `0x11` → `0xabe125`, `0x73` → `0xabdb9a`, `0x74` → `0xac44a4`, `0x7E` → `0xac4a94`, `0x7F` → `0xac4b45`, `0xB4` → `0xabf314`, `0xC4` → `0xabdea5`, `0xE6` → `0xac0093`, `0xF0` → `0xabf6b7`, `0x121` → `0xabd21d`, `0x152` → `0xac96fe`; `0x05` e `0x33` caem no padrão). O da conexão global é `0x00acdc10`.
- ✅ **`0x02` (personagem)**: copia `0x1db0` bytes, guarda o deslocamento de relógio (`DAT_01158524/28`) e chama `0x00abbd50`:
  1. `0x00ad1f30`, `0x00abb1c0`, `0x00abb4b0` (❓ limpezas);
  2. `0x00b5bb50`: aplica os dados ao personagem (nome em `+0x1b0`/`+0x12c9`, byte `+0x29` → `+0x12de`/`+0x1c5`, …) e cria o objeto do digimon líder em `+0x308` se ainda não existe;
  3. `0x0085e7b0(célula x = dados+0x1270, célula y = dados+0x1272, direção 0)` põe o tamer no mapa (abaixo);
  4. byte `+0x29` ≠ 5, 6, 7 → `0x008669e0`: copia os `0x2b8` bytes do digimon (dados `+0x4d8`) para `digimon+0x800` e o põe **na célula (x − 1, y − 1)**, direção 0, tipo de fila `+5 = 2`;
  5. `[DAT_01158530]+0xb` = dados `+0x19b5` (❓).
- ✅ **`0x03` (inventário)**: copia `0x1140` bytes para `personagem+0x17b8` e chama **`0x00abbb20` (entrar no mundo)**:
  1. `0x00ad3730`: modo `[DAT_01158504]+0x80 = 1` (campo); **carrega o mapa** `0x00b37860(id)` → vtable `+4` do objeto de mapa `0x00b378f0` (`map\%s.map`, ver [mapas](mapas.md)); cria os NPCs do mapa ([quais](#npcs-do-mapa)) e outras preparações (❓).
  2. `0x00adcc50`: conecta no servidor **global** e manda o `0x64`.
  3. Manda no mapa **`0x04`** (16 B: o id de 16 B do personagem, vtable `+0x14` = `0x00865100`, que devolve `+0x1a0`).
  4. Manda **`0xF0`** (28 B, primeiro u32 = `0x100`; ❓ resto).
  5. `+0x34c = 0x5f` (clareia a tela a partir de 95, ver o quadro da cena).
  6. Só na primeira entrada (`+0x3f15 == 0`): `0x00ae07b0(0x121)` (pacote vazio) e marca o sinal.
  - Isso explica o atraso de ~1 s na captura: `0x04`/`0xF0`/`0x121` só saem depois da carga do `.map`.

## Tamer e digimon no campo
- ✅ **Posição** (`0x0085e7b0(cx, cy, dir)`): estado `+0x149 = 1`, animação `+0x180 = 1` (parado), deslocamento do quadro `+0x182 = rand() % 8` (✅ **fase aleatória** da animação parada), `+0x186 = 1`, `+0x19a = rand() % 8`, `+0x18a = 0x37`, direção `+0x164 = dir`; célula atual e destino `+0x178/+0x17a` e `+0x17c/+0x17e` = (cx, cy); **mundo em px `+0x168 = cx·64 + 32`, `+0x16c = cy·32 + 16`** (centro da célula).
- ✅ **Tela**: `+0x170/+0x174` = mundo − rolagem da câmera (abaixo).
- ✅ **Fila de desenho** (o tamer local fica fora da lista `+0x2f8`): `0x00b5af10`, chamado no meio de `0x00ad4a50`, põe o **digimon (`+0x308`) como tipo 4** e o **tamer como tipo 3**, e cada outro digimon da lista `[DAT_01158504]+0x2f0` (❓ outros jogadores, próximo marco). Os dois vão pela ordem em y da fila.
- ✅ **Desenho do tamer** (fila tipo 3 → `0x00864810`): o mesmo **boneco de papel `0x0099fb90`** da seleção de personagens ([cenas](cenas.md)), com (tamer = vtable `+0x20`, x = `+0x170`, y = `+0x174`, quadro `+0x184`, equipamentos `+0x224`, início da animação, −1); o corpo e as roupas vão pelo canto superior esquerdo em **(x − 120, y − 162)**, sem âncora. ✅ **Sombra**: o `0x0099fb90` passa um modo ao `0x00b1d0f0` (1 nos mapas `0x61`/`0x66`/`0x67`/`0x6d`, 2 em `0x50`–`0x54` e `0x59`, 0 nos outros); só no modo 0 e com a opção `shadow` (`[DAT_01158530]+2`) ele desenha antes do corpo a **camada de sombra** do quadro do corpo (da roupa de corpo, ou do corpo base quando ela não tem sombra) por `0x00b57460(…, 0xe)` no mesmo canto, misturada a 14/32. As roupas não têm sombra própria. Implementado em `TamerDoll.Draw` + `SpriteEffects.TamerHasShadow`. Depois o tipo 3 desenha o nome (`0x00a10e10` em (`+0x170`, `+0x174 − +0x18c − 30`), ❓ fonte/cor) quando `+0x128 > 0`.
- ✅ **Desenho do digimon** (fila tipo 4 → vtable `+0x3c` da classe `0x01007310` = `0x007e79d0` → caso normal `0x007e83d0`): sprite da **espécie `+0x814`** (dados do `0x02` `+0x4ec`) pela [tabela por espécie](tabelas-de-especie.md) (`0x00b23d20`), quadro `+0x184`, **com a âncora do `.ani`**: canto em **(x − cx[quadro], y − cy[quadro])** (`0x0086d200`/`0x0086d220`); espécies `0x149` e `0x3e9` descem mais 32 px. ✅ Antes, com a opção `shadow` (`[DAT_01158530]+2`), a **camada de sombra** do mesmo quadro (`0x00871e30`) por `0x00b57460(…, 0xe)` no mesmo canto do sprite, misturada a 14/32 (4/32 nos mapas `0x61`/`0x66`/`0x67`/`0x6d`; as espécies `0x34`/`0x291` nas animações 9/10 e `0x94` na 10 não têm sombra, ❓ animações ainda não tocadas no campo); depois, fora das animações 9/10 e com a opção `anti` (`[DAT_01158530]+0`), a **camada extra** (`0x00871df0`, borda suave) por `0x00b57330` a 14/32 (6/32 nos mesmos mapas). As duas usam a mistura do modo −1 quando a opção `alpha` está ligada e o desenho normal quando não. Implementado em `GameScene.DrawDigimon` + `SpriteEffects`.
- ✅ **Animação** (por passo de lógica; tamer `0x00867a60`/`0x00867790`, digimon `0x007f4cf0`/`0x007f4080`):
  1. quadro `+0x184` = início(animação `+0x180`, direção `+0x164`) + deslocamento `+0x182`;
  2. duração do quadro `+0x186` = o **primeiro campo do quadro no [`.ani`](formato-ani.md)** (`0x00873140`; as espécies-sprite `0x11b`, `0x11c`, `0x15d`, `0x343`, `0x360` usam 2);
  3. contador `+0x188`: se `contador ≥ duração` → deslocamento + 1 e contador 0; senão contador + 1 (cada quadro fica **duração + 1 passos**);
  4. deslocamento > contagem − 1 → volta a 0 (parado) — no mapa **todos os quadros aparecem**, ao contrário da seleção de personagens.
  - ✅ Digimon parado: contador `+0x1a0` + 1 por passo; no passo 200 troca para a animação **3** (1 chance em 4) ou **2**; no passo 320 volta para a 1 e zera (as espécies `0xfb` não trocam). Trocar de animação (`0x0085e4c0`) zera deslocamento e contador e recalcula o quadro.
- ✅ **Passo de lógica**: `DAT_0135af08 = 30` (`0x00867ed0(30)`, objeto de tempo `0x135aed8`), o divisor do acumulador no quadro da cena. ❓ A unidade do tempo somado (`0x00867e40`) ainda não foi lida; o emulador calibrou o movimento a 60 passos/s.

## BGM do mapa
- ✅ `0x00ad3460`, chamado pela carga do mapa (`0x00ad3730`): **para a BGM atual** (`0x00b424a0`), escolhe o arquivo pelo id do mapa (tabelas fixas no binário), toca em laço (`0x00b42500(arquivo, 0)` + `0x00b42670(1)`) e aplica o volume da opção `[DAT_01158530]+8` (`0x00b42460`). Mapa sem entrada: nada toca (a anterior já parou). Tabela (id decimal → arquivo; "—" = sem música, "vazio" = caminho vazio):

| Mapas | Arquivo | Mapas | Arquivo |
|---|---|---|---|
| 0, 14–17, 30–33 | vazio | 1, 5, 250 | `Sound\007.ogg` |
| 2–4, 6–13, 24–29, 44–46, 79, 131 | `Sound\001.ogg` | 18, 19, 38, 39, 52, 53 | `Sound\009.ogg` |
| 20, 40 | `Sound\020.ogg` | 21–23, 41–43 | `Sound\105.ogg` |
| 34, 35, 99, 107, 132, 134 | `sound\035.ogg` | 36 | `sound\036.ogg` |
| 37, 51 | `sound\037.ogg` | 60–65 | `Sound\060.ogg` |
| 70 | `Sound\070.ogg` | 71–77, 95 | `Sound\071.ogg` |
| 80–87, 89, 98, 112–116 | `Sound\080.ogg` | 96, 100, 101, 108 | `Sound\Ws_cave.ogg` |
| 97, 102, 103, 109 | `Sound\WS_Ocean.ogg` | 104 | `Sound\WS_Passage.ogg` |
| 105 | `Sound\WS_BossRoom.ogg` | 106 | `Sound\Riddle.ogg` |
| 117, 119 | `sound\XUANWUMON_map.ogg` | 125–128 | `sound\SPIRALMOUNTAIN_map.ogg` |
| 129 | `sound\DarkForest.ogg` | 130 | `sound\UnderForest.ogg` |
| 133, 136–141 | `sound\ZhuqiaomonDungeon.ogg` | 135, 142 | `sound\MugenMountain.ogg` |
| 143, 155, 156 | `sound\HellsField_Floor.ogg` | 144–149 | `sound\UnderGroundPassage_Hard.ogg` |
| 150, 151 | `sound\Gehenna.ogg` | 152–154 | `sound\HellsField_Passage.ogg` |
| 200–204 | `Sound\BZRoom.ogg` | resto | — |

## Câmera
- ✅ `0x00afe310` (campo): chama a vtable `+0x24` do personagem e põe a rolagem do mapa (`0x00b36b80`) em **(`+0x168` − 400, `+0x16c` − 300)**, presa a `0..largura·64 − 800` e `0..altura·32 − 600`. Na batalha a rolagem é fixa (250, 206).

## UI do campo (HUD)
Escopo do marco 3 (decisão do dono): **painéis com os valores reais** do `0x02`; chat e botões só desenhados (sem função).

Referência: captura de tela do original logo após entrar no Digital Administration Bureau (mapa 1), enviada pelo dono (fica em `.local/m3/ref_entry_original.png`, fora do git). Na entrada aparecem, além da barra: o **minimapa** aberto no canto superior direito com 3 botões redondos à esquerda, a caixa **"EXP 2000%"** e os ícones sino/W/E à esquerda, **3 painéis de missão** no canto inferior esquerdo, mensagens no chat e os botões SEAL/REWARD/SHOP/WORLD/DEPOSITORY acima do painel do digimon. Correção: `GUI\dr2_main_001_*` (`0x00b67b10`, `0x00b65470`) é a **UI de batalha** (COMMAND, SKILL, CARD SLASH), não o minimapa.

- ✅ Bloco de UI do campo (`0x00b4b550` passo 3): `0x00a45230(0)`/`(1)` são as **camadas de efeitos** (não janelas). `0x009b6010` → `0x009b1640` (objeto com modos 1..4, ❓). `0x00b42f60` é vazio. `0x007dca50` → `0x00b641b0` ×2, `0x00b40340` (aviso que gira a cada 7 s em (centrado, 40)), `0x007de4b0` (`Interface\w_tamer_menu.spr`, ❓), **`0x00b9a7c0`** (gerenciador de janelas: cada janela visível pela vtable `+0x28`).
- ✅ **Barra principal** = objeto global `DAT_01158508` (ctor `0x00b84b20`, vtable `0x01031724`, desenho `+0x28` = `0x00b8a850`, carga dos sprites e posições `+0x20` = `0x00b90570`). Visível por `+0x184` (1 no ctor; Ctrl+Z alterna). Janela 800 × altura de `dr2_main_000_000`, em y = 600 − 105 = **495**. Sprites `GUI\%s.spr` (o `_eng` quando existe).

### Ordem do desenho (`0x00b8a850`, modo campo, sem batalha)
1. **Fundo** (`[+0x180]` vtable `+4` = `0x00b90520`): `dr2_main_000_000` quadro 0 em (0, 495); depois o quadro 1 no mesmo lugar por `0x00b569f0(1, spr, 0, 495, −1, 0x10, 0, 0, −1, −1, 0)`. ✅ Modo −1 com a opção `[DAT_01158530]+1` desligada = desenho normal (`0x008754f0`); ligada = **mistura alfa** `0x00875a00` → `0x00876a30` → `0x00874710` (555) / `0x008749d0` (565): `destino = (fonte·a + destino·(32 − a)) >> 5` por canal, com `a` = o 6º argumento limitado a 0..32 (aqui `0x10` = 50 %). Só os pixels opacos do sprite entram (as faixas transparentes do RLE são puladas). ✅ As opções vêm de `option.ini [SPRITE]` (`0x00b42be0`): `anti` → `+0`, `alpha` → `+1`, `shadow` → `+2`, `card` → `+3`, todas **1 por padrão** (`GetPrivateProfileInt(…, 1)`); a janela de opções `0x00b16980` as muda em jogo. Implementado: `SpriteOptions` (lido do `option.ini`, nunca gravado), `SpriteOptions.DrawBlended` → `IRenderer.DrawRegionBlended` (o `SDL_SetTextureAlphaMod` com `a·255/32` dá a mesma mistura), `FieldHud.BarGlowAlpha`/`ChatAlpha`.
2. **Painel do tamer** (canto superior esquerdo, asm `0x00b8a964`):
   - `Interface\w_unitframe.spr` (129×38) quadro 0 em (0, 0);
   - retrato do tamer `0x00b1ec40(tamer)` **quadro 1** em (2, 2). ✅ `0x00b1ec40(id)` devolve o sprite `0x010d5624 + id·0x38`: ids fora de 1..1100 dão um sprite vazio (`0x010e8394`); `0x141`–`0x143`, `0x3ec` e `0x3a5`–`0x3ad` ficam como estão, os outros passam pela [tabela por espécie](tabelas-de-especie.md) `0x00b23d20` (os tamers 1–9 não têm entrada, então são o próprio id). Os sprites são `Sprite\icon_%03d.spr` (nomes montados em `0x00b21cd0`, carregados por `0x00b231b0` a partir de `0x00ad3d30`, de 1 a 1099); arquivo que falta carrega `icon_065.spr` (e o id `0x331` com o modo `+0x80 = 2` do jogador usa `Sprite\icon_817_2.spr`). O mesmo vale para o retrato do digimon. Implementado em `SpeciesTables.IconId` e `FieldHud.Portrait`;
   - nome do tamer em (33, 4), cor `0x80ffff`, com sombra (`0x00b55760`);
   - ícone `0x00a14910(+0x12df, 33, 20)`: quadro `valor − 1` do sprite `[DAT_0135a300]+0x2698`; 0 não desenha (`+0x12df` = byte `0x02`+0x2a, o `Unknown42` do `CharacterDataPacket`; ❓ sprite);
   - `"Level "` em (50, 22) branco **sem sombra** (`0x00b55900`); nível do tamer (`+0x12e0` = `0x02`+0x2c) em (90, 22) cor `0x80ffff` sem sombra;
   - `"Digimon RPG 3.26"` em (130, 1) branco com sombra; nome do servidor/canal (`[DAT_011584f4]+0xafc60`) em (130, 14); mapa ≥ 200: mais uma linha `[DAT_011584f4]+0xafc9c` em (130, 28);
   - **relógio** (texto largo, `0x00b55320`): `TextAttach 0x1118` `"PST : %2d:%02d:%02d %d-%d-%d"` com (hora, min, seg, ano, mês, dia) de **UTC − 7 h** (`time − 0x6270`, `gmtime`), alinhado à direita em x = **640 − largura**, y = 1, branco com sombra;
   - **mapa e posição**: nome do mapa (`0x00b395a0(id)`, `Map_US`) + `"(" x "," y ")"` com a célula `+0x178`/`+0x17a`, em (130, 27) branco com sombra; nos mapas `0x33..0x35, 0x6a, 0x6c, 0x6d, 0x75, 0x77, 0x81, 0x82, 0x85, 0x87..0x95, 0x98..0x9c` só o nome.
3. `0x00b89df0`, `0x00b891c0`, `0x00b86f90`: dicas de buff e de tempo restante (nada na entrada; fora do marco).
4. **Painel do digimon** (bloco de status do líder = `digimon+0x800` = `0x02`+0x4d8, `DigimonStatus`):
   - retrato `0x00b1ec40(espécie +0x14)` quadro 0 em (594, 509);
   - `"Lv.%d"` (nível `+0x2e`) em (670, 508) branco com sombra;
   - só na batalha: `dr2_main_000_007` em (454, 473), a barra de TP e `dr2_main_000_008` (`[+0x1d90]`) em (672, 479); no campo o código pula direto para o HP (`0x00b8cc87` → `0x00b8cf05`);
   - **barras** (classe `0x007e3460`: `0x007e35e0(%)` guarda a largura = (largura do quadro 0 − 1) × % / 100 + 1; `0x007e3560` desenha o **quadro 0 recortado nessa largura** e depois o **quadro 1 inteiro** por cima); % = atual / máximo × 100 (`0x00b31c00`, 0 se máximo = 0):
     | Barra | Sprite | Posição | Atual / máximo (bloco) |
     |---|---|---|---|
     | HP | `dr2_main_000_002` (99×12) | (692, 525) | `+0xc0` / `+0xec` |
     | VP | `dr2_main_000_005` (99×12) | (692, 545) | `+0xc4` / `+0xf0` |
     | EVP | `dr2_main_000_004` (99×12) | (692, 564) | `+0xc8` / `+0xf4` |
     | EXP | `dr2_main_000_003` (295×12) | (488, 585) | `+0xe0` / `+0x100` (i64) |
     | TP | `dr2_main_000_006` (202×12) | (464, 481) | só na batalha |
   - os textos `"HP %d/%d"`, `"VP %d/%d"`, `"EVP %d/%d"`, `0x885` `"EXP %I64d/%I64d %.2f%%"` só aparecem com o **mouse sobre a barra** (`0x00b31aa0`), no painel de dica `0x007de1b0` (fora do marco).
   - entre EVP e EXP, `0x00b91720`: os **8 botões de menu** `dr2_main_000_010..017` (29×29) em (450, 515), (482, 515), (546, 514), (546, 548), (514, 515), (514, 548), (482, 548), (450, 548); quadro **3** com a janela fechada, 0 aberta (janelas 1, 3, 4, 0xf, 2, 0xcc, 0xa8, 0xb) → todos no quadro 3 na entrada.
5. Botões no estado normal (quadro 0; `0x009b7260` = quadro `tabela[estado]`, estado 0/1/2 = normal/mouse em cima/pressionado): `000_042` SEAL (516, 439), `000_032` SHOP (516, 462), `000_041` DEPOSITORY (516, 485), `000_031` WORLD (451, 485). **REWARD** = `GUI\dr2_sub_016_005` (controle `+0x77478`, carregado em `0x00b91554`) em (451, 462), desenhado em `0x00b8d399` fora dos canais BATTLE ZONE: com `+0x77438` = 0 pelo estado do mouse; senão pisca (contador `+0x7743c`, há recompensa; ❓ quem liga).
- ✅ **Chat** (`0x00b8e6a0`/`0x00b91a70`, modo do tamanho `+0x770a4` = 0 na entrada): fundo `dr2_main_000_018` quadro 0 em **(28, 460) recortado a 81 px de altura** (✅ opção `[DAT_01158530]+1` ligada → `0x00b569f0(0, spr, 28, 460, −1, 0x14, 0, 0, −1, 81, 0)`: a mesma mistura alfa do fundo da barra com `a = 0x14` = 20/32; desligada → `0x00b56ff0` direto, opaco). Botões do modo de chat `000_023..027` (20×20) em (179, 572), (201, 572), (222, 572), (244, 572), (157, 572): quadro 3, e 0 no modo atual (0 → `000_025`); o `000_027` fica desabilitado com nível ≤ 4. Abas `000_037..040` (35×14) em (280, 574), (317, 574), (355, 574), (391, 574): quadro 2 na aba atual (0 na entrada), 0 nas outras.

### Canto esquerdo: bônus de EXP, sino, W/E (`0x00b8ae36`…, dentro do desenho da barra)
Só fora dos canais BATTLE ZONE (`0x00b39760`) e com `[DAT_01158504]+0x80` = 1 (campo).
- ✅ **Bônus fixo**: com `[jogador]+0xf6a8` > 0, `Sprite\Exp_Bonus.spr` quadro 0 (caixa verde "경험 이벤트") em (2, 100); mouse em cima → dica `0x805` com o valor (fora do marco). ❓ Quem preenche `+0xf6a8` (`0x00af1a80`).
- ✅ **Bônus de evento** (a caixa "EXP 2000%" da captura): com `+0xe390` = 1 e nível `+0xe3d4` ≠ 0, em **(0, 96)**:
  - `+0xe3d5` = 0: `Exp_Bonus.spr` quadro `nível` (o quadro n mostra **n × 25 %**, 1..80 → 25 %..2000 %; 81 quadros), ou quadro `nível + 0x14` se um dos 6 efeitos em `[DAT_01158504]+0xd2c` (passo 0xc) tem id `0x3ED`;
  - `+0xe3d5` ≠ 0: `Sprite\Exp_Bonus_Hot.spr` quadro `nível`;
  - mouse em cima → dica com o texto `+0xe394` (fora do marco).
- ✅ Esses campos vêm do **S2C `0xE6`** (caso `0xac0093` → `0x00aef8b0`; ignorado se o u32 em +4 = `0x7F6`): byte +0 = nível (0 desliga: `+0xe390 = 0`), u32 +4/+8/+0xc = números do texto `0xE63`, byte +0x10 = "hot", byte +0x11 = tipo (1 → só o texto `0xE62`, sem mudar o nível; 0 → grava nível e "hot"). Na captura o nível é 80.
- ✅ **Sino** = `GUI\RemainingTime_Notification_Icon.spr` (controle `+0x774f8`) em (1, 123): quadro 1 se algum dos 6 itens com prazo tem menos de 1 dia (`< 0x15180` s e ≠ −1, `0x00b28f70`), senão quadro 0 (`0x00b8bdd6`).
- ✅ **W** e **E** = `Interface\icon_buff.spr` quadros `0x75` e `0x76` (controles `+0x77538`/`+0x7758c`) em (17, 123) e (33, 123), só com a barra visível e quando as listas `+0x77578`/`+0x775cc` têm algo (montadas dos efeitos do jogador a partir de `+0x4078`; ❓ quais efeitos e quais pacotes). Fora do marco além do desenho.

### Minimapa (`WindowMiniMap`, janela 5)
- ✅ Classe vtable `0x01033008` (ctor `0x00b9ae90`, carga `+0x20` = `0x00b9d310`, desenho `+0x28` = `0x00b9c600`). Janela em **(641, 0)**; na captura ela já está **aberta na entrada**.
- ✅ **Quando aparece**: barra visível (`+0x184`) **e** `0x00b9cfe0(mapa)` (mapa = `[DAT_011584f8]+0x511c98`) **e** o mapa tem imagem na tabela abaixo. `0x00b9cfe0` esconde nos mapas `0x22, 0x25, 0x2E, 0x33..0x35, 0x4F..0x57, 0x59, 0x5E, 0x64..0x6E, 0x75, 0x77, 0x85, 0x88..0x8D, 0x8F, 0x98..0x9C, 0xCA, 0xFA`. O **tutorial (mapa 79 = `0x4F`) não tem minimapa** (o que o dono viu).
- ✅ **Imagem por mapa** (`Interface\w_minimap_N.spr`, quadros 150×120; sem linha = sem minimapa, `+0x20d = 0`):
  | Mapas | Sprite | Quadro |
  |---|---|---|
  | 1..19 | `w_minimap_1` | mapa − 1 (mapa 1 → quadro 0, conferido com a captura) |
  | 20..34 | `w_minimap_2` | mapa − 20 (35 → 9, 36 → 8; 37..39 → mapa − 32) |
  | 40..46 | `w_minimap_3` | mapa − 40 |
  | 60..65 | `w_minimap_4` | mapa − 60 |
  | 70..77 | `w_minimap_6` | mapa − 70 |
  | 200..202 | `w_minimap_5` | mapa − 200 |
  | 88, 118, 120, 121, 122, 123 | `w_minimap_7` | 0, 1, 2, 3, 4, 5 |
  Largura/altura = as do quadro (largura 0 → 150).
- ✅ **Desenho** (`0x00b9c600`):
  1. `GUI\dr2_sub_007_007` (158×20, título) em (641, 0);
  2. se não recolhido (`+0x180` = 0): `dr2_sub_007_021` (158×126, moldura) em (641, y + 15) — ou (641, y + 20) com a opção `[DAT_01158530]+1` ligada; depois a imagem do mapa em **(645, 22)** (com a opção ligada por `0x00b569f0(…, −1, 0x18, …)`, ❓ mistura);
  3. botão recolher/abrir (controle `+0x184`) em **(781, 5)**: `dr2_sub_007_022` aberto, `023` recolhido; o clique alterna `+0x180`. Recolhido, nada abaixo é desenhado;
  4. `0x00b9b930` — **marcadores** (`Interface\Map_icon_01.spr`, 12×12): para cada NPC do mapa (lista `[DAT_01158504]+0x300`), o registro do `npc_info_us` pelo id do NPC: se `+0xbc` (col. 14) ≠ 0, quadro `+0xb8` (col. 13) na célula (`+0x24`, `+0x28`) (col. 4, 5) do registro; e os até 4 portais do registro do mapa no `map_us` (`+0x84`, passo 0x24: ligado em +0x18, célula em +0x1c/+0x20) com o quadro 0 (o portal azul). Mouse em cima → nome (texto do registro) por `0x00b9c4b0`;
  5. membros do grupo no mesmo mapa (lista `[DAT_01158504]+0x308`, menos o próprio): `w_minimap_point` por `0x00b569f0(…, 6, −18, −3, 31, …)` (❓ tingido);
  6. **o jogador**: `Interface\w_minimap_point.spr` (8×8) quadro 0 centrado em (645 + px · L / Lpx, 22 + py · A / Apx), com px/py = posição em pixels (`+0x168`/`+0x16c`) e Lpx/Apx = tamanho do mapa em pixels (`[DAT_011584f8]+0x511c88/+0x511c8c`).
  Posição na imagem de uma célula (NPC, grupo, portal) = (645 + cx · L / largura em células − w/2, 22 + cy · A / altura em células − h/2) (`[DAT_011584f8]+4/+8`), centrada pelo tamanho do quadro.
- Fora do marco: clique no minimapa = andar até lá (`0x00b9d120`, C2S `0xE3` a cada 3 s; mensagens `0xF2F`/`0xF30`).
- ✅ **3 botões redondos** à esquerda = `GUI\TeleportEasy_Icon24.spr` quadros 0, 1, 2 (controles da barra `+0x77624/+0x77664/+0x776a4`, carregados em `0x00b91610`) em **(615, 65), (615, 92), (615, 119)**, desenhados em `0x00b8db44` só com o minimapa visível e aberto (`0x00b9d0f0` e `+0x180` = 0) e fora das linhas 7..18 do servidor e dos mapas `0x58`, `0x76`, `0x78..0x7B`. O clique usa itens de teletransporte (`0x238`/`0x23a`/`0x23b`/`0x23c`; fora do marco).

### Painéis de missão (`CWindow_QuestNotice`, janela `0x17B`)
- ✅ **Não vêm de um pacote próprio**: o handler do S2C `0x03` (`0x00abbd04`) chama a vtable `+0x20` (`0x0098bed0`) da janela, fora das linhas 7..18 e dos mapas de `0x00b38b30`. Ela percorre os registros de **`pop-upwindow_us`** (lista `+0x20..+0x24` do `CTableClass_QuestDialog`, tabela do grupo 21, registros de 0x48 bytes; [tabelas](#tabelas-usadas-pelo-minimapa-e-pelos-painéis)) e cria um painel para cada um cujo **item** (coluna 0) o jogador tem (`0x00ad30f0(item, 1)`: id em +4 e quantidade em +0xc de cada espaço de 108 bytes da **bolsa de itens do próprio `0x03`** (`[jogador]+0x1ed8`, 24 espaços) ou das 7 bolsas extras em `+0x76fc` (0xa90 bytes cada, as páginas do **S2C `0x7F`**). Sem nenhum painel a janela fecha. Na captura: os itens 22008, 24171 e 24172 (cartas de apresentação do tutorial) → Nyang (embaixo), Matsuda Takato & Guilmon, GM. Mitsuo Yamaki (em cima).
- ✅ **Desenho** (`0x0098b0e0`): base (x, y) = (2, 398); painel n (n = 0 é o primeiro registro da tabela, embaixo) com deslocamento n × 61 para cima:
  - `GUI\QuestNoticeUI.spr` quadro 1 (misturado, `0x00b569f0`) e quadro 0 em (2, 398 − 61n);
  - retrato `Sprite\QuestNoticeIcon.spr` quadro `registro+0xc` (coluna 3) em y = 398 − 61n + 19;
  - textos brancos com sombra em x = 2 + 47: título (`registro+0x18`, coluna 6) e lugar (`registro+0x30`, coluna 7), cortados com "..." acima de 25 e 20 caracteres (`+0x180`/`+0x184` da janela); mouse em cima do texto → texto inteiro;
  - botão "Progress" `GUI\win052_1_go` em (2 + 169, 398 − 61(n+1) + 99); o clique (fora do tutorial `0x4F`) manda um C2S (fora do marco).
  ❓ Posições exatas do retrato e do título/mapa no eixo y (ler os `push` em `0x0098b44e`…`0x0098ba75`).
- O `0x7E` (7 páginas, índices `0x29..0x2F`, 0x790 bytes cada, gravadas em `[jogador]+0x4190 + 0x790·(índice − 0x29)`) é outra bolsa (24 espaços de 0x4c bytes = registros de card, como a bolsa de cards do `0x03`; ❓ quais), não os painéis.

### Tabelas usadas pelo minimapa e pelos painéis
Texto separado por `|` em `table.hf` ([arquivos de dados](arquivos-de-dados.md)); colunas a partir de 0, com o offset no registro do original.
- ✅ **`npc_info_us`** → `CTableClass_Npc` (grupo 15, `NPC_INFO.dat`, linha lida por `0x007231e0`; mapa por **id do NPC**, `0x00722ce0`):
  | Col. | Registro | Conteúdo |
  |---|---|---|
  | 0 | +0 | mapa |
  | 1 | +4 | **id do NPC** (chave) |
  | 2 | +8 | nome (dica no minimapa) |
  | 3 | +0x20 | direção inicial (3º argumento de `0x0085e7b0`; ❓ valores 0/1/2/7) |
  | 4, 5 | +0x24, +0x28 | célula x, y |
  | 6 | +0x2c | sprite (`Custom…` = avatar) |
  | 7 | +0x44 | ícone |
  | 8 | +0x5c | ❓ |
  | 9 | +0x60 | **1 = o cliente cria o NPC ao carregar o mapa** |
  | 10, 11 | +0x64, +0x68 | ❓ (diálogo?) |
  | 12 | +0x6c | ❓ guardado no NPC (`+0x2b8`) |
  | 13 | +0xb8 | quadro do `Map_icon_01` no minimapa (0..4) |
  | 14 | +0xbc | 1 = aparece no minimapa |
  Ex.: `1|322|GM. Mitsuo Yamaki|0|59|86|NPC_yamaki|icon_npc|26|1|238|0|0|2|1`.

### NPCs do mapa
- ✅ Ao carregar o mapa (`0x00ad3730`), o cliente percorre **todas** as linhas do `npc_info_us` e cria um NPC para cada uma com **col. 0 = mapa atual** (menos o mapa `0x66`) e **col. 9 = 1** (`0x00abb750(id)`). O NPC (objeto de 0x9c4 bytes, GUID novo) é iniciado por `0x0085f090` com id (col. 1), nome (col. 2), célula (col. 4, 5), direção (col. 3) e col. 12, e entra na lista `[DAT_01158504]+0x300` — a mesma que o minimapa percorre. No mapa 1 são 21 linhas (Culumon, Pet Guide, Nyang, GM. Mitsuo Yamaki, Megumi…); as com col. 9 = 0 (Yagami Taichi, Ishida Yamato, Peti Meramon…) não aparecem sozinhas.
- ✅ **S2C `0x09`** (10 bytes, caso `0xac7331`): i16 mapa, i16 id do NPC, i16 x, i16 y, byte +8 = 1 adicionar / 0 remover.
  - Adicionar (`0x00abb9e0`): só se o mapa é o atual e o id existe no `npc_info_us`; cria o NPC na célula do pacote (`0x00abb8b0`) e guarda (mapa, id, x, y) na lista `+0x760`, cujos NPCs são **recriados** a cada carga daquele mapa.
  - Remover (`0x00acfed0`): tira o NPC com esse id da lista `+0x300` e da `+0x760`.
  - ❓ O emulador ainda não manda o `0x09`; o oficial usa para os NPCs de missão com col. 9 = 0.
- Outros casos na carga: mapa `0x66` (`0x00abbff0`), mapas `0x58`/`0x78..0x7B` (`0x00ad48a0`), mapa `0x76` (`0x00ad6470`) — fora do marco.
- ✅ **`map_us`** → `CTableClass_MapInfo` (grupo 11, `Map.dat`, linha lida por `0x0078e480`, registros de 0x114 bytes; busca por id `0x0078dc20`): col. 0 id (+0), 1 nome (+4), 2 (+0x1c, byte), 3 (+0x1d), 4 código (`R001`, +0x20), 5 (+0x38), 6 = n e as n colunas seguintes (de 5) = nomes de sala (+0x40); depois 12/13 posição no mapa-múndi (+0x4c/+0x50), 14/15 duas linhas de descrição (+0x54/+0x6c) e **4 portais** de 4 colunas (+0x84, passo 0x24): nome do destino (+0, dica), ligado (+0x18), célula x (+0x1c), y (+0x20). Mapa 1: `West of Administration Bureau|1|3|120` e `South of Administration Bureau|1|94|147` = os dois redemoinhos azuis da captura.
- ✅ **`pop-upwindow_us`** → lista do `CTableClass_QuestDialog` (grupo 21, `Pop-UpWindow.dat`, linha lida por `0x007b49f0`; só as linhas com a última coluna = 1, na ordem do arquivo):
  | Col. | Registro | Conteúdo |
  |---|---|---|
  | 0 | +0 | id do item que mostra o painel |
  | 1 | +4 | id do NPC |
  | 2 | +8 | ❓ (diálogo?) |
  | 3 | +0xc | quadro do `QuestNoticeIcon` |
  | 4, 5 | +0x10, +0x14 | célula de destino x, y (botão Progress) |
  | 6 | +0x18 | título |
  | 7 | +0x30 | lugar |
  | 8 | — | 1 = entra na lista |
  O arquivo do v322 tem 3 linhas (as da captura). O `questdialog_us` do mesmo grupo é o texto dos diálogos de NPC, não os painéis.

## Ainda ❓ (fase A em andamento)
- UI: modos 5, 6, `0xe`, `0xf`/`0x10` do `0x00876a30` (✅ −1 é a mistura alfa, ver a barra de baixo; `0x14` e `0x18` dos chamadores são o alfa do modo −1, não modos), sprite do ícone `+0x12df`, `w_tamer_menu` na entrada, `0x009b1640`, `0x00a0ceb0`; colunas ❓ das tabelas acima; sprite e animação do NPC (`0x0085f090`).
- Montagem de `0x18`, `0x49`, resto do `0xF0`, e da conexão global (`0x64`). ✅ O `0x01` é montado em `0x00adca00` (43 B: nome[21], `+0x493`[21], 1 B) e o `0x16` em `0x00ae8700`, logo depois.
- Nome sobre o tamer (fonte, cor); a janela de opções (`0x00b16980`) que muda `[SPRITE]` em jogo; o `icon_817_2` do modo `+0x80 = 2`.

## Implementação no cliente novo (marco 3, fase C)
Código: `GameScene` (cena 5), `WorldLoading` e `LoadingScreen` (tela de carga), `WorldSession` (conexões de mapa e global), `MapDecoder`, `FieldMap`, `FieldAnimator`, `FieldHud`, `MapBgm`, `HardwareAddresses`; pacotes em `DRPG.Shared/Packets/Map/WorldEntryPackets.cs`, `Channel/ChannelPackets.cs`, `Global/GlobalPackets.cs`; leitores em `DRPG.Shared/Formats/Map`. Testes: `WorldLoadingTests`, `WorldSessionTests`, `GameSceneTests`, `EnterGameTests`, `MapDecoderTests`, `FieldTests`, `MapFormatTests`, `WorldEntryPacketTests` e, contra o emulador, `EmulatorTests.A_created_character_enters_the_world`.

**Como a sequência da tabela acima é seguida**
1. "Game Start" (`CharacterSelectScene.EnterGame`): registra `Map Server : <host> <porta>` (de `servers.map`, ❓ o original escolhe por uma tabela mundo × mapa do binário), guarda o personagem no `GameContext` e escurece como a seleção de mundo.
2. No fim do escurecimento: a conexão no mapa e a troca para a cena 5. Falhou → texto `0xee6` por 80 quadros e o jogador fica na seleção.
3. Mapa: `0x49` × 3 na conexão (❓ o original manda um e os outros dois com o `0x0E`); a `GameScene` começa pela tela de carga (`WorldLoading`, quadros de 90 ms pelo relógio, um por quadro da tela no máximo) e, no fim da primeira parte, manda o `0x01` (nome do personagem + usuário) e o `0x16`/`0x18` no channel. O `0x0E` do servidor é **ecoado** (a semente fica guardada para o `0xE6`); chegam `0x02`, `0x03`, `0x7E` × 7, `0x7F` × 7, `0x05`, `0xE6`, `0x74`, `0x152`, `0xB4`, `0x73` (os sem uso só têm o **tamanho** conferido, e um tamanho errado derruba a sessão).
4. Com `0x02` e `0x03` a `GameScene` começa a segunda parte da tela de carga, carrega o mapa (`Map` → coluna 4, `map\<código>.map`) e, quando ela termina (mapa carregado e 16 quadros), manda `0x04`, `0xF0` e, na primeira entrada da execução, `0x121`; abre o global (`0x64`, `0xDE` × 2). Mapa sem linha, sem arquivo ou corrompido = mundo perdido (`LoadMap Error`), sem enviar nada.
5. O `0xC4` do servidor é respondido na hora (✅ capturas: 5,35 s e 10,54 s).

**Layouts tirados das capturas** (sessão 52, conexões 87 a 89; o RE do cliente não leu a montagem) ✅ conferidos byte a byte, ❓ nos campos que são zero ou constante:
- `0xF0` **C2S** = pedido de renomear, 42 B: `u8 0, u8 tipo (1 tamer, 2 digimon), u16 0, u32 uid, nome[20], 14 B`; na entrada o cliente manda `(tamer, uid 0, sem nome)`. O de 28 B é o S2C de resposta.
- `0x04` C2S: o id de 16 B do personagem (o serial do `0x02`) e 14 B. `0x121` C2S: 14 B zero. `0x49` C2S: 18 B, o primeiro é 1 (❓).
- `0x0E` C2S: a semente recebida e 14 B zero. `0x16` C2S (55 B): usuário[21], personagem[21], 13 B zero ❓. `0x18` C2S (74 B): três campos de 20 B com os MAC `AA-BB-CC-DD-EE-FF` e 14 B zero. Decisão do dono: **MAC reais**, só no pacote, nunca em log.
- Global `0x64` C2S (114 B): usuário[21], personagem[21], zeros, `u32 3, u32 1, u32 1` ❓ e o uid do personagem no byte 96; `0xDE` C2S (50 B): `u32 3`, personagem[20], `u32 x`, `u32 y` (a célula do tamer), duas vezes.

**Provisório (❓)** no código, para a conferência lado a lado: curva do clareamento (escuro até 100, linear até 200), âncora dos objetos do mapa, véu `0x88bcf8`, nome do servidor (texto do link do canal), passo da lógica de 60/s no lugar de `DAT_0135af08 = 30`, saída da tela de conexão perdida (um clique volta à cena 2) e bit 8 das células do `.map`.

**Corrigido ao testar**: o contador do clareamento (`+0x34c`) zerava ao chegar em 200 e o passo seguinte o somava de novo, escurecendo a tela outra vez em ciclo; agora 0 = terminado.
