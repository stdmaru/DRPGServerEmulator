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
   - conectou → `0x00b8e690(0)`, **troca para a cena 5** e `[DAT_0135a1dc]+0x10 = 1`;
   - não conectou → `+0x864 = 1`: mensagem `0xee6` "The server is under maintenance. Please try again later. 12" por 80 quadros, continua na seleção.
3. ✅ Pelas capturas (sessão 52), **antes** de conectar no mapa o cliente manda no channel `0x16` (`MapConnectionConfirm`, 69 B) e `0x18` (`ClientHardwareInfo`, 88 B: endereços MAC). ❓ Onde são montados (candidatos: `0x00adb4f0`, `0x00ae8700` para `0x16`). ✅ Decisão do dono: o cliente novo manda os **MACs reais**, como o original (1:1). Só no pacote; nunca em log.

## Sequência na rede (✅ captura `v322 login até o mapa`, conexões 87–89)
Tempo a partir da conexão no mapa:

| t (s) | Conexão | Sentido | Pacotes |
|---|---|---|---|
| −0,11 | channel | C2S | `0x16`, `0x18` |
| 0,000 | map | C2S | `0x49` × 3 (32 B), `0x01` (71 B, entrar com o personagem) |
| 0,156 | map | S2C | `0x0E` hello (30 B) |
| 0,165 | map | C2S | `0x0E` eco (44 B) |
| 0,257–0,904 | map | S2C | `0x33` × 3, `0x02` (personagem, 7614 B), `0x03` (inventário, 4430 B), `0x7E` × 7, `0x7F` × 7, `0x05`, `0xE6`, `0x74`, `0x152`, `0xB4` (digimon líder), `0x73` |
| 1,952 | map | C2S | `0x04`, `0xF0`, `0x121` (❓ provavelmente no fim da carga do mapa: ~1 s depois do último S2C) |
| 2,120 | global | C2S | `0x64` (128 B), `0xDE` × 2 → S2C `0x0E`, C2S `0x0E`, `0x7B`, `0x7C` → S2C `0xE1`, `0x65`, `0x7B`, `0x7C` |
| 2,120–2,280 | map | S2C | `0x10`, `0xF0`, `0x121` (10318 B), depois `0x11` em rajadas (selvagens) |
| 5,35 | map | S2C/C2S | `0xC4` keep-alive (~5 s) |

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
  1. `0x00ad3730`: modo `[DAT_01158504]+0x80 = 1` (campo); **carrega o mapa** `0x00b37860(id)` → vtable `+4` do objeto de mapa `0x00b378f0` (`map\%s.map`, ver [mapas](mapas.md)); cria os NPCs do mapa pela lista `+0x2d18` (`0x00abb750`) e outras preparações (❓).
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
- ✅ **Desenho do tamer** (fila tipo 3 → `0x00864810`): o mesmo **boneco de papel `0x0099fb90`** da seleção de personagens ([cenas](cenas.md)), com (tamer = vtable `+0x20`, x = `+0x170`, y = `+0x174`, quadro `+0x184`, equipamentos `+0x224`, início da animação, −1); o corpo e as roupas vão pelo canto superior esquerdo em **(x − 120, y − 162)**, sem âncora. Depois o tipo 3 desenha o nome (`0x00a10e10` em (`+0x170`, `+0x174 − +0x18c − 30`), ❓ fonte/cor) quando `+0x128 > 0`.
- ✅ **Desenho do digimon** (fila tipo 4 → vtable `+0x3c` da classe `0x01007310` = `0x007e79d0` → caso normal `0x007e83d0`): sprite da **espécie `+0x814`** (dados do `0x02` `+0x4ec`) pela [tabela por espécie](tabelas-de-especie.md) (`0x00b23d20`), quadro `+0x184`, **com a âncora do `.ani`**: canto em **(x − cx[quadro], y − cy[quadro])** (`0x0086d200`/`0x0086d220`); espécies `0x149` e `0x3e9` descem mais 32 px. Antes, com a opção de sombra (`[DAT_01158530]+2`), a sombra `0x00b57460(…, 0xe)` (4 no mapa `0x6d`); e, fora das animações 9/10, `0x00b57330` (❓ contorno pela opção `[DAT_01158530]+0`). Mapas `0x61`, `0x66`, `0x67`, `0x6d` usam outros modos (❓, fora deste marco).
- ✅ **Animação** (por passo de lógica; tamer `0x00867a60`/`0x00867790`, digimon `0x007f4cf0`/`0x007f4080`):
  1. quadro `+0x184` = início(animação `+0x180`, direção `+0x164`) + deslocamento `+0x182`;
  2. duração do quadro `+0x186` = o **primeiro campo do quadro no [`.ani`](formato-ani.md)** (`0x00873140`; as espécies-sprite `0x11b`, `0x11c`, `0x15d`, `0x343`, `0x360` usam 2);
  3. contador `+0x188`: se `contador ≥ duração` → deslocamento + 1 e contador 0; senão contador + 1 (cada quadro fica **duração + 1 passos**);
  4. deslocamento > contagem − 1 → volta a 0 (parado) — no mapa **todos os quadros aparecem**, ao contrário da seleção de personagens.
  - ✅ Digimon parado: contador `+0x1a0` + 1 por passo; no passo 200 troca para a animação **3** (1 chance em 4) ou **2**; no passo 320 volta para a 1 e zera (as espécies `0xfb` não trocam). Trocar de animação (`0x0085e4c0`) zera deslocamento e contador e recalcula o quadro.
- ✅ **Passo de lógica**: `DAT_0135af08 = 30` (`0x00867ed0(30)`, objeto de tempo `0x135aed8`), o divisor do acumulador no quadro da cena. ❓ A unidade do tempo somado (`0x00867e40`) ainda não foi lida; o emulador calibrou o movimento a 60 passos/s.

## Câmera
- ✅ `0x00afe310` (campo): chama a vtable `+0x24` do personagem e põe a rolagem do mapa (`0x00b36b80`) em **(`+0x168` − 400, `+0x16c` − 300)**, presa a `0..largura·64 − 800` e `0..altura·32 − 600`. Na batalha a rolagem é fixa (250, 206).

## Ainda ❓ (fase A em andamento)
- UI básica da entrada (`0x00a45230`, `0x009b6010`, `0x00b42f60`, `0x00a0ceb0`, `0x007dca50`), BGM do mapa.
- Montagem de `0x16`, `0x18`, `0x49`, `0x01`, resto do `0xF0`, e da conexão global (`0x64`).
- Nome sobre o tamer (fonte, cor), sombra (opção) e o `0x00b57330`.
