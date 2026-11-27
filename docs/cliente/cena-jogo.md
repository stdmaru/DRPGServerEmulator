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

## Ainda ❓ (fase A em andamento)
- Mapa: objeto `DAT_011584f8`, carga do `.map`/`.til`/`.obj` (quando: provavelmente ao receber o `0x02`), câmera `0x00b36c20`, desenho de tiles e objetos, y-sort.
- Tamer e digimon no mapa (`0x00ad4a50`), direção e animação parada.
- UI básica da entrada (`0x00a45230`, `0x009b6010`), BGM do mapa.
- Montagem de `0x16`, `0x18`, `0x49`, `0x01`, `0x04`, `0xF0`, `0x121` e da conexão global.
