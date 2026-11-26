# Plano: cliente novo (source port)

> **Em andamento.** O PR #41 foi mergeado em 2026-11-26. O marco 1 (Loading → Login) está no PR `feat/client-m1-login-screen`.

Objetivo: um cliente novo que lê os assets da instalação oficial (`resource.pf`, `table.pf`), fala o protocolo **v322** e se comporta como o original, com resolução livre, fontes TTF e nada preso a tecnologia legada. É o "nível 4" de [qualidade de vida](../cliente/qualidade-de-vida.md). O fã-cliente [Reboot](../cliente/reboot-analise.md) prova que o caminho funciona; **nada dele é usado** (licença).

## Decisões de base
Ver [decisões](../projeto/decisoes.md) de 2026-11-25.
- **Reescrever, não recompilar.** O `DigimonRPG.bin.orig` decompilado no Ghidra serve de **especificação**: cada função lida tem o endereço citado na wiki. As mecânicas são do servidor (emulador); o cliente desenha e manda input.
- **Linguagem: C# .NET 10 nos dois lados.** Uma biblioteca `DRPG.Shared` (protocolo, structs, tabelas, formatos de arquivo) é usada pelo cliente e pelo emulador.
- **Plataformas:** Windows e Linux, x64 e arm64. Cliente publicado com **NativeAOT** (um executável nativo, sem runtime instalado).
- **Gráficos: SDL3 + SDL_GPU** (backends Vulkan, D3D12, Metal). Sem OpenGL, D3D9/11 ou DirectDraw. SDL3 também cuida de janela, input, gamepad, áudio e DPI.

## Requisitos do dono (fase inicial, até segunda ordem)
Definidos em 2026-11-26 ([decisões](../projeto/decisoes.md)).
1. **Substituto direto do binário.** Basta copiar o executável para a pasta da instalação oficial e ele funciona. Lê os arquivos como estão (`resource.hf/pf`, `table.hf/pf`, `Language/`, `Sound/`, `Fonts/`, `option.ini`, `VersionInfo.ini`) e **não altera nenhum deles**. Aceita a mesma linha de comando do `DigimonRPG.bin` (`<usuário> <hash>`, ver [servidores](../protocolo/servidores.md)).
2. **Visual e comportamento idênticos ao original**, ou o mais perto possível: as mesmas telas, posições, tempos, sons e fluxo. Qualquer diferença visível só entra **quando o dono pedir explicitamente** e fica registrada nas decisões.
3. **Diferenças aceitas desde já:**
   - **técnicas, não visuais:** 64 bits, áudio e vídeo modernos, SDL3, NativeAOT, multiplataforma;
   - **janela maior e tela cheia:** o comportamento original é mantido. A tela maior só **mostra mais mundo**, e cada elemento da UI fica ancorado numa borda, num canto ou no centro. Telas fixas em 4:3 (login, seleção de personagem, loading) são **centralizadas** com bordas pretas até um rework próprio.
4. **Renderização atrás de uma interface própria** (`IRenderer`, em `Rendering/`). A primeira implementação é SDL_GPU (`SdlGpuRenderer`). Nenhum código acima de `Rendering/` conhece SDL_GPU, para que Vulkan ou D3D12 direto possam entrar depois como outra implementação.
5. **Tudo em inglês no código:** nomes, comentários, mensagens de log e de exceção. A wiki continua em português.
6. **Engenharia reversa à mão:** o comportamento original é consultado no [dump de RE](../ferramentas/dump-re.md) (código decompilado em arquivos, pesquisável com `grep`), e o endereço da função é citado no código e na wiki.

## Stack
| Peça | Escolha |
|---|---|
| Janela, input, áudio | SDL3 (bindings C#) |
| Renderização | `IRenderer` → `SdlGpuRenderer` (SDL_GPU) + sprite batcher próprio (quads, atlas, blend normal/aditivo, shaders compilados offline para SPIR-V/DXIL/MSL) |
| Texto | FreeType (+ HarfBuzz se precisar) ou FontStashSharp com renderer próprio; contorno e sombra |
| Áudio | SDL3 + decodificador Ogg Vorbis (o `Sound/` é `.ogg`/`.wav`) |
| Vídeo | ❓ o original tem vídeos de skill: identificar formato antes de escolher decoder |
| UI | própria, ancorada, com escala (1×, 1,5×, 2×) |

## Regras de desempenho (para nunca precisar trocar de linguagem)
- **Orçamento medido desde o marco 2:** cliente a 60 FPS estáveis com p99 do frame < 16,6 ms; emulador com p99 do tick do mundo bem abaixo de 344 ms sob carga.
- **Teste de carga do emulador** com bots (base: `drpg_replay.py`), meta inicial de 1000 conexões simultâneas num mundo. Roda antes de cada marco.
- **Zero alocação no caminho quente** (loop de frame, framer, tick): `Span<T>`, `ArrayPool`, structs, sem LINQ e sem `async` dentro do frame. Medir com `dotnet-counters`/`dotnet-trace` (contagem de GC por segundo).
- Cliente: GC em modo de baixa latência (`SustainedLowLatency`), assets carregados fora da thread de render.
- Se um ponto quente aparecer, otimiza-se **aquele ponto** (ou vira código nativo via P/Invoke). Nunca reescrever tudo.

## RE necessária (por ordem)
| # | O quê | Temos? |
|---|---|---|
| 1 | Pacote `resource.hf/pf` | ✅ `drpg_pack.py` |
| 2 | Tabelas `table/*.dat`, `language/*.bin` | ✅ parcial (`ClientPack.cs`) |
| 3 | `.map`: grade | ✅ ([mapas](../cliente/mapas.md)); ❓ camadas, objetos, portais visuais |
| 4 | **`.spr`** (5978): cabeçalho, frames, pixel, paleta/colorkey/alpha, âncoras | ❌ |
| 5 | **`.ani`** (687): frames, tempos, offsets, eventos | ❌ |
| 6 | **`.til`** (683) e **`.obj`** (1180): tiles, objetos, ordem de desenho (y-sort) | ❌ |
| 7 | Layout de cada janela (`CWindow_*`, 985 classes com RTTI) | ❌ |
| 8 | Handlers S2C (246 opcodes) e envios C2S | parcial ([opcodes](../protocolo/opcodes.md)) |
| 9 | Lógica de cliente: caminho do clique, interpolação do `0x20`, ritmo `0x68 → 0x67 → 0x69` | ❌ |
| 10 | Efeitos de skill (recortes, aditivo) e vídeos | ❌ |

## Arquitetura
```
DRPG.Shared   protocolo, structs, tabelas, formatos (.pf/.spr/.ani/.map)  ← também usada pelo emulador
DRPG.Client   loop SDL3 + SDL_GPU
 ├─ Assets    VFS sobre resource.pf, cache de textura, carga assíncrona
 ├─ Net       login/channel/map/global, fila de pacotes → thread do jogo
 ├─ Scenes    Login → Servidor → Channel → Loading → Mapa ↔ Batalha
 ├─ World     câmera (zoom, viewport livre), tiles, objetos, entidades, y-sort
 ├─ Battle    slots, barra de ação, animações
 └─ UI        janelas ancoradas, escala, texto TTF, chat, atalhos
```
- Viewport maior exige *area of interest* configurável no emulador.
- Modo padrão idêntico ao original (UI 1×, mesmas posições e sons); modernidades ficam nas opções.
- Arte: filtro *point* em escala inteira; upscale (xBRZ/ESRGAN) só opcional, gerado na máquina do usuário, nunca distribuído.

## Marcos (cada um jogável contra o emulador)
0. **Monorepo** ([arquitetura](../arquitetura/README.md#repositório)): `DRPG.Shared` (protocolo, pacotes de mão dupla, `ClientPack`), `DRPG.Client`, `DRPG.Architecture.Tests`, `.editorconfig`, analyzers e `BannedSymbols.txt` ([garantias](../arquitetura/padroes-de-codigo.md#como-as-regras-são-garantidas)). A renomeação `DRPGServer` → `DRPG.Server` pode ficar para um PR próprio. O cliente segue a [arquitetura do cliente](../arquitetura/cliente.md).
1. **Loading → Login** (PR `feat/client-m1-login-screen`): o executável na pasta do jogo abre a janela, mostra a tela de loading e depois a tela de login, idênticas ao original, lendo `.spr` direto do `resource.pf`. Ainda sem rede. Valida SDL_GPU + NativeAOT em Windows e Linux. Pronto quando a captura de tela bate com a do original lado a lado.
2. Login de verdade → servidor → channel → mapa; andar, ver jogadores e selvagens. Primeira medição de desempenho.
3. HUD e chat com TTF; resolução, tela cheia e escala de UI.
4. Batalha PvE completa.
5. Janelas uma a uma (inventário, loja, depósito, evolução, party, quests), comparando com o original.
6. Efeitos de skill, cenas de evolução, sons, vídeos.

O visualizador de assets (`.spr`/`.ani`/`.til`/`.obj`/`.map` → PNG) entra em `DRPG.Tools` junto com cada leitor de formato, a partir do marco 1.

## Como garantir que é "o mesmo jogo"
1. Original rodando ao lado ([Wine](../cliente/wine.md)): cada tela comparada por screenshot e pcap.
2. Lógica de cliente vem do original decompilado, com endereço citado.
3. O emulador decide tudo que o oficial decide no servidor.

## Riscos
- **Volume de UI:** centenas de janelas; é a maior parte do trabalho.
- Desenho fino (blend, recorte, ordem) só aparece comparando com o original.
- Bindings de SDL3 para C# e SDL_GPU são recentes: validar no marco 1 em Linux arm64 e Windows arm64.
- Dapper usa reflexão: não importa no servidor (não precisa de AOT), mas `DRPG.Shared` deve ser compatível com AOT (sem reflexão).
- [Legal](../projeto/legal.md): o cliente não leva arte; lê a instalação oficial do usuário.

## Launcher (login + auto-patcher)
Decisão de 2026-11-26. **Não existe** tela de login com ID e senha no binário v322 (confirmado pelo dono). A autenticação é só pela linha de comando: `0x00aa24b0` exige `<usuário> <hash>` e fecha o jogo com qualquer outra coisa ([dump de RE](../ferramentas/dump-re.md)). A tela de login fica num **launcher próprio**, que imita a tela de login antiga do jogo.
- **Arte:** os sprites antigos que continuam no `resource.hf`, lidos 1:1: `gui\dr2_in_000_*` a `gui\dr2_in_003_*` (34 arquivos) e `gui\dr_title_000..007.spr` (10). Os dois grupos incluem as variantes `_eng.spr`. Layout, posições e comportamento vêm dos sprites e da memória do dono. ❓ Não há código original para consultar.
- **Funções:** (1) login com ID e senha, (2) auto-patcher, (3) inicia o jogo com `<usuário> <hash>`. Com esse contrato, o launcher abre **tanto o cliente novo quanto o `DigimonRPG.bin` original**.
- **Projeto:** `src/DRPG.Launcher` (NativeAOT), com as mesmas camadas `Platform/`, `Rendering/` (`IRenderer`) e `Assets/` do cliente. Elas saem para uma biblioteca comum (❓ `DRPG.Engine`) quando o segundo executável precisar delas.
- **Segurança:** a senha nunca vai em texto puro na linha de comando nem para o log. O patcher só aplica arquivos assinados e com hash conferido. Os arquivos originais só mudam por patch explícito.
- ❓ **Em aberto (decisão do dono):** de onde vem o `<hash>` (token de sessão emitido pelo servidor ou hash da senha), qual servidor autentica (o login atual do emulador ou um endpoint HTTP), formato do manifesto de patch e servidor de arquivos, e se o launcher entra no marco 1 ou num marco próprio.

## Distribuição e assinatura (falso positivo de antivírus)
O medo é o Defender acusar `Trojan:Win32/Wacatac` e apagar o executável, o que é comum em builds .NET. Essa detecção é uma heurística do **Defender** (aprendizado de máquina) que coloca o arquivo em quarentena. O **SmartScreen** só avisa sobre "aplicativo não reconhecido" e não apaga nada.

Causa típica em C#: o `PublishSingleFile`, que é um stub nativo com DLLs anexadas e às vezes comprimidas (parece um *dropper*), sem assinatura, com hash novo a cada build. Ofuscador e UPX pioram o quadro.

Regras do cliente:
1. **NativeAOT**, que gera um PE nativo comum, parecido com um executável C++. **Proibidos:** `PublishSingleFile`, `EnableCompressionInSingleFile`, UPX, ofuscadores e extração de DLL em tempo de execução. As DLLs nativas (SDL3 etc.) ficam soltas ao lado do executável.
2. **Recurso de versão preenchido** (Company, Product, FileDescription, FileVersion), manifest e ícone.
3. **Assinatura Authenticode** com *timestamp* no `.exe`, nas DLLs próprias e no instalador, para que a reputação fique no certificado e não em cada hash. Opções: SignPath Foundation (grátis para projeto open source), Certum Open Source Code Signing (barato, aceita pessoa física no Brasil), Azure Artifact Signing (antigo Trusted Signing, cerca de US$ 10/mês; ❓ elegibilidade de pessoa física fora de EUA/Canadá) ou OV comercial. EV não dá mais reputação instantânea desde 2024.
4. **Barreira no CI:** um runner Windows atualiza as definições (`MpCmdRun -SignatureUpdate`) e escaneia o artefato (`MpCmdRun -Scan -ScanType 3 -File <pasta>`). Se houver detecção, não há release.
5. **Se ainda houver detecção:** enviar como falso positivo no portal WDSI da Microsoft (opção *Software developer*). A correção chega a todos pela atualização de definições.
6. **No código:** um auto-updater só baixa e executa arquivos assinados e confere a assinatura antes. Nada de injeção, hook ou escrita em memória de outro processo.
