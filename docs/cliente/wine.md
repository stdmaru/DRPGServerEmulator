# Rodar o cliente no Wine

Todo o trabalho é feito em Linux. O cliente é Windows 32-bit e roda no Wine (testado com wine-11.22).
Script: `work/tools/client/drpg_wine.sh`.

```bash
cd work/tools/client
./drpg_wine.sh setup            # cria um prefixo dedicado (~/.local/share/wineprefixes/digimonrpg)
./drpg_wine.sh run              # roda DigimonRPG.bin com os argumentos do client/start_bin.bat (driver Wayland)
./drpg_wine.sh run --debug      # idem, com WINEDEBUG detalhado (+seh,+loaddll,+threadname,warn+ddraw…)
./drpg_wine.sh hang             # com o jogo TRAVADO: backtrace de todas as threads + minidump
./drpg_wine.sh kill             # wineserver -k
```
- Logs ficam em `work/logs/wine/` (`run-*.log`, `hang-*.txt`, `hang-*.mdmp`).
- Configuração opcional em `tools/client/wine.env` (`DRPG_PREFIX`, `DRPG_CLIENT`, `DRPG_BIN`, `DRPG_ARGS`, `DRPG_WINEDEBUG`, `DRPG_DRIVER`). Para usar o prefixo atual: `DRPG_PREFIX=$HOME/.wine`.
- O `run` avisa se o binário estiver empacotado ou com o XIGNCODE ativo (ver [patch_client](../ferramentas/patch-cliente.md)).
- Se existir `client/ddraw.dll` (cnc-ddraw), o `run` liga o override `ddraw=n,b` automaticamente (ver [qualidade de vida](qualidade-de-vida.md)).
- **Driver gráfico:** o `run` usa o driver **Wayland** do Wine (`winewayland.drv`, rodando sem `DISPLAY`) quando há `WAYLAND_DISPLAY`; `DRPG_DRIVER=x11` força o X11. Os dois funcionam igual para o jogo; o driver **não** era a causa do congelamento. Para trocar de driver, rode `./drpg_wine.sh kill` antes: o `explorer.exe` do prefixo guarda o driver da primeira execução.
- Inicialização do jogo: `DigimonRPG.bin <conta> <hash>` (`start_bin.bat`). ⚠️ O `start_bin.bat` atual tem a conta errada (sem o sufixo). A conta certa é a do `start_launcher.bat`; com a errada, o login responde `0x05` com resultado 2 e código 0x68. O launcher usa `DigimonRPG.exe nadigimonrpg:<hash>;<conta>;` (`start_launcher.bat`).

## Prefixo
- O `~/.wine` atual é compartilhado e muito customizado (dxvk, d3d9/d3d11 nativos, várias fontes e runtimes). Para diagnosticar o jogo, use o **prefixo dedicado**: um prefixo limpo elimina uma variável.
- O XIGNCODE em execução sob Wine cria arquivos com nome `XIGNCODE:{GUID}` na pasta do cliente (alternate data streams viram arquivos literais no Linux). São lixo e podem ser apagados.

## Problema: tela congelada no loading
**Estado:** ✅ resolvido pelo patch `loadingbar` do [patch_client](../ferramentas/patch-cliente.md), aplicado por padrão. Causa: **duas threads do jogo usando o mesmo backbuffer DirectDraw sem trava**. Não é bug do Wine; o Wine só expõe a corrida com muito mais frequência que o Windows.

Sintoma: a barra para em ~30% e não sai mais. Raramente passava.

O que acontece (✅ verificado com trace `+ddraw`, breakpoints no `winedbg` e leitura da memória):
1. Durante o loading, a "PreLoadingBar Thread" (`0xB34B60` → loop `0xB355F0`) desenha a tela de loading **inteira**, fundo e barra, no backbuffer (`Lock`/`Unlock` e `GetDC` de sistema, 16 bits, 800×600). Não há limitador efetivo: ~2000 apresentações por segundo.
2. Ao mesmo tempo, a thread principal carrega os dados (`0xAD3D30`) e faz `GetDC`/`ReleaseDC` **no mesmo backbuffer**. Os wrappers de Lock (`0xB56930`/`0xB56970`) ainda guardam a descrição da superfície num buffer compartilhado do objeto de desenho (`[this+0x5C]`, pixels em `+0x80`).
3. A intercalação gera "Unlock Error" (`0xB576E0`/`0xB57700`, string `0x102FA20`). Às vezes o backbuffer fica com um mapeamento sem par.
4. Daí em diante, o `BltFast` backbuffer → superfície intermediária da apresentação (`0x7DAF60`, retorno em `0x7DB047`) retorna **`0x887601AE` (`DDERR_SURFACEBUSY`) em todo quadro**. A intermediária mantém o último quadro da barra, que é reapresentado para sempre. Mover ou redimensionar a janela não muda nada.
5. Por trás, o jogo segue: o carregador termina (`[0x1158504]+0x81 = 1`), a thread da barra grava `+0x654 = 1` e sai, e a `CSceneTitle` (`0xB54CC0`) troca para a **`CSceneServer`** (seleção de servidor, render em `0xB54240`). O login já deu certo.

Correção `loadingbar`: a thread da barra pula o desenho (`jmp` de `0xB356BC` para a checagem de conclusão em `0xB358D0`) e troca a apresentação por `Sleep(16)`. Ela só confere o fim do carregamento e grava `+0x654`. Efeito colateral: **a tela fica preta durante o loading** (alguns segundos) e abre direto na seleção de servidor.

Testes (2026-11-21, v322 sem XIGNCODE, prefixo dedicado):
| Configuração | Resultado |
|---|---|
| sem `loadingbar` (X11 ou Wayland; GL, gdi; desktop virtual; login atrasado) | congela quase sempre (`SURFACEBUSY`) |
| `renderer=vulkan` ou desktop virtual | tela preta (outro problema, não investigado) |
| **com `loadingbar`** | ✅ chega na seleção de servidor |

Bateria com o detector (`BltFast` em `0x7DB047`, 30 s após abrir, driver Wayland): **com `loadingbar`: 5 de 6 ok**, e 1 em que o jogo fechou sozinho em ~8 s (`Mismatched CoUninitialize` no log, sem congelamento; ❓ causa, talvez login repetido em sequência). **Sem `loadingbar`: 3 de 3 congelados** (`0x887601AE`). O usuário confirmou na tela: preto durante o loading, depois a seleção de servidor.

❓ Pendências:
- A "PreMap Loading Thread" (criador `0xB34C10`, proc `0xB34B80` → `0xB34EA0`, flag `+0x5E4`) tem o mesmo desenho. Pode congelar na **troca de mapa** pelo mesmo motivo. Verificar quando chegar no mapa.
- Melhoria futura: desenhar a tela de loading uma vez, ou serializar o acesso ao backbuffer, em vez de deixar preto.

### Corrida na criação da thread da barra (real, mas não é a causa)
O criador (`0xB34BA0`, chamado em `0xAA2D1D`) faz `CreateThread` e **só depois** grava `byte [this+0x63C] = 1` (`0xB34C01`). A thread confere o flag em `0xB355C7` e sai se ainda for 0; nesse caso ninguém grava `+0x654`. Patch opcional `--enable loadingrace`. Não muda o congelamento.

Campos do objeto da barra (`[0x135B150]`): `+0x9` ativo, `+0x638` handle da thread, `+0x63C` rodando, `+0x644` progresso alvo (float), `+0x64C` progresso desenhado, `+0x654` concluído.

### Como investigar de novo
```bash
./drpg_wine.sh run --debug        # terminal 1
./drpg_wine.sh hang               # terminal 2: backtrace de todas as threads + minidump
```
- **Detector de congelamento:** breakpoint em `0x7DB047`. `eax = 0x887601AE` significa congelado; `0` significa ok.
  `printf 'attach 0xPID\nbreak *0x7db047\ncont\np $eax\ndelete 1\ndetach\nquit\n' | winedbg`
- Ler memória: `x /x 0x135b150` no `winedbg`. O `/proc/PID/mem` é bloqueado pelo `ptrace_scope=1`. Não use `list` no `winedbg` em modo script: ele trava com o jogo suspenso.
- Um backtrace da thread principal em `0xB54267`/`0xB550FF` não indica travamento: é o quadro normal da `CSceneServer`.
- O erro de GL "required buffer is missing" aparece em toda apresentação, inclusive nas boas. É ruído.
- `DRPG_WINEDEBUG='+timestamp,+tid,+ddraw,+debugstr,+thread'` mostra Lock/GetDC por thread; casar por `iface`.
