# Cliente "Digimon Reboot" (fãs): o que é

Análise estática de 2026-11-25 da pasta `~/Downloads/Digimon Reboot/` (versão 1.0.31 pelo `noticias.txt`). Só cabeçalho PE, seções e strings (ASCII e UTF-16). **Nada foi decompilado nem desempacotado**, e não deve ser: ver [licença](#licença-do-reboot).

## Veredito
✅ **Reescrita do zero** (source port / reimplementação). Não é o executável original modificado nem uma recompilação dele. Mantém o jogo porque:
1. **lê a arte original** (`.spr`, `.ani`, `.map`, tabelas `*_us.dat`/`.bin`), reempacotada num formato próprio;
2. **fala o protocolo do original** (assinatura `0xCC`, os mesmos opcodes) com um servidor próprio;
3. o comportamento de cada tela foi copiado **olhando o binário original** (as mensagens de log citam endereços dele).

| | Cliente original (nosso, v322) | Reboot |
|---|---|---|
| Binário | PE32 **x86**, MSVC 2017, C++ | PE32+ **x64**, **C# .NET 8 NativeAOT** (`.managed`, `hydrated`) |
| Gráficos | DirectDraw 7, 800×600 fixo | **MonoGame 3.8.5.1 DesktopGL** (SDL2 + OpenGL), resolução livre, tela cheia, zoom de câmera |
| Fonte | bitmap/GDI do jogo | **FontStashSharp + StbTrueTypeSharp** (TTF rasterizado em runtime, qualquer tamanho) |
| Som | DirectSound | OpenAL (`openal.dll`) + **NVorbis** (`.ogg`) |
| Imagens | — | StbImageSharp / StbImageWriteSharp (screenshot `.png`/`.jpg`) |
| Rede | TCP, 14 bytes de cabeçalho, sem cifra | TCP + login por HTTPS (`/digimon/api/Login.php`), "máscara de frame" negociada no login, **BouncyCastle** (Ed25519) |
| Assets | `resource.hf/pf` (11,8 GB), `table.hf/pf` | `Assets.dpk` (11,8 GB, 9754 entradas), `Dados.dpk` (tabelas, `Language/`, `Quest/`), `Guide.dpk`, `Tabelas.dpk`, `zz_patch_NNNN.dpk` |
| Proteção | XIGNCODE + Themida | pacotes `DROPACK` cifrados/assinados, `login.dat` em DPAPI, `servidores.assinado` (base64 + assinatura) |
| Launcher | `DigimonRPG.exe` | `Launcher.exe` .NET (WPF/WinForms, 75 MB self-contained), patch HTTP próprio |

## Evidências
- **Runtime:** seções `.managed`/`hydrated` (marca do NativeAOT), `.NETCoreApp,Version=v8.0`, `8.0.24`; assembly do jogo `Digimon RPG Online, Version=1.0.0.0`. Caminho de build no `launcher_cache.json`: `L:\Digimon Rpg\NovoClient\dist\TESTE_AOT\`.
- **Bibliotecas** (metadados de assembly): MonoGame.Framework 3.8.5.1, FontStashSharp 1.2.3 / MonoGame 1.5.6, StbTrueTypeSharp 1.26.12, NVorbis 0.10.4, BouncyCastle 2.4.0, System.Text.Json, System.Net.Http.
- **Código próprio em português:** centenas de tipos `*Ui`, `*Net`, `*Ctx`, `*Janela` (ex.: `ArmazemJanela`, `AutoBattleUi`, `DigiXrosNet`, `LojaNpcUi`, `MapGame`, `SprSheet`, `GuiRenderer`, `InPacket`/`OutPacket`). Padrão por feature: **Net** (pacotes) + **Ctx** (estado) + **Ui** (desenho).
- **Assets originais:** literais `dr2_main_000_018.spr`, `icon_skikill.spr`, `Map_icon_01.spr`, `effect_portal.spr`, `^\d+_\d+\.ani`, `item_info_us.dat.txt`, `m_pattern_message_us.dat`, "folhas 800x600 em (0,0)". `Assets.dpk` tem o mesmo tamanho do `resource.pf` (11,8 GB).
- **Protocolo original:** logs como `-> 0x35CC`, `0x89CC type=1` (Digimon Depository = nosso `0x89`), `0x16CC`, `0x62CC (party)`, `0x67CC`, `0x69CC`. É o u16 `0xCC` + opcode escrito como um número só. Opcodes extras (`0xA6CC` GM evo, `0xECCC` auto-loot, `0xF8CC` coleção…) são do servidor deles.
- **RE do original como especificação:** "`effId 389 @ 004E36E4` … o handler lê `[esi+8]`", "`[CHATSET] do BINARIO original (5 flags, 0x43818..0x4382C)`", "SPEC 87.1", pasta `re\` com arquivos de "aceito/recusado" e ferramentas `fxator`/`fxpares` para comparar recortes de efeito com o original.
- **Mais que o original:** auto-battle, auto-loot, battle pass, megafone, recompensa de nível com "Digimon X", guia de mobs, comandos de GM, e "resolução ignorada no celular" (alvo móvel também).

## Como eles devem ter lidado com o 800×600
- Mundo: câmera com `zoom` e `viewport` livre (`[CLICKDIAG] cam=… zoom=… viewport=WxH`). O mapa é desenhado em espaço de mundo, então a tela maior mostra mais mapa.
- UI: janelas ancoradas (`LeftWindows`/`RightWindows`, "tela cheia no caminho ancorado").
- Arte de tela cheia 800×600 (evolução, flash, arena) é **escalada** ("escalada pela FxLeiDaTela").
- `ArteNitida`/`IconeFonte`: arte de UI e ícones tratados à parte (provável filtro/escala nítida).
- Texto: TTF em qualquer tamanho, em vez das fontes bitmap.

## Licença do Reboot
O executável traz um aviso: programa proprietário, distribuído só para teste a convite, **proíbe** descompilar, desmontar, extrair ou reutilizar os dados e a arte dos `.dpk`, usar o cliente contra servidor de terceiros e hospedar servidor derivado. Consequências para nós:
- **não** usar código, `.dpk` nem ferramentas do Reboot; **não** apontar o Reboot para o nosso emulador;
- o nosso cliente sai da **nossa** RE do original (o que eles fizeram serve só como prova de que o caminho funciona);
- a arte continua sendo a do cliente oficial, lida do `resource.pf` do próprio usuário ([legal](../projeto/legal.md)).

Plano do nosso: [cliente novo](../planos/cliente-novo.md).
