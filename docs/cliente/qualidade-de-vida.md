# Cliente: qualidade de vida

Problema principal: a resolução é fixa em 800×600. No código há 134 imediatos `0x320` (800) e 115 `0x258` (600), fora as constantes derivadas (400/300 para centralizar) e as posições dos layouts de GUI. O renderer é DirectDraw 7 ([binário](binario.md)).

Decisão: **não** decompilar e recompilar tudo de uma vez (ver [decisões](../projeto/decisoes.md)). O caminho tem três níveis:

| Nível | O que é | Resultado | Esforço |
|---|---|---|---|
| 1. Wrapper DirectDraw | Trocar `ddraw.dll` pelo **cnc-ddraw** (open source) | Janela, borderless ou tela cheia em qualquer resolução, com upscale (integer/bilinear/shader), vsync, Windows moderno e Wine. **Não aumenta o campo de visão.** | dias |
| 2. DLL de hooks | DLL injetada (proxy de `version.dll`/`dinput`) + MinHook | Viewport maior de verdade, UI ancorada nas bordas, correção de bugs, limite de FPS, melhorias de chat | semanas por feature |
| 3. Decompilação incremental | Na mesma DLL, trocar funções originais por C++ uma de cada vez, testando no jogo (abordagem OpenRCT2/re3) | O binário vira um "host" e, no fim, um source port (DirectDraw → SDL2/D3D11) | contínuo |

Notas:
- Começar o nível 3 por `NetworkMgr` e pelos handlers de pacote. Isso também alimenta o [catálogo de opcodes](../protocolo/opcodes.md).
- Com viewport maior, o servidor precisa enviar entidades num raio maior (area of interest). No emulador isso é um parâmetro.
- O compilador original é o VS2017 (v141). Use o mesmo toolset na DLL para manter a compatibilidade de CRT e ABI.
